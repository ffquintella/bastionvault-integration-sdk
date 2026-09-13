"""OVR-001 transport seam, plus the per-operation option types (CFG-050/060/061).

No HTTP request, retry execution, or observability hook lands in M1a. This module
defines the shapes only: `Transport` is the injection point tests use to avoid opening
a socket; `RetryPolicy` and `RequestOptions` are materialised data, not yet consumed.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass, field
from datetime import timedelta
from typing import Any, Protocol, runtime_checkable

from .secrets import SecretString


@runtime_checkable
class Transport(Protocol):
    """The HTTP transport injection point (OVR-001). Replaceable so tests never open a socket."""

    def request(
        self,
        method: str,
        url: str,
        headers: Mapping[str, str],
        body: Any,
    ) -> Any:
        """Send one request and return an implementation-defined response object."""
        ...


@dataclass(frozen=True)
class RetryPolicy:
    """Retry policy defaults (CFG-050). Retry *execution* is M1b."""

    max_attempts: int = 3
    initial_backoff: timedelta = timedelta(milliseconds=250)
    max_backoff: timedelta = timedelta(seconds=5)
    backoff_multiplier: float = 2.0
    jitter: float = 0.2
    respect_retry_after: bool = True
    retry_idempotent_only: bool = True


@dataclass(frozen=True)
class RequestOptions:
    """Per-operation options (CFG-060: every field optional; CFG-061: never mutates `Client`)."""

    namespace: str | None = None
    headers: Mapping[str, str] = field(default_factory=dict)
    timeout: timedelta | None = None
    idempotent: bool = False
    wrap_ttl: str | None = None
    token: SecretString | None = None

    def __post_init__(self) -> None:
        # Defensive copy so a caller's mutable dict cannot alias this frozen instance
        # (the same hazard the headers-aliasing defect on `ClientConfig` warns about).
        object.__setattr__(self, "headers", dict(self.headers))
