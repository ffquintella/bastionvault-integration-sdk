"""Unit tests for the M1b transport-seam types: `RequestOptions`, `RetryPolicy`,
`Clock`/`JitterSource` defaults, and `FakeTransport` (TRN-100)."""

from __future__ import annotations

import asyncio
from datetime import timedelta

import pytest

from bastionvault_integration_sdk.errors import BastionVaultError, ErrorCodes, make_error
from bastionvault_integration_sdk.secrets import SecretString
from bastionvault_integration_sdk.testing import FakeTransport
from bastionvault_integration_sdk.transport import (
    RequestOptions,
    RetryPolicy,
    SystemClock,
    SystemJitterSource,
    TransportRequest,
    TransportResponse,
)


@pytest.mark.parametrize(
    ("kwargs", "setting"),
    [
        ({"namespace": 5}, "RequestOptions.Namespace"),
        ({"timeout": 5}, "RequestOptions.Timeout"),
        ({"idempotent": "yes"}, "RequestOptions.Idempotent"),
        ({"wrap_ttl": 5}, "RequestOptions.WrapTtl"),
        ({"token": "not-a-secret-string"}, "RequestOptions.Token"),
        ({"total_timeout": 5}, "RequestOptions.TotalTimeout"),
        ({"api_version": "v3"}, "RequestOptions.ApiVersion"),
        ({"headers": {"x": 5}}, "RequestOptions.Headers"),
    ],
)
def test_d_m1a_19_wrong_typed_request_option_raises_coded_error(
    kwargs: dict[str, object], setting: str
) -> None:
    """@req D-M1a-19 (applied to M1b options)"""
    with pytest.raises(BastionVaultError) as excinfo:
        RequestOptions(**kwargs)  # type: ignore[arg-type]
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert excinfo.value.details["setting"] == setting


def test_request_options_valid_values_construct_cleanly() -> None:
    """@req CFG-060 @req D-M1b-13"""
    options = RequestOptions(
        namespace="team",
        timeout=timedelta(seconds=5),
        idempotent=True,
        wrap_ttl="30s",
        token=SecretString("s.child"),
        total_timeout=timedelta(seconds=30),
        api_version="v2",
    )
    assert options.idempotent is True
    assert options.api_version == "v2"


def test_request_options_headers_are_defensively_copied() -> None:
    """@req CFG-061"""
    caller_headers = {"X-Extra": "value"}
    options = RequestOptions(headers=caller_headers)
    caller_headers["X-Extra"] = "mutated"
    assert options.headers["X-Extra"] == "value"


def test_retry_policy_rejects_non_string_retry_on_entries() -> None:
    """@req D-M1b-7"""
    with pytest.raises(BastionVaultError) as excinfo:
        RetryPolicy(retry_on=("BV-TRANSPORT-001", 5))  # type: ignore[arg-type]
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert excinfo.value.details["setting"] == "RetryPolicy.RetryOn"


def test_retry_policy_defaults_match_cfg_050() -> None:
    """@req CFG-050"""
    policy = RetryPolicy()
    assert policy.max_attempts == 3
    assert policy.retry_on == (
        "BV-TRANSPORT-001",
        "BV-TRANSPORT-002",
        "BV-SERVER-002",
        "BV-SERVER-003",
    )
    assert policy.retry_idempotent_only is True


def test_system_clock_now_and_zero_delay_do_not_block() -> None:
    """@req RES-003"""
    clock = SystemClock()
    before = clock.now()
    asyncio.run(clock.delay(timedelta(0)))
    after = clock.now()
    assert after >= before


def test_system_jitter_source_returns_value_in_unit_interval() -> None:
    """@req RES-003"""
    jitter = SystemJitterSource()
    value = jitter.next_double()
    assert 0.0 <= value < 1.0


def test_fake_transport_records_and_replays_in_order() -> None:
    """@req TRN-100"""
    transport = FakeTransport(
        exchanges=[
            TransportResponse(status_code=200, headers={}, body=b"{}"),
            make_error(ErrorCodes.TRANSPORT_TIMEOUT),
        ],
        supports_custom_verbs=False,
    )
    assert transport.supports_custom_verbs is False

    first = asyncio.run(transport.send(TransportRequest(method="GET", url="https://x.invalid/a")))
    assert first.status_code == 200

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(transport.send(TransportRequest(method="GET", url="https://x.invalid/b")))
    assert excinfo.value.code == ErrorCodes.TRANSPORT_TIMEOUT

    assert len(transport.requests) == 2
    transport.assert_exhausted()


def test_fake_transport_raises_when_out_of_scripted_exchanges() -> None:
    """@req TRN-100"""
    transport = FakeTransport(exchanges=[])
    with pytest.raises(AssertionError, match="no scripted exchange"):
        asyncio.run(transport.send(TransportRequest(method="GET", url="https://x.invalid/a")))


def test_fake_transport_assert_exhausted_fails_when_exchanges_remain() -> None:
    """@req TRN-100"""
    transport = FakeTransport(exchanges=[TransportResponse(status_code=200)])
    with pytest.raises(AssertionError, match="were not consumed"):
        transport.assert_exhausted()
