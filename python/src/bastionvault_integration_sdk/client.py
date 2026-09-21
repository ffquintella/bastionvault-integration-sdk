"""The `Client`: a resolved `ClientConfig`, a transport, and the logical request loop.

`Client` is also the view type `WithNamespace` returns (D-M1b-9): a view shares the
transport, the config and the token cell with its parent, differing only in namespace.
`SetToken`/`ClearToken` (CFG-070) write a thread-safe cell every view shares; each
logical operation snapshots the token once at its start (outside the retry loop) so
in-flight requests keep the token they started with.
"""

from __future__ import annotations

import asyncio
import threading
import uuid
from collections.abc import Mapping
from dataclasses import replace
from datetime import datetime, timedelta
from typing import Any

from ._error_paths import redact
from .auth import AuthOperations, TokenInfo
from .config import ClientConfig, ClientOptions, _validate_namespace
from .environment import EnvironmentSource
from .errors import BastionVaultError, ErrorCodes, make_error
from .httpx_transport import HttpxTransport
from .logical import (
    Logical,
    RawResponse,
    Response,
    _build_headers,
    _build_url,
    _display_path,
    _encode_body,
    _interpret_envelope,
    _interpret_raw,
    _is_login_path,
    _parse_retry_after,
    _present,
)
from .secrets import SecretString
from .token_source import TokenSource
from .transport import (
    Clock,
    JitterSource,
    RateGateState,
    RequestEvent,
    RequestOptions,
    RetryPolicy,
    SystemClock,
    SystemJitterSource,
    Transport,
    TransportRequest,
)

_NEVER_RETRY = frozenset({ErrorCodes.SERVER_SEALED, ErrorCodes.RATE_LIMITED_BY_DOS_GUARD})


class _ClientState:
    """The mutable state a `Client` and every one of its `WithNamespace` views share.

    AUT-001's "exactly one `TokenSource`" cell lives here, so a `with_namespace` view sees
    the same source -- and the same token -- as its parent (CFG-071). `set_token` replaces
    the source rather than writing a token field, which is AUT-001's second sentence.

    CFG-070's thread-safety is the `threading.Lock` this class already held. What changed at
    M2a is that the lock no longer guards a plain field read: D-M2-11 recorded that an
    atomic reference read was `CFG-070`'s entire M1a justification and that an async,
    side-effecting, cache-filling resolve is not one. The lock still guards the *cell*; the
    resolution's own concurrency contract is single-flight, and it lives in `TokenSource`
    (D-M2-11(a)). This is why `CFG-070` is re-opened onto the baseline and owned by M2a
    (D-M2-11(c)).
    """

    def __init__(self, token_source: TokenSource, last_resolved: SecretString | None) -> None:
        self._lock = threading.Lock()
        self._token_source = token_source
        # The most recent value `resolve_token` produced (or the configured token, before
        # the first resolution), which is what AUT-004's `Auth.current_token` reads. Held
        # separately from the source because reading `current_token` must **not** perform a
        # resolution: a `Callback` source would otherwise call the application's coroutine
        # merely because somebody looked at the property.
        self._last_resolved = last_resolved
        self._token_info: "TokenInfo | None" = None
        self._rate_gate_paused_until: datetime | None = None

    @property
    def token_source(self) -> TokenSource:
        with self._lock:
            return self._token_source

    async def resolve_token(self) -> SecretString | None:
        """D-M2-9's resolution call: what replaced the `get_token()` field read.

        The executor calls this once per pass, above the retry loop, and threads the result
        through every attempt of that pass (D-M1b-9, CFG-070). The `await` deliberately
        happens **outside** the lock: `TokenSource.resolve` may perform I/O, and a
        `threading.Lock` held across an await would serialise every concurrent operation on
        the client.
        """
        source = self.token_source
        resolved = await source.resolve()
        with self._lock:
            self._last_resolved = resolved
        return resolved

    def current_token(self) -> SecretString | None:
        """AUT-004: the token the client currently holds, or `None` when it holds none."""
        with self._lock:
            resolved = self._last_resolved
        if resolved is None or not resolved.reveal():
            return None
        return resolved

    def token_info(self) -> "TokenInfo | None":
        with self._lock:
            return self._token_info

    def set_token_info(self, value: "TokenInfo") -> None:
        with self._lock:
            self._token_info = value

    def set_token(self, token: SecretString) -> None:
        """AUT-001: a token write replaces the source with a `Static` one."""
        with self._lock:
            self._token_source = TokenSource.static(token)
            self._last_resolved = token

    def clear_token(self) -> None:
        """CFG-070, and AUT-001: still exactly one source, now a `Static` holding nothing."""
        with self._lock:
            self._token_source = TokenSource.static(SecretString(""))
            self._last_resolved = None

    def pause_rate_gate(self, until: datetime) -> None:
        with self._lock:
            if self._rate_gate_paused_until is None or until > self._rate_gate_paused_until:
                self._rate_gate_paused_until = until

    def rate_gate_state(self, now: datetime) -> RateGateState:
        with self._lock:
            until = self._rate_gate_paused_until
        paused = until is not None and now < until
        return RateGateState(paused=paused, paused_until=until)


