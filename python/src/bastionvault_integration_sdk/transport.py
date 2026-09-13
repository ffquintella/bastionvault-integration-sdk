"""The M1b transport seam: one canonical async shape (D-M1b-1), plus the seams that
retry and backoff depend on (`Clock`, `JitterSource`), the observability hook
(`RequestObserver`/`RequestEvent`, D-M1b-8), the rate-gate pause snapshot
(`RateGateState`, D-M1b-16), and the per-operation option types (CFG-050/060/061).

`Transport.send` is the only place a socket opens. A transport-level failure MUST be
raised as `BastionVaultError` carrying `BV-TRANSPORT-001/002/003/005`, never a bare
runtime exception (D-M1b-1) -- that is what lets the retry loop read a code instead of
matching on exception types.
"""

from __future__ import annotations

import asyncio
import random
from collections.abc import Mapping
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any, Protocol, runtime_checkable

from .errors import ErrorCodes, make_config_error
from .secrets import SecretString

_VALID_API_VERSIONS = ("v1", "v2")


@dataclass(frozen=True)
class TransportRequest:
    """One HTTP exchange, fully constructed above the transport (D-M1b-1).

    `method` is the literal HTTP method string, including `LIST` (TRN-010). `url` is the
    fully-constructed, already-encoded absolute URL (TRN-020/021). `body` is bytes, never
    a parsed object -- serialization happens above the transport. `timeout` is the
    *per-attempt* budget (RES-004). `max_response_bytes` (TRN-033/D-M1b-20) MUST be
    enforced by the transport while reading, not audited afterwards.
    """

    method: str
    url: str
    headers: Mapping[str, str] = field(default_factory=dict)
    body: bytes | None = None
    timeout: timedelta = timedelta(seconds=30)
    connect_timeout: timedelta = timedelta(seconds=10)
    max_response_bytes: int | None = None


@dataclass(frozen=True)
class TransportResponse:
    """The transport's answer: status, headers, and the raw (unparsed) body bytes."""

    status_code: int
    headers: Mapping[str, str] = field(default_factory=dict)
    body: bytes = b""


@runtime_checkable
class Transport(Protocol):
    """The HTTP transport injection point (OVR-001), redesigned async at M1b (D-M1b-1)."""

    async def send(self, request: TransportRequest) -> TransportResponse:
        """Send one request and return its response, or raise `BastionVaultError`."""
        ...

    @property
    def supports_custom_verbs(self) -> bool:
        """Whether this transport can send a literal, non-standard HTTP method (`LIST`).

        Default `true` in every production transport (D-M1b-3). `Client` construction
        asserts this and raises `BV-CONFIG-009` when false (D-M1b-14, TRN-010).
        """
        ...


@runtime_checkable
class Clock(Protocol):
    """The injected time seam (RES-003): no backoff or pause waits in real time in a test."""

    def now(self) -> datetime:
        """The current instant (UTC)."""
        ...

    async def delay(self, duration: timedelta) -> None:
        """Wait for `duration`. Every backoff and pause wait goes through this."""
        ...


@runtime_checkable
class JitterSource(Protocol):
    """The injected random seam (RES-003)."""

    def next_double(self) -> float:
        """A pseudo-random value in `[0.0, 1.0)`."""
        ...


class SystemClock:
    """The default `Clock`: the wall clock and `asyncio.sleep`."""

    def now(self) -> datetime:
        return datetime.now(timezone.utc)

    async def delay(self, duration: timedelta) -> None:
        seconds = duration.total_seconds()
        if seconds > 0:
            await asyncio.sleep(seconds)


class SystemJitterSource:
    """The default `JitterSource`: `random.random()`."""

    def next_double(self) -> float:
        return random.random()  # noqa: S311 - jitter, not a security primitive


@dataclass(frozen=True)
class RequestEvent:
    """One reported attempt (RES-002, CFG-080). No body, no token, no headers."""

    method: str
    path: str
    namespace: str
    status_code: int | None
    duration: timedelta
    request_id: str
    attempt: int
    error_code: str | None = None


