"""The two D-M2-7 harness instruments, each proven by a seeded violation.

R-10 and DR-0001 both say the same thing: an instrument that has never failed has not been
tested, and this project has now shipped four gates that were trusted on their record.

These are the **standing** proofs -- they seed the violation inside the test, so the proof
re-runs on every build rather than living in a handback summary. They were preceded by
one-off seeded violations against the real library code, each reverted:

| Seed | Where | Result |
|------|-------|--------|
| `remaining_ttl = None` (AUT-014 not computed) | `auth.py` | red: `expected 'PT1H', got None` |
| driver ignores `clock.start` | `fixture_instruments.py` | red: `expected 'PT1H', got 'PT497029H'` |
| SDK logs the resolved token | `client.py` | red: TST-051 leak in a log line |
| observer event carries the unredacted path | `client.py` | red: TST-051 leak in `RequestEvent.path` |

The third and fourth are the two halves D-M2-7 insisted on separately, and the fourth
reproduces .NET's own defect 1 exactly: `RequestEvent.path` shipping a live token to every
CFG-080 observer.

**One substitution the corpus cannot catch, reported rather than papered over.** Replacing
AUT-014's `creation_time + creation_ttl - now_utc` with a bare `creation_ttl` leaves
`auth.token.lookup-self-remaining-ttl` **green**, because that fixture's
`clock.start` (`2026-09-13T12:00:00Z`) is exactly its `creation_time` (`1789300800`), which
makes the subtraction degenerate. The fixture proves the clock is *injected and read*; it
does not pin the arithmetic. Correcting it means editing a fixture, which is a
specification change under FIX-012 and therefore not M2a's to make -- it is escalated in
the handback instead.
"""

from __future__ import annotations

import json
from collections.abc import Mapping
from datetime import datetime, timedelta, timezone
from typing import Any

import pytest

from bastionvault_integration_sdk.testing import FakeTransport
from bastionvault_integration_sdk.transport import RequestEvent, TransportRequest

from .harness import fake_tokens
from .harness.fixture_driver import (
    ClientConfiguration,
    FixtureDriver,
    FixtureRunFailure,
    OperationRegistry,
)
from .harness.fixture_instruments import (
    FixtureInstruments,
    assert_no_leak,
    harvest_secrets,
    parse_iso8601_duration,
)


def _synthetic(document: str) -> Mapping[str, Any]:
    """A fixture built in-test.

    Deliberately *not* written to `specifications/fixtures/`: a synthetic document proving
    the harness is not a captured server behaviour, and adding one to the corpus would put
    a non-specification entry in front of the fixture-count assertion.
    """
    parsed: Mapping[str, Any] = json.loads(document)
    return parsed


async def _send(transport: FakeTransport, url: str) -> None:
    await transport.send(
        TransportRequest(
            method="GET",
            url=url,
            headers={},
            body=None,
            timeout=timedelta(seconds=1),
            connect_timeout=timedelta(seconds=1),
            max_response_bytes=1024,
        )
    )


# --------------------------------------------------------------------------------
# Instrument one: the fixture `clock`
# --------------------------------------------------------------------------------


def test_a_fixture_declaring_a_clock_fails_when_the_operation_ignores_it() -> None:
    """The exact failure mode that hid `auth.token.lookup-self-remaining-ttl` since M0.

    The fixture declares a clock, the driver injects nothing, the operation never asks the
    time, and the fixture passes while asserting nothing about the scripted instant.

    @req TST-011
    """
    fixture = _synthetic(
        """
        {
          "id": "synthetic.clock-ignored",
          "title": "Clock declared but never read",
          "requirements": ["TST-011"],
          "level": "core",
          "sections": ["05"],
          "clock": {"start": "2026-09-13T12:00:00Z"},
          "operation": {"name": "Synthetic.IgnoresClock"},
          "exchanges": [],
          "expect": {"result": {"value": "ok"}}
        }
        """
    )
    registry = OperationRegistry()
    registry.register("Synthetic.IgnoresClock", lambda c, t, o: {"value": "ok"})

    with pytest.raises(FixtureRunFailure, match="never read the injected clock"):
        FixtureDriver(registry).run(fixture)


