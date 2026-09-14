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
    _parse_retry_after,
    _present,
)
from .secrets import SecretString
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
    """The mutable state a `Client` and every one of its `WithNamespace` views share."""

    def __init__(self, token: SecretString | None) -> None:
        self._lock = threading.Lock()
        self._token = token
        self._rate_gate_paused_until: datetime | None = None

    def get_token(self) -> SecretString | None:
        with self._lock:
            return self._token

    def set_token(self, token: SecretString) -> None:
        with self._lock:
            self._token = token

    def clear_token(self) -> None:
        with self._lock:
            self._token = None

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
        self._state = _ClientState(self._config.token)

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
    def rate_gate_state(self) -> RateGateState:
        """D-M1b-16: the pause half of the client-side rate gate (EFF-006)."""
        return self._state.rate_gate_state(self._clock.now())

    def set_token(self, token: SecretString) -> None:
        """CFG-070: thread-safe; in-flight requests keep the token they started with."""
        self._state.set_token(token)

    def clear_token(self) -> None:
        """CFG-070: thread-safe."""
        self._state.clear_token()

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
    ) -> Response | RawResponse | None:
        try:
            return await self._execute_uncancelled(
                method=method,
                path=path,
                body=body,
                options=options,
                default_idempotent=default_idempotent,
                read_like=read_like,
                absolute=absolute,
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
        absolute: bool = False,
    ) -> Response | RawResponse | None:
        opts = options if options is not None else RequestOptions()
        if opts.wrap_ttl is not None:
            # TRN-017: response wrapping is not implemented by the server.
            raise make_error(ErrorCodes.INPUT_UNSUPPORTED_OPTION, attempts=0)

        config = self._config
        namespace = opts.namespace if opts.namespace is not None else self._namespace
        token = opts.token if opts.token is not None else self._state.get_token()  # D-M1b-9 snapshot
        explicit_token = opts.token is not None
        body_bytes = _encode_body(body)
        display_path = _display_path(namespace, path)
        idempotent = opts.idempotent if opts.idempotent is not None else default_idempotent
        retry_policy = config.retry_policy
        timeout = opts.timeout if opts.timeout is not None else config.timeout
        deadline = self._clock.now() + opts.total_timeout if opts.total_timeout is not None else None
        request_id = uuid.uuid4().hex
        prefix = opts.api_version if opts.api_version is not None else config.api_prefix

        attempt = 0
        last_error: BastionVaultError | None = None
        while True:
            attempt += 1
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
            start = self._clock.now()
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

            duration = self._clock.now() - start
            if error is not None:
                error.attempts = attempt
            config.observer.on_request_completed(
                RequestEvent(
                    method=method,
                    path=display_path,
                    namespace=namespace,
                    status_code=status_code,
                    duration=duration,
                    request_id=request_id,
                    attempt=attempt,
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
            if deadline is not None and self._clock.now() >= deadline:
                eligible = False
            if not eligible:
                # ERR-034/ERR-040 enrichment happens once, here, where the error stops
                # being retryable and becomes what the caller sees (D-M1c-14 item 7).
                raise _present(
                    last_error,
                    attempt=attempt,
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
                wait = min(wait, deadline - self._clock.now())
            await self._clock.delay(wait)

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
        self._state.pause_rate_gate(self._clock.now() + timedelta(seconds=seconds))


def _case_insensitive(headers: Mapping[str, str], name: str) -> str | None:
    target = name.casefold()
    for key, value in headers.items():
        if key.casefold() == target:
            return value
    return None


__all__ = ["Client"]
