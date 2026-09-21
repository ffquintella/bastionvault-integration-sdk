"""Building the real `Client` a fixture describes, wired to D-M2-7's instruments.

Shared by every fixture suite so there is exactly one place that decides how a fixture's
`client` block becomes a `ClientOptions`, and exactly one place that injects the clock, the
capturing logger and the capturing `RequestObserver`. A suite that built its own client
could quietly inject its own clock, which would make the driver's "was the declared clock
honoured" check unfalsifiable.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import replace
from datetime import timedelta
from typing import Any

from bastionvault_integration_sdk import (
    Client,
    ClientOptions,
    MapEnvironmentSource,
    RateGate,
    RetryPolicy,
)
from bastionvault_integration_sdk.errors import BastionVaultError
from bastionvault_integration_sdk.testing import FakeTransport

from .fixture_driver import ClientConfiguration, OperationError
from .fixture_instruments import parse_iso8601_duration


class FixedJitterSource:
    """A `JitterSource` pinned mid-range, so a backoff is arithmetic and not a coin flip."""

    def next_double(self) -> float:
        return 0.5


def client_options_from_settings(configuration: ClientConfiguration) -> ClientOptions:
    """Translate a fixture's `client.settings` block into `ClientOptions`."""
    settings = configuration.settings if isinstance(configuration.settings, Mapping) else {}
    retry_settings = settings.get("RetryPolicy")
    retry_settings = retry_settings if isinstance(retry_settings, Mapping) else {}
    rate_settings = settings.get("RateGate")
    rate_settings = rate_settings if isinstance(rate_settings, Mapping) else {}
    headers_settings = settings.get("Headers")
    headers_settings = headers_settings if isinstance(headers_settings, Mapping) else {}

    retry_kwargs: dict[str, Any] = {}
    if "MaxAttempts" in retry_settings:
        retry_kwargs["max_attempts"] = int(retry_settings["MaxAttempts"])
    if "InitialBackoff" in retry_settings:
        retry_kwargs["initial_backoff"] = parse_iso8601_duration(
            str(retry_settings["InitialBackoff"])
        )
    retry_policy = RetryPolicy(**retry_kwargs) if retry_kwargs else None

    rate_kwargs: dict[str, Any] = {}
    if "RatePerSecond" in rate_settings:
        rate_kwargs["rate_per_second"] = int(rate_settings["RatePerSecond"])
    if "Burst" in rate_settings:
        rate_kwargs["burst"] = int(rate_settings["Burst"])
    rate_gate = RateGate(**rate_kwargs) if rate_kwargs else None

    return ClientOptions(
        address=configuration.address,
        token=configuration.token,
        namespace=configuration.namespace,
        api_prefix=configuration.api_prefix,
        retry_policy=retry_policy,
        rate_gate=rate_gate,
        headers=dict(headers_settings) or None,
    )


def make_client(configuration: ClientConfiguration, transport: FakeTransport) -> Client:
    """Build the real `Client`, wired to the fixture's instruments (D-M2-7)."""
    options = client_options_from_settings(configuration)
    instruments = configuration.instruments
    return Client(
        replace(options, logger=instruments.logger, observer=instruments.observer),
        environment=MapEnvironmentSource(configuration.environment),
        transport=transport,
        clock=instruments.clock,
        jitter_source=FixedJitterSource(),
    )


def error_to_operation_error(
    error: BastionVaultError, *, client_state: Mapping[str, Any] | None = None
) -> OperationError:
    """Translate a real `BastionVaultError` into the driver's language-neutral outcome.

    The error itself travels along as `surfaced` so TST-051's leak scan sees it exactly as
    a caller would -- message, hint, server message, redacted path, details, and ERR-002's
    one-line rendered form.
    """
    retry_after = int(error.retry_after.total_seconds()) if error.retry_after is not None else None
    return OperationError(
        surfaced=error,
        code=error.code,
        status_code=error.status_code,
        retryable=error.retryable,
        attempts=error.attempts,
        retry_after=retry_after,
        details=dict(error.details),
        hint=error.hint,
        server_message=error.server_message,
        client_state=dict(client_state) if client_state is not None else {},
    )


def iso8601_duration(value: timedelta) -> str:
    """Render a `timedelta` as the ISO-8601 duration the fixture corpus compares against.

    `auth.token.lookup-self-remaining-ttl` asserts `"PT1H"`, so the renderer must be
    canonical: whole hours, minutes and seconds, zero components omitted.
    """
    total = value.total_seconds()
    sign = "-" if total < 0 else ""
    total = abs(total)
    hours, remainder = divmod(int(total), 3600)
    minutes, seconds = divmod(remainder, 60)
    fraction = total - int(total)
    parts = ""
    if hours:
        parts += f"{hours}H"
    if minutes:
        parts += f"{minutes}M"
    if seconds or fraction:
        rendered = f"{seconds + fraction:g}" if fraction else str(seconds)
        parts += f"{rendered}S"
    return f"{sign}PT{parts or '0S'}"


__all__ = [
    "FixedJitterSource",
    "client_options_from_settings",
    "error_to_operation_error",
    "iso8601_duration",
    "make_client",
]