def test_a_fixture_declaring_a_clock_passes_when_the_scripted_time_is_read() -> None:
    """`clock.start` is absolute; each `clock.advance` entry applies between exchanges.

    @req TST-011
    """
    fixture = _synthetic(
        """
        {
          "id": "synthetic.clock-honoured",
          "title": "Clock start and advances are honoured",
          "requirements": ["TST-011"],
          "level": "core",
          "sections": ["05"],
          "clock": {"start": "2026-09-13T12:00:00Z", "advance": ["PT30S", "PT1M"]},
          "operation": {"name": "Synthetic.ReadsClock"},
          "exchanges": [
            {"expectRequest": {"method": "GET", "url": "https://fixture.invalid/1"},
             "respond": {"status": 200}},
            {"expectRequest": {"method": "GET", "url": "https://fixture.invalid/2"},
             "respond": {"status": 200}}
          ],
          "expect": {
            "result": {
              "start": "2026-09-13T12:00:00+00:00",
              "afterFirst": "2026-09-13T12:00:30+00:00",
              "afterSecond": "2026-09-13T12:01:30+00:00"
            }
          }
        }
        """
    )

    async def reads_clock(
        configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
    ) -> Any:
        del operation
        clock = configuration.instruments.clock
        start = clock.now_utc().isoformat()
        await _send(transport, "https://fixture.invalid/1")
        after_first = clock.now_utc().isoformat()
        await _send(transport, "https://fixture.invalid/2")
        return {
            "start": start,
            "afterFirst": after_first,
            "afterSecond": clock.now_utc().isoformat(),
        }

    registry = OperationRegistry()
    registry.register("Synthetic.ReadsClock", reads_clock)

    assert FixtureDriver(registry).run(fixture).status == "passed"


def test_a_fixture_without_a_clock_block_keeps_the_frozen_pre_m2a_default() -> None:
    """No `clock` block means frozen time, so no backoff or pause waits in real time.

    @req RES-003
    """
    instruments = FixtureInstruments.from_fixture({"id": "synthetic.no-clock"})

    assert instruments.clock.is_scripted is False
    assert instruments.clock.now_utc() == datetime(1970, 1, 1, tzinfo=timezone.utc)


def test_delay_moves_the_clock_instead_of_sleeping() -> None:
    """@req RES-003"""
    instruments = FixtureInstruments.from_fixture(
        {"id": "synthetic.delay", "clock": {"start": "2026-09-13T12:00:00Z"}}
    )
    import asyncio

    before = instruments.clock.now_utc()
    asyncio.run(instruments.clock.delay(timedelta(seconds=90)))
    asyncio.run(instruments.clock.delay(timedelta(seconds=-5)))  # never moves backwards

    assert instruments.clock.now_utc() - before == timedelta(seconds=90)


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("PT1H", timedelta(hours=1)),
        ("PT30S", timedelta(seconds=30)),
        ("PT1M30S", timedelta(minutes=1, seconds=30)),
        ("P1DT2H", timedelta(days=1, hours=2)),
    ],
)
def test_iso8601_durations_the_corpus_uses_are_parsed(text: str, expected: timedelta) -> None:
    assert parse_iso8601_duration(text) == expected


def test_an_unsupported_duration_is_refused_rather_than_guessed_at() -> None:
    with pytest.raises(ValueError, match="unsupported ISO-8601 duration"):
        parse_iso8601_duration("1 hour")


# --------------------------------------------------------------------------------
# Instrument two: the TST-051 capture (logger plus observer)
# --------------------------------------------------------------------------------


def test_tst051_fails_when_a_fixture_token_reaches_a_log_line() -> None:
    """The seeded violation D-M2-7 names for the logger half.

    @req TST-051
    """
    fixture = _synthetic(
        """
        {
          "id": "synthetic.tst051-log-leak",
          "title": "Seeded TST-051 violation: the token is logged",
          "requirements": ["TST-051"],
          "level": "core",
          "sections": ["05"],
          "client": {"address": "https://vault.example.com:8200",
                     "token": "__FAKE_TOKEN__"},
          "operation": {"name": "Synthetic.LogsTheToken"},
          "exchanges": [],
          "expect": {"result": {"value": "ok"}}
        }
        """.replace("__FAKE_TOKEN__", fake_tokens.CLIENT)
    )

    def logs_the_token(
        configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
    ) -> Any:
        del transport, operation
        configuration.instruments.logger.warning(f"resolved token {configuration.token}")
        return {"value": "ok"}

    registry = OperationRegistry()
    registry.register("Synthetic.LogsTheToken", logs_the_token)

    with pytest.raises(FixtureRunFailure, match="TST-051"):
        FixtureDriver(registry).run(fixture)