@runtime_checkable
class RequestObserver(Protocol):
    """The observability hook (D-M1b-8). Fires once per attempt, never per body/token."""

    def on_request_completed(self, event: RequestEvent) -> None:
        """Report one completed attempt."""
        ...


class _NoOpRequestObserver:
    """The default `RequestObserver`: observes nothing."""

    def on_request_completed(self, event: RequestEvent) -> None:
        return None


@dataclass(frozen=True)
class RateGateState:
    """The part of the client-side rate gate M1b ships (D-M1b-16): the pause only."""

    paused: bool
    paused_until: datetime | None = None


@dataclass(frozen=True)
class RetryPolicy:
    """Retry policy (CFG-050). `RetryOn` is the D-M1b-7 addition; a per-call `Timeout`
    bounds one attempt, `RequestOptions.TotalTimeout` bounds attempts plus backoff.
    """

    max_attempts: int = 3
    initial_backoff: timedelta = timedelta(milliseconds=250)
    max_backoff: timedelta = timedelta(seconds=5)
    backoff_multiplier: float = 2.0
    jitter: float = 0.2
    retry_on: tuple[str, ...] = (
        "BV-TRANSPORT-001",
        "BV-TRANSPORT-002",
        "BV-SERVER-002",
        "BV-SERVER-003",
    )
    respect_retry_after: bool = True
    retry_idempotent_only: bool = True

    def __post_init__(self) -> None:
        object.__setattr__(self, "retry_on", tuple(self.retry_on))
        for code in self.retry_on:
            if not isinstance(code, str):
                raise make_config_error(
                    ErrorCodes.CONFIG_INVALID_SETTING_VALUE,
                    details={"setting": "RetryPolicy.RetryOn"},
                )


@dataclass(frozen=True)
class RequestOptions:
    """Per-operation options (CFG-060: every field optional; CFG-061: never mutates
    `Client`). `Idempotent` is optional/tri-state (D-M1b-6): unset defers to the
    operation table, set wins in both directions. `ApiVersion` and `TotalTimeout` are
    the D-M1b-13 clarifications.
    """

    namespace: str | None = None
    headers: Mapping[str, str] = field(default_factory=dict)
    timeout: timedelta | None = None
    idempotent: bool | None = None
    wrap_ttl: str | None = None
    token: SecretString | None = None
    api_version: str | None = None
    total_timeout: timedelta | None = None

    def __post_init__(self) -> None:
        # Defensive copy so a caller's mutable dict cannot alias this frozen instance.
        object.__setattr__(self, "headers", dict(self.headers))
        for key, value in self.headers.items():
            if not isinstance(key, str) or not isinstance(value, str):
                raise make_config_error(
                    ErrorCodes.CONFIG_INVALID_SETTING_VALUE,
                    details={"setting": "RequestOptions.Headers"},
                )
        # D-M1a-19, applied here per D-M1b's note: a wrong-typed option is a coded
        # error, never a bare TypeError/AttributeError surfacing deep inside a request.
        _check_type(self.namespace, str, "RequestOptions.Namespace")
        _check_type(self.timeout, timedelta, "RequestOptions.Timeout")
        _check_type(self.idempotent, bool, "RequestOptions.Idempotent")
        _check_type(self.wrap_ttl, str, "RequestOptions.WrapTtl")
        _check_type(self.token, SecretString, "RequestOptions.Token")
        _check_type(self.total_timeout, timedelta, "RequestOptions.TotalTimeout")
        if self.api_version is not None and self.api_version not in _VALID_API_VERSIONS:
            raise make_config_error(
                ErrorCodes.CONFIG_INVALID_SETTING_VALUE,
                details={"setting": "RequestOptions.ApiVersion"},
            )


def _check_type(value: Any, expected_type: type, setting: str) -> None:
    if value is not None and not isinstance(value, expected_type):
        raise make_config_error(ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": setting})
