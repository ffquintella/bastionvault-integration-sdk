"""D-M2-7's two harness instruments: the fixture `clock`, and the TST-051 capture.

Both are net-new capabilities at M2a. Neither existed in any language before it, which is
why `auth.token.lookup-self-remaining-ttl` had been silently unasserted since M0 -- the
schema has defined `clock` since then and no driver read it -- and why **TST-051 had never
been asserted anywhere**.

Each instrument is proven by a seeded violation before it is trusted (R-10, DR-0001): an
instrument that has never failed has not been tested. The two proofs are standing tests in
`tests/test_fixture_instruments.py`.
"""

from __future__ import annotations

import re
from collections.abc import Iterable, Mapping, Sequence
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any, Final

from bastionvault_integration_sdk.errors import BastionVaultError
from bastionvault_integration_sdk.testing import FakeTransport
from bastionvault_integration_sdk.transport import RequestEvent

_ISO8601_DURATION_RE: Final = re.compile(
    r"^P(?:(?P<days>\d+(?:\.\d+)?)D)?"
    r"(?:T(?:(?P<hours>\d+(?:\.\d+)?)H)?(?:(?P<minutes>\d+(?:\.\d+)?)M)?"
    r"(?:(?P<seconds>\d+(?:\.\d+)?)S)?)?$"
)

#: The JSON property names whose value is credential material regardless of its spelling.
#: TST-050's `s.FAKE...` / `password-fixture` conventions catch most of it; these catch a
#: fixture that spells a secret some other way.
_SECRET_BEARING_PROPERTIES: Final[frozenset[str]] = frozenset(
    {
        "token",
        "password",
        "secret_id",
        "machine_token",
        "totp_code",
        "client_token",
        "child_token",
        "user_token",
        "x-bastionvault-token",
    }
)

#: TST-050's two fixture-secret conventions (`15-testing-requirements.md:350-353`). The
#: whole TST-051 assertion is exactly as strong as compliance with them: a substring search
#: is a valid test only because fixture secrets are distinctive.
_SECRET_CONVENTIONS: Final = ("s.FAKE", "password-fixture")


def parse_iso8601_duration(text: str) -> timedelta:
    """Parse the `PT1H`/`P1DT30S` durations the fixture corpus uses."""
    match = _ISO8601_DURATION_RE.match(text)
    if not match:
        raise ValueError(f"unsupported ISO-8601 duration in fixture: {text!r}")
    parts = {name: float(value) if value else 0.0 for name, value in match.groupdict().items()}
    return timedelta(
        days=parts["days"],
        hours=parts["hours"],
        minutes=parts["minutes"],
        seconds=parts["seconds"],
    )


class FixtureClock:
    """Instrument one: the controllable `Clock` a fixture's `clock` block describes.

    `clock.start` is an absolute timestamp; each `clock.advance` entry is a duration applied
    **between** exchanges, so the first entry takes effect after the first exchange and
    before the second. The position in the `advance` list is derived from how many requests
    the scripted transport has recorded, which is what "between exchanges" means and needs
    no hook inside the transport.

    A fixture that declares `clock` and runs against a driver that ignores it must **fail**,
    not pass (D-M2-7). A driver cannot know the operation used the clock *correctly*, but it
    can know whether it ever asked: `reads` counts resolutions and `FixtureDriver` refuses a
    scripted-clock fixture that never read it.

    With no `clock` block the behaviour is the pre-M2a one exactly -- frozen, so no backoff
    or pause ever waits in real time (RES-003, D-M1b-7).
    """

    def __init__(
        self,
        start: datetime,
        advances: Sequence[timedelta],
        *,
        is_scripted: bool,
    ) -> None:
        self._start = start
        self._advances = tuple(advances)
        self.is_scripted = is_scripted
        self.reads = 0
        self._transport: FakeTransport | None = None
        self._slept = timedelta(0)

    @staticmethod
    def from_fixture(fixture: Mapping[str, Any]) -> "FixtureClock":
        """Build the clock a fixture declares, or the frozen default when it declares none."""
        clock = fixture.get("clock")
        if not isinstance(clock, Mapping):
            return FixtureClock(
                datetime(1970, 1, 1, tzinfo=timezone.utc), (), is_scripted=False
            )
        raw_start = clock.get("start")
        start = (
            datetime.fromisoformat(str(raw_start).replace("Z", "+00:00"))
            if isinstance(raw_start, str)
            else datetime(1970, 1, 1, tzinfo=timezone.utc)
        )
        raw_advance = clock.get("advance")
        advances = (
            tuple(parse_iso8601_duration(str(item)) for item in raw_advance)
            if isinstance(raw_advance, Sequence) and not isinstance(raw_advance, (str, bytes))
            else ()
        )
        return FixtureClock(start, advances, is_scripted=True)

    def bind(self, transport: FakeTransport) -> None:
        """Bind the transport whose recorded-request count drives `clock.advance`."""
        self._transport = transport

    def now_utc(self) -> datetime:
        """D-M2-2's member: wall-clock time, as a timezone-aware UTC `datetime`."""
        self.reads += 1
        completed = len(self._transport.requests) if self._transport is not None else 0
        applied = sum(self._advances[:completed], timedelta(0))
        return self._start + applied + self._slept

    async def delay(self, duration: timedelta) -> None:
        """Never really sleeps (D-M1b-7); the clock simply moves forward."""
        if duration > timedelta(0):
            self._slept += duration