def test_tst051_fails_when_a_fixture_token_reaches_an_observer_event() -> None:
    """The observer half, which D-M2-7 calls non-negotiable.

    `RequestEvent.path` is where `auth/token/renew/{token}` and `auth/token/lookup/{token}`
    leak a live token, and it was a real defect in .NET that this instrument caught in the
    same milestone that built it. The log half alone would not have seen it.

    @req TST-051 @req CFG-080 @req ERR-003
    """
    fixture = _synthetic(
        """
        {
          "id": "synthetic.tst051-observer-leak",
          "title": "Seeded TST-051 violation: the token is in RequestEvent.path",
          "requirements": ["TST-051"],
          "level": "core",
          "sections": ["05"],
          "client": {"address": "https://vault.example.com:8200",
                     "token": "__FAKE_TOKEN__"},
          "operation": {"name": "Synthetic.LeaksViaObserver"},
          "exchanges": [],
          "expect": {"result": {"value": "ok"}}
        }
        """.replace("__FAKE_TOKEN__", fake_tokens.CLIENT)
    )

    def leaks_via_observer(
        configuration: ClientConfiguration, transport: FakeTransport, operation: Mapping[str, Any]
    ) -> Any:
        del transport, operation
        configuration.instruments.observer.on_request_completed(
            RequestEvent(
                method="POST",
                path=f"auth/token/renew/{configuration.token}",
                namespace="",
                status_code=200,
                duration=timedelta(0),
                request_id="fixed",
                attempt=1,
            )
        )
        return {"value": "ok"}

    registry = OperationRegistry()
    registry.register("Synthetic.LeaksViaObserver", leaks_via_observer)

    with pytest.raises(FixtureRunFailure, match="TST-051"):
        FixtureDriver(registry).run(fixture)


def test_secrets_are_harvested_by_both_tst050_conventions_and_by_property_name() -> None:
    """The assertion is exactly as strong as TST-050 compliance (D-M2-7).

    A substring search is a valid test only because `15-testing-requirements.md:350-353`
    forces distinctive fixture secrets. The property-name list is the backstop for a
    fixture that spells a secret some other way.

    @req TST-050 @req TST-051
    """
    harvested = harvest_secrets(
        {
            "client": {"token": fake_tokens.CLIENT},
            "operation": {"args": {"password": "password-fixture", "username": "alice"}},
            "exchanges": [{"respond": {"body": {"auth": {"client_token": "opaque-value"}}}}],
            "unrelated": "not-a-secret",
        }
    )

    assert fake_tokens.CLIENT in harvested  # `s.FAKE` convention
    assert "password-fixture" in harvested  # `password-fixture` convention
    assert "opaque-value" in harvested  # `client_token` property name
    assert "alice" not in harvested
    assert "not-a-secret" not in harvested


def test_the_recorded_requests_are_deliberately_not_searched() -> None:
    """The wire legitimately carries the token; TRN-015 is what says where.

    @req TST-051 @req TRN-015
    """
    fixture = {"id": "synthetic.wire", "client": {"token": fake_tokens.CLIENT}}

    # The same literal in a surfaced string is a failure; in a request it is the protocol.
    assert_no_leak(fixture, ["a hint with no secret in it"])
    with pytest.raises(AssertionError, match="TST-051"):
        assert_no_leak(fixture, [f"leaked {fake_tokens.CLIENT} here"])


def test_a_leak_report_masks_the_secret_it_found() -> None:
    """A failing gate must not become the thing that prints the credential.

    @req TST-051
    """
    with pytest.raises(AssertionError) as raised:
        assert_no_leak(
            {"id": "synthetic.mask", "client": {"token": fake_tokens.CLIENT}},
            [f"surfaced {fake_tokens.CLIENT}"],
        )

    assert fake_tokens.CLIENT not in str(raised.value)
    assert "s.FAKE***" in str(raised.value)