class Client:
    """A `BastionVault` client: a resolved `ClientConfig`, a transport, and the token cell."""

    def __init__(
        self,
        options: ClientOptions | None = None,
        *,
        environment: EnvironmentSource | None = None,
        transport: Transport | None = None,
        clock: Clock | None = None,
        jitter_source: JitterSource | None = None,
    ) -> None:
        base_options = options if options is not None else ClientOptions()
        # Never mutate a caller-owned `ClientOptions` (the same hazard CFG-017's
        # headers-aliasing regression guards against, applied to the options object).
        resolved_options = (
            replace(base_options, transport=transport) if transport is not None else base_options
        )
        self._config = ClientConfig.resolve(resolved_options, environment)
        self._clock: Clock = clock if clock is not None else SystemClock()
        self._jitter: JitterSource = jitter_source if jitter_source is not None else SystemJitterSource()
        self._transport: Transport = (
            self._config.transport if self._config.transport is not None else HttpxTransport(self._config)
        )
        if not self._transport.supports_custom_verbs:
            # D-M1b-14/TRN-010: proved via a fake transport that declares `false`.
            raise make_error(ErrorCodes.CONFIG_LIST_VERB_UNSUPPORTED, attempts=0)
        self._namespace = self._config.namespace
        # AUT-001: exactly one source. An application-supplied one is it; otherwise the
        # resolved configuration token becomes a `Static` source, which is byte-for-byte
        # the pre-M2a behaviour. A `Static` source's token is already known, so
        # `Auth.current_token` can report it without resolving; any other source has
        # resolved nothing yet, and reading `current_token` must not be what triggers the
        # first resolution (AUT-004).
        explicit_source = self._config.token_source
        configured = self._config.token if self._config.token is not None else SecretString("")
        self._state = _ClientState(
            explicit_source if explicit_source is not None else TokenSource.static(configured),
            None if explicit_source is not None else self._config.token,
        )

    @property
    def config(self) -> ClientConfig:
        """The fully-resolved configuration this `Client` was constructed with."""
        return self._config

    @property
    def is_insecure(self) -> bool:
        """CFG-018: true when `TlsSkipVerify` disabled certificate verification."""
        return self._config.is_insecure

    @property
    def logical(self) -> Logical:
        """TRN-001: the four logical primitives plus the `Raw` escape hatch."""
        return Logical(self)

    @property
    def auth(self) -> AuthOperations:
        """OVR-008: the `Auth` area -- the token source, the credential, the token store."""
        return AuthOperations(self)

    @property
    def rate_gate_state(self) -> RateGateState:
        """D-M1b-16: the pause half of the client-side rate gate (EFF-006)."""
        return self._state.rate_gate_state(self._clock.now_utc())

    def set_token(self, token: SecretString) -> None:
        """CFG-070: thread-safe; in-flight requests keep the token they started with."""
        self._state.set_token(token)

    def clear_token(self) -> None:
        """CFG-070: thread-safe."""
        self._state.clear_token()

    @property
    def _token_source(self) -> TokenSource:
        """AUT-001's single source, behind `Auth.token_source`."""
        return self._state.token_source

    @property
    def _current_token(self) -> SecretString | None:
        """AUT-004's `Auth.current_token`. Never resolves."""
        return self._state.current_token()

    @property
    def _token_info(self) -> TokenInfo | None:
        """AUT-004's `Auth.token_info`: the most recent `lookup_self` result."""
        return self._state.token_info()

    def _set_token(self, token: SecretString) -> None:
        self._state.set_token(token)

    def _set_token_info(self, value: TokenInfo) -> None:
        self._state.set_token_info(value)

    async def _resolve_token(self) -> SecretString | None:
        return await self._state.resolve_token()

    def with_namespace(self, namespace: str) -> "Client":
        """CFG-071: a lightweight view sharing the transport, config and token cell."""
        validated = _validate_namespace(namespace)
        view = object.__new__(Client)
        view._config = self._config
        view._clock = self._clock
        view._jitter = self._jitter
        view._transport = self._transport
        view._namespace = validated
        view._state = self._state
        return view

    async def _execute(
        self,
        *,
        method: str,
        path: str,
        body: Mapping[str, Any] | None,
        options: RequestOptions | None,
        default_idempotent: bool,
        read_like: bool,
        absolute: bool = False,
        request_id: str | None = None,
        attempts_before: int = 0,
    ) -> Response | RawResponse | None:
        # D-M1b-8, as amended by D-M2-9: the id is minted here, above any replay, so one
        # caller-visible operation keeps one id across every pass. `request_id` and
        # `attempts_before` are inbound so AUT-003's re-login replay (M2b) can run a second
        # pass under the *same* id with its per-pass `attempt` reset -- an internal
        # signature change, not a public one.
        try:
            return await self._execute_uncancelled(
                method=method,
                path=path,
                body=body,
                options=options,
                default_idempotent=default_idempotent,
                read_like=read_like,
                absolute=absolute,
                request_id=request_id if request_id is not None else uuid.uuid4().hex,
                attempts_before=attempts_before,
            )
        except asyncio.CancelledError as cancelled:
            # OVR-006: a cancelled operation surfaces as a coded error, not a bare
            # `CancelledError`, so callers never need to catch a runtime primitive to
            # tell "the caller cancelled" apart from every other transport failure.
            raise make_error(ErrorCodes.TRANSPORT_CANCELLED, attempts=0) from cancelled

    async def _execute_uncancelled(
        self,
        *,
        method: str,
        path: str,
        body: Mapping[str, Any] | None,
        options: RequestOptions | None,
        default_idempotent: bool,
        read_like: bool,
        absolute: bool,
        request_id: str,
        attempts_before: int,
    ) -> Response | RawResponse | None:
        opts = options if options is not None else RequestOptions()
        if opts.wrap_ttl is not None:
            # TRN-017: response wrapping is not implemented by the server.
            raise make_error(ErrorCodes.INPUT_UNSUPPORTED_OPTION, attempts=0)

        config = self._config
        namespace = opts.namespace if opts.namespace is not None else self._namespace
        body_bytes = _encode_body(body)
        display_path = _display_path(namespace, path)
        # CFG-080 / TST-051: the observer sees the **redacted** display path. AUT-080's
        # `auth/token/renew/{token}` and `Auth.token.lookup`'s `auth/token/lookup/{token}`
        # put a live token in the path, and `RequestEvent.path` is the second consumer of
        # that string after the error (ERR-003 already covers the first). Redacted once
        # here so no call site can pass the unredacted form -- this was defect 1 of the
        # three the .NET pathfinder found by reading its own request path (D-M2-7).
        observed_path = redact(display_path) or display_path
        explicit_token = opts.token is not None
        # D-M1b-9's snapshot, in the place D-M1b-9 put it: once per pass, above the retry
        # loop, so in-flight requests keep the token they started with (CFG-070). **Nothing
        # relocates at M2a** -- the only change is that the token now comes from
        # `TokenSource.resolve()` instead of a field read (D-M2-11(b)).
        token = await self._resolve_snapshot(
            opts,
            path=path,
            method=method,
            display_path=observed_path,
            attempts_before=attempts_before,
        )
        idempotent = opts.idempotent if opts.idempotent is not None else default_idempotent
        retry_policy = config.retry_policy
        timeout = opts.timeout if opts.timeout is not None else config.timeout
        deadline = self._clock.now_utc() + opts.total_timeout if opts.total_timeout is not None else None
        prefix = opts.api_version if opts.api_version is not None else config.api_prefix

        # Two counters, because `attempt` was doing double duty (D-M2-9 ruling 2). Per-pass
        # `attempt` governs retry eligibility and the backoff exponent and resets on an
        # AUT-003 replay; accumulated `attempts_total` is what the error's `attempts` and
        # the observer event report. Feeding the accumulation into the eligibility check
        # instead would mean a replayed request whose first pass had already burnt
        # `max_attempts` is never retried at all, so CFG-051..055 would silently not apply
        # to the replay path.
        attempt = 0
        attempts_total = attempts_before
        last_error: BastionVaultError | None = None
        while True:
            attempt += 1
            attempts_total += 1
            url = _build_url(address=config.address, prefix=prefix, path=path, absolute=absolute)
            headers = _build_headers(
                client_headers=config.headers,
                option_headers=opts.headers,
                has_body=body_bytes is not None,
                user_agent=config.user_agent,
                path=path,
                token=token,
                explicit_token=explicit_token,
                namespace=namespace,
            )
            transport_request = TransportRequest(
                method=method,
                url=url,
                headers=headers,
                body=body_bytes,
                timeout=timeout,
                connect_timeout=config.connect_timeout,
                max_response_bytes=config.max_response_bytes,
            )
            start = self._clock.now_utc()
            status_code: int | None = None
            error: BastionVaultError | None = None
            result: Response | RawResponse | None = None
            try:
                transport_response = await self._transport.send(transport_request)
            except BastionVaultError as transport_error:
                error = transport_error
            else:
                status_code = transport_response.status_code
                if status_code == 429:
                    self._pause_rate_gate(transport_response.headers)
                if absolute:
                    outcome = _interpret_raw(
                        status_code=status_code,
                        body=transport_response.body,
                        headers=transport_response.headers,
                        method=method,
                        display_path=display_path,
                    )
                else:
                    outcome = _interpret_envelope(
                        status_code=status_code,
                        body=transport_response.body,
                        headers=transport_response.headers,
                        method=method,
                        display_path=display_path,
                        read_like=read_like,
                        logger=config.logger,
                    )
                result, error = outcome.result, outcome.error

            duration = self._clock.now_utc() - start
            if error is not None:
                error.attempts = attempts_total
            config.observer.on_request_completed(
                RequestEvent(
                    method=method,
                    path=observed_path,
                    namespace=namespace,
                    status_code=status_code,
                    duration=duration,
                    request_id=request_id,
                    attempt=attempts_total,
                    error_code=error.code if error is not None else None,
                )
            )
            if error is None:
                return result

            last_error = error
            eligible = (
                error.code in retry_policy.retry_on
                and error.code not in _NEVER_RETRY
                and (idempotent or not retry_policy.retry_idempotent_only)
                and attempt < retry_policy.max_attempts
            )
            if deadline is not None and self._clock.now_utc() >= deadline:
                eligible = False
            if not eligible:
                # ERR-034/ERR-040 enrichment happens once, here, where the error stops
                # being retryable and becomes what the caller sees (D-M1c-14 item 7).
                raise _present(
                    last_error,
                    attempt=attempts_total,
                    method=method,
                    display_path=display_path,
                    active_namespace=namespace,
                    config=config,
                )

            wait = self._compute_backoff(retry_policy, attempt)
            if error.retry_after is not None and retry_policy.respect_retry_after:
                wait = max(error.retry_after, wait)
                wait = min(wait, retry_policy.max_backoff * 6)  # CFG-054
            if deadline is not None:
                # The eligibility check above already guarantees `now() < deadline`
                # here, so `remaining` is always positive; only the clamp is live.
                wait = min(wait, deadline - self._clock.now_utc())
            await self._clock.delay(wait)

    async def _resolve_snapshot(
        self,
        opts: RequestOptions,
        *,
        path: str,
        method: str,
        display_path: str,
        attempts_before: int,
    ) -> SecretString | None:
        """D-M2-9's seam, with the guard `BV-AUTH-017` and `BV-TRANSPORT-005` need.

        The ordering of the three `except` clauses is **load-bearing** (D-M2-18 item 1).
        .NET expresses it with exception filters, which Python does not have:

        1. `asyncio.CancelledError` is read **first**. Inverted, a cancelled resolution
           would report `BV-AUTH-017` instead of `BV-TRANSPORT-005`. Note that
           `CancelledError` derives from `BaseException` in Python 3.8+, so the generic arm
           below would not catch it even if the order were wrong -- it would escape
           untyped instead, which ERR-020/TRN-054 forbid just as firmly. The clause is
           therefore here for the *intent*, not only for the order.
        2. A `BastionVaultError` is re-raised **unwrapped**, so M2b's `Login` source keeps
           its recogniser codes: AUT-003's replay keys on `BV-AUTHZ-001` specifically, and
           wrapping would break it outright (D-M2-18 item 3).
        3. Anything else is `BV-AUTH-017 TokenSourceFailed` (D-M2-16). The source is
           configured and its resolution failed, which `BV-AUTH-001 NoToken` does not
           describe -- that code's message is "No token is *configured*". `attempts` is the
           count so far (none, on a first pass) because no request was sent, and the
           source's own exception is preserved as the cause rather than flattened into a
           message.
        """

        if opts.token is not None:
            # CFG-060: a per-call token is itself "the current token" for this call, and
            # the client's source is not resolved at all.
            return _present_token(opts.token)

        if _is_login_path(path):
            # CFG-020's first MUST (D-M1c-24): a login carries no token header. Resolution
            # is skipped entirely rather than resolved-and-discarded, so a `Login` source
            # does not recurse into a login in order to send one.
            return None

        try:
            return _present_token(await self._resolve_token())
        except asyncio.CancelledError as cancelled:
            raise make_error(
                ErrorCodes.TRANSPORT_CANCELLED,
                attempts=attempts_before,
                method=method,
                path=display_path,
                address=self._config.address,
            ) from cancelled
        except BastionVaultError:
            raise
        except Exception as failure:
            raise make_error(
                ErrorCodes.AUTH_TOKEN_SOURCE_FAILED,
                attempts=attempts_before,
                method=method,
                path=display_path,
                address=self._config.address,
            ) from failure

    def _compute_backoff(self, retry_policy: RetryPolicy, attempt: int) -> timedelta:
        backoff = retry_policy.initial_backoff * (retry_policy.backoff_multiplier ** (attempt - 1))
        backoff = min(retry_policy.max_backoff, backoff)
        jitter_span = self._jitter.next_double() * 2 - 1  # [-1, 1)
        factor = 1 + jitter_span * retry_policy.jitter
        return max(timedelta(0), backoff * factor)

    def _pause_rate_gate(self, headers: Mapping[str, str]) -> None:
        """D-M1b-16/22: pause on any `429`, driven by status alone."""
        retry_after = _parse_retry_after(_case_insensitive(headers, "retry-after"))
        seconds = min(retry_after, 30.0) if retry_after is not None else 1.0
        self._state.pause_rate_gate(self._clock.now_utc() + timedelta(seconds=seconds))


def _present_token(token: SecretString | None) -> SecretString | None:
    """An empty resolution is "no token", not a token whose value happens to be empty.

    `clear_token` keeps AUT-001's "exactly one source" by installing a `Static` source
    holding nothing, so the resolution of a cleared client is an empty `SecretString`. That
    must not become an empty `X-BastionVault-Token` header, and it is the same normalisation
    AUT-004's `Auth.current_token` applies when it reports `None`.
    """

    return token if token is not None and token.reveal() else None


def _case_insensitive(headers: Mapping[str, str], name: str) -> str | None:
    target = name.casefold()
    for key, value in headers.items():
        if key.casefold() == target:
            return value
    return None


__all__ = ["Client"]