class CapturingClientLogger:
    """Instrument two, half one (TST-051): every line the SDK writes through CNF-030's seam.

    `ClientLogger` exposes only `warning`, so the TST-051 proof is seeded at that level.
    D-M2-14 rules that adding a public logger member with no requirement behind it is not
    M2a's to do: CNF-031's "first four characters plus `...` when debug logging is
    explicitly enabled" carve-out is what implies a debug level, and `CNF-031`/`CNF-032` are
    **M2b**'s IDs.
    """

    def __init__(self) -> None:
        self.lines: list[str] = []

    def warning(self, message: str) -> None:
        self.lines.append(message)


class CapturingRequestObserver:
    """Instrument two, half two (TST-051, CFG-080): every observer event.

    Named explicitly by D-M2-7, and **non-negotiable**, because this -- not the log -- is
    where the leak this milestone was most likely to ship actually surfaces:
    `RequestEvent.path` carries the request path, and AUT-080's
    `auth/token/renew/{current_token}` and `Auth.token.lookup`'s
    `auth/token/lookup/{token}` put a live token in it. ERR-003 already required the path
    redacted in *errors*; the observer is the second consumer of the same string, and in
    .NET it was a real defect this instrument caught in the milestone that built it.
    """

    def __init__(self) -> None:
        self.events: list[RequestEvent] = []

    def on_request_completed(self, event: RequestEvent) -> None:
        self.events.append(event)


@dataclass
class FixtureInstruments:
    """The instruments one fixture run is given, and the strings it surfaced."""

    clock: FixtureClock
    logger: CapturingClientLogger = field(default_factory=CapturingClientLogger)
    observer: CapturingRequestObserver = field(default_factory=CapturingRequestObserver)

    @staticmethod
    def from_fixture(fixture: Mapping[str, Any]) -> "FixtureInstruments":
        return FixtureInstruments(clock=FixtureClock.from_fixture(fixture))

    def surfaced(self, error: BastionVaultError | None = None) -> list[str]:
        """Everything one run surfaced: log lines, observer events, and the rendered error.

        The recorded *requests* are deliberately not searched: the wire legitimately carries
        the token, and TRN-015 is what says where.
        """
        haystacks: list[str] = list(self.lines_and_events())
        if error is not None:
            haystacks.extend(
                [
                    error.code,
                    error.message,
                    error.hint,
                    error.server_message or "",
                    error.path or "",
                    str(error),  # ERR-002's one-line rendered form.
                ]
            )
            haystacks.extend(error.server_errors)
            for key, value in error.details.items():
                haystacks.append(str(key))
                haystacks.append(_render(value))
        return haystacks

    def lines_and_events(self) -> list[str]:
        # `repr` of the frozen dataclass renders every member, so a field added to
        # `RequestEvent` later is searched without this list being updated.
        return [*self.logger.lines, *(repr(event) for event in self.observer.events)]


def harvest_secrets(fixture: Mapping[str, Any]) -> tuple[str, ...]:
    """Every secret literal a fixture carries, de-duplicated, in declaration order."""
    found: list[str] = []
    _walk(fixture, None, found)
    seen: dict[str, None] = {}
    for item in found:
        seen.setdefault(item, None)
    return tuple(seen)


def assert_no_leak(fixture: Mapping[str, Any], haystacks: Iterable[str]) -> None:
    """TST-051: fail when any harvested literal appears in anything the SDK surfaced."""
    secrets = harvest_secrets(fixture)
    failures: list[str] = []
    materialised = list(haystacks)
    for secret in secrets:
        for haystack in materialised:
            if secret in haystack:
                failures.append(
                    f"secret {_mask(secret)!r} appears in a surfaced string: "
                    f"{haystack.replace(secret, _mask(secret))}"
                )
    if failures:
        raise AssertionError(
            f"TST-051: fixture {fixture.get('id', '<unknown>')!r} leaked secret material. "
            + "; ".join(failures)
        )


def _walk(node: Any, property_name: str | None, found: list[str]) -> None:
    if isinstance(node, Mapping):
        for key, value in node.items():
            _walk(value, str(key), found)
        return
    if isinstance(node, Sequence) and not isinstance(node, (str, bytes)):
        for item in node:
            _walk(item, property_name, found)
        return
    if isinstance(node, str) and _is_secret(node, property_name):
        found.append(node)


def _is_secret(value: str, property_name: str | None) -> bool:
    if not value:
        return False
    if any(convention in value for convention in _SECRET_CONVENTIONS):
        return True
    return property_name is not None and property_name.casefold() in _SECRET_BEARING_PROPERTIES


def _render(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, str):
        return value
    if isinstance(value, Sequence) and not isinstance(value, (str, bytes)):
        return ",".join(str(item) for item in value)
    return str(value)


def _mask(secret: str) -> str:
    return "***" if len(secret) <= 6 else secret[:6] + "***"


__all__ = [
    "CapturingClientLogger",
    "CapturingRequestObserver",
    "FixtureClock",
    "FixtureInstruments",
    "assert_no_leak",
    "harvest_secrets",
    "parse_iso8601_duration",
]
