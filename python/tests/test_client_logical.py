"""Unit tests for `Client`/`Logical` behaviour not already exercised by the transport
fixtures: runtime mutation (CFG-070/071), the observability hook (CFG-080/081), the
retry deadline (RES-004), idempotency overrides (D-M1b-6), and `Logical.Raw`'s success
path (D-M1b-12). No test here sleeps in real time (RES-003): `Clock`/`JitterSource` are
always injected fakes.
"""

from __future__ import annotations

import asyncio
import json
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any

import pytest

from bastionvault_integration_sdk import Client, ClientOptions, RequestOptions, RetryPolicy, SecretString
from bastionvault_integration_sdk.errors import BastionVaultError, ErrorCodes, make_error
from bastionvault_integration_sdk.testing import FakeTransport
from bastionvault_integration_sdk.transport import RequestEvent, TransportResponse


class _FrozenClock:
    def __init__(self, start: datetime | None = None) -> None:
        self._now = start or datetime(2026, 1, 1, tzinfo=timezone.utc)

    def now(self) -> datetime:
        return self._now

    async def delay(self, duration: timedelta) -> None:
        self._now += duration


class _ZeroJitter:
    def next_double(self) -> float:
        return 0.5


@dataclass
class _RecordingObserver:
    events: list[RequestEvent] = field(default_factory=list)

    def on_request_completed(self, event: RequestEvent) -> None:
        self.events.append(event)


def _json_response(status: int, body: Any, headers: dict[str, str] | None = None) -> TransportResponse:
    return TransportResponse(
        status_code=status, headers=headers or {}, body=json.dumps(body).encode("utf-8")
    )


def test_cfg_070_set_token_and_clear_token_affect_next_snapshot() -> None:
    """@req CFG-070"""
    transport = FakeTransport(
        exchanges=[
            _json_response(200, {"data": {"a": 1}}),
            _json_response(200, {"data": {"a": 2}}),
        ]
    )
    client = Client(
        ClientOptions(address="https://vault.example.com:8200", token="s.initial"),
        transport=transport,
        clock=_FrozenClock(),
        jitter_source=_ZeroJitter(),
    )
    asyncio.run(client.logical.read("secret/data/x"))
    assert transport.requests[0].headers["X-BastionVault-Token"] == "s.initial"

    client.set_token(SecretString("s.rotated"))
    asyncio.run(client.logical.read("secret/data/x"))
    assert transport.requests[1].headers["X-BastionVault-Token"] == "s.rotated"

    client.clear_token()
    transport.exchanges = [*transport.exchanges, _json_response(200, {"data": {}})]
    asyncio.run(client.logical.read("secret/data/x"))
    assert "X-BastionVault-Token" not in transport.requests[2].headers


def test_cfg_071_with_namespace_shares_token_cell_but_not_namespace() -> None:
    """@req CFG-071"""
    transport = FakeTransport(
        exchanges=[_json_response(200, {"data": {}}), _json_response(200, {"data": {}})]
    )
    client = Client(
        ClientOptions(address="https://vault.example.com:8200", token="s.shared", namespace="root"),
        transport=transport,
        clock=_FrozenClock(),
        jitter_source=_ZeroJitter(),
    )
    view = client.with_namespace("team")

    asyncio.run(view.logical.read("secret/data/x"))
    assert transport.requests[0].headers["X-BastionVault-Namespace"] == "team"

    client.set_token(SecretString("s.rotated-on-parent"))
    asyncio.run(view.logical.read("secret/data/x"))
    assert transport.requests[1].headers["X-BastionVault-Token"] == "s.rotated-on-parent"


def test_cfg_080_observer_fires_once_per_attempt_with_no_body_or_token() -> None:
    """@req CFG-080 @req RES-002"""
    observer = _RecordingObserver()
    transport = FakeTransport(
        exchanges=[make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED), _json_response(200, {"data": {}})]
    )
    options = ClientOptions(
        address="https://vault.example.com:8200",
        token="s.secret-value-should-never-appear",
        observer=observer,
        retry_policy=RetryPolicy(initial_backoff=timedelta(0)),
    )
    client = Client(options, transport=transport, clock=_FrozenClock(), jitter_source=_ZeroJitter())

    asyncio.run(client.logical.read("secret/data/x"))

    assert len(observer.events) == 2
    assert observer.events[0].attempt == 1
    assert observer.events[0].error_code == ErrorCodes.TRANSPORT_CONNECTION_FAILED
    assert observer.events[1].attempt == 2
    assert observer.events[1].error_code is None
    for event in observer.events:
        assert "s.secret-value-should-never-appear" not in repr(event)


def test_res_004_total_timeout_cuts_retries_short() -> None:
    """@req RES-004"""
    clock = _FrozenClock()
    transport = FakeTransport(
        exchanges=[
            make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED),
            make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED),
        ]
    )
    client = Client(
        ClientOptions(
            address="https://vault.example.com:8200",
            retry_policy=RetryPolicy(max_attempts=5, initial_backoff=timedelta(seconds=100)),
        ),
        transport=transport,
        clock=clock,
        jitter_source=_ZeroJitter(),
    )

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(
            client.logical.read(
                "secret/data/x", RequestOptions(total_timeout=timedelta(seconds=0))
            )
        )
    assert excinfo.value.code == ErrorCodes.TRANSPORT_CONNECTION_FAILED
    assert excinfo.value.attempts == 1
    assert len(transport.requests) == 1  # deadline already elapsed; no second attempt is made


def test_d_m1b_6_explicit_idempotent_option_allows_write_retry() -> None:
    """@req D-M1b-6 @req CFG-051"""
    transport = FakeTransport(
        exchanges=[make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED), _json_response(200, {"data": {}})]
    )
    client = Client(
        ClientOptions(
            address="https://vault.example.com:8200",
            retry_policy=RetryPolicy(initial_backoff=timedelta(0)),
        ),
        transport=transport,
        clock=_FrozenClock(),
        jitter_source=_ZeroJitter(),
    )

    result = asyncio.run(
        client.logical.write("secret/data/x", {"a": 1}, RequestOptions(idempotent=True))
    )

    assert result is not None
    assert len(transport.requests) == 2


def test_d_m1b_6_explicit_non_idempotent_blocks_read_retry() -> None:
    """@req D-M1b-6"""
    transport = FakeTransport(exchanges=[make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED)])
    client = Client(
        ClientOptions(
            address="https://vault.example.com:8200",
            retry_policy=RetryPolicy(initial_backoff=timedelta(0)),
        ),
        transport=transport,
        clock=_FrozenClock(),
        jitter_source=_ZeroJitter(),
    )

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.read("secret/data/x", RequestOptions(idempotent=False)))
    assert excinfo.value.attempts == 1


def test_d_m1b_14_transport_declaring_no_custom_verbs_fails_construction() -> None:
    """@req TRN-010 @req D-M1b-14"""
    transport = FakeTransport(exchanges=[], supports_custom_verbs=False)
    with pytest.raises(BastionVaultError) as excinfo:
        Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)
    assert excinfo.value.code == ErrorCodes.CONFIG_LIST_VERB_UNSUPPORTED


def test_d_m1b_12_raw_success_path_is_unparsed() -> None:
    """@req D-M1b-12"""
    transport = FakeTransport(
        exchanges=[TransportResponse(status_code=200, headers={}, body=b"<html>not json</html>")]
    )
    client = Client(
        ClientOptions(address="https://vault.example.com:8200"), transport=transport, clock=_FrozenClock()
    )

    result = asyncio.run(client.logical.raw("GET", "/v1/sys/health"))

    assert result.status_code == 200
    assert result.body == b"<html>not json</html>"


def test_d_m1b_12_raw_redirect_is_protocol_003() -> None:
    """@req TRN-060 @req D-M1b-12"""
    transport = FakeTransport(exchanges=[TransportResponse(status_code=302, headers={}, body=b"")])
    client = Client(
        ClientOptions(address="https://vault.example.com:8200"), transport=transport, clock=_FrozenClock()
    )

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.raw("GET", "/v1/sys/health"))
    assert excinfo.value.code == ErrorCodes.PROTOCOL_UNEXPECTED_REDIRECT


def test_trn_017_wrap_ttl_is_unsupported() -> None:
    """@req TRN-017"""
    transport = FakeTransport(exchanges=[])
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.read("secret/data/x", RequestOptions(wrap_ttl="30s")))
    assert excinfo.value.code == ErrorCodes.INPUT_UNSUPPORTED_OPTION


def test_trn_032_oversized_body_is_rejected_client_side() -> None:
    """@req TRN-032"""
    transport = FakeTransport(exchanges=[])
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.write("secret/data/x", {"blob": "x" * (33 * 1024 * 1024)}))
    assert excinfo.value.code == ErrorCodes.INPUT_BODY_TOO_LARGE
    assert excinfo.value.attempts == 0


def test_trn_012_reserved_header_in_request_options_is_rejected() -> None:
    """@req TRN-012"""
    transport = FakeTransport(exchanges=[])
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(
            client.logical.read("secret/data/x", RequestOptions(headers={"Authorization": "Bearer x"}))
        )
    assert excinfo.value.code == ErrorCodes.CONFIG_RESERVED_HEADER


def test_304_not_modified_result_has_absent_data() -> None:
    """@req D-M1b-10"""
    transport = FakeTransport(exchanges=[TransportResponse(status_code=304, headers={}, body=b"")])
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    result = asyncio.run(client.logical.read("secret/data/x"))

    assert result is not None
    assert result.status_code == 304
    assert result.data is None


def test_write_404_empty_body_is_not_absent_but_an_error() -> None:
    """@req D-M1b-11"""
    transport = FakeTransport(exchanges=[TransportResponse(status_code=404, headers={}, body=b"")])
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.write("secret/data/x", {"a": 1}))
    assert excinfo.value.code == ErrorCodes.NOT_FOUND_PATH_NOT_FOUND


def test_error_response_snippet_is_sanitized_and_capped() -> None:
    """@req TRN-053"""
    long_body = "<html>" + ("x" * 400) + "\x00control\x01</html>"
    transport = FakeTransport(
        exchanges=[
            TransportResponse(
                status_code=500,
                headers={"Content-Type": "text/html"},
                body=long_body.encode("utf-8"),
            )
        ]
    )
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.read("secret/data/x"))
    assert excinfo.value.code == ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE
    snippet = excinfo.value.details["snippet"]
    assert len(snippet) <= 256
    assert "\x00" not in snippet


def test_error_body_with_invalid_json_maps_to_protocol_002() -> None:
    """@req D-M1b-23"""
    transport = FakeTransport(
        exchanges=[TransportResponse(status_code=500, headers={}, body=b"{not valid json")]
    )
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.read("secret/data/x"))
    assert excinfo.value.code == ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE


def test_raw_error_body_with_invalid_json_maps_to_protocol_002() -> None:
    """@req D-M1b-12 @req D-M1b-23"""
    transport = FakeTransport(
        exchanges=[TransportResponse(status_code=500, headers={}, body=b"{not valid json")]
    )
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    with pytest.raises(BastionVaultError) as excinfo:
        asyncio.run(client.logical.raw("GET", "/v1/sys/x"))
    assert excinfo.value.code == ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE


def test_auth_object_without_client_token_is_not_treated_as_auth() -> None:
    """@req TRN-040"""
    transport = FakeTransport(
        exchanges=[_json_response(200, {"data": {"a": 1}, "auth": {"policies": ["default"]}})]
    )
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    result = asyncio.run(client.logical.read("secret/data/x"))

    assert result is not None
    assert result.auth is None


def test_list_operation_uses_literal_list_verb() -> None:
    """@req TRN-010"""
    transport = FakeTransport(exchanges=[_json_response(200, {"data": {"keys": []}})])
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    asyncio.run(client.logical.list("secret/metadata/app/"))

    assert transport.requests[0].method == "LIST"


def test_delete_operation_sends_optional_body() -> None:
    """@req TRN-002"""
    transport = FakeTransport(exchanges=[TransportResponse(status_code=204, headers={}, body=b"")])
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    result = asyncio.run(client.logical.delete("secret/data/x", {"versions": [1]}))

    assert result is None
    sent_body = transport.requests[0].body
    assert sent_body is not None
    assert transport.requests[0].method == "DELETE"
    assert json.loads(sent_body.decode("utf-8")) == {"versions": [1]}


def test_cfg_054_retry_after_widens_the_computed_backoff() -> None:
    """@req CFG-054"""
    clock = _FrozenClock()
    sealed_body = json.dumps({"error": "no leader"}).encode()
    transport = FakeTransport(
        exchanges=[
            TransportResponse(status_code=503, headers={"Retry-After": "2"}, body=sealed_body),
            _json_response(200, {"data": {}}),
        ]
    )
    retry_policy = RetryPolicy(initial_backoff=timedelta(milliseconds=1), max_backoff=timedelta(seconds=5))
    client = Client(
        ClientOptions(
            address="https://vault.example.com:8200",
            retry_policy=retry_policy,
        ),
        transport=transport,
        clock=clock,
        jitter_source=_ZeroJitter(),
    )

    asyncio.run(client.logical.read("secret/data/x"))

    assert len(transport.requests) == 2
    assert clock.now() - datetime(2026, 1, 1, tzinfo=timezone.utc) >= timedelta(seconds=2)


def test_res_004_deadline_exceeded_between_backoff_and_next_attempt_stops_retrying() -> None:
    """@req RES-004"""
    clock = _FrozenClock()
    transport = FakeTransport(
        exchanges=[
            make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED),
            make_error(ErrorCodes.TRANSPORT_CONNECTION_FAILED),
        ]
    )
    client = Client(
        ClientOptions(
            address="https://vault.example.com:8200",
            retry_policy=RetryPolicy(max_attempts=5, initial_backoff=timedelta(seconds=10)),
        ),
        transport=transport,
        clock=clock,
        jitter_source=_ZeroJitter(),
    )

    with pytest.raises(BastionVaultError):
        asyncio.run(
            client.logical.read("secret/data/x", RequestOptions(total_timeout=timedelta(seconds=5)))
        )

    # The first failure's backoff (10s) is clamped to the 5s remaining and still fits,
    # so one retry happens; the deadline then stops a second retry from being attempted.
    assert len(transport.requests) == 2


class _HangingTransport:
    """A transport whose `send` never completes on its own -- only cancellation ends it."""

    supports_custom_verbs = True

    async def send(self, request: Any) -> Any:
        del request
        await asyncio.sleep(3600)
        raise AssertionError("should have been cancelled before this point")


def test_ovr_006_cancellation_surfaces_as_bv_transport_005() -> None:
    """@req OVR-006"""

    async def scenario() -> BastionVaultError:
        client = Client(
            ClientOptions(address="https://vault.example.com:8200"), transport=_HangingTransport()
        )
        task = asyncio.ensure_future(client.logical.read("secret/data/x"))
        await asyncio.sleep(0)
        task.cancel()
        try:
            await task
        except BastionVaultError as error:
            return error
        raise AssertionError("expected a BastionVaultError, task completed normally")

    error = asyncio.run(scenario())
    assert error.code == ErrorCodes.TRANSPORT_CANCELLED


def test_ovr_005_one_client_serves_concurrent_tasks_independently() -> None:
    """@req OVR-005"""
    transport = FakeTransport(
        exchanges=[_json_response(200, {"data": {"n": i}}) for i in range(5)]
    )
    client = Client(ClientOptions(address="https://vault.example.com:8200"), transport=transport)

    async def scenario() -> list[Any]:
        return await asyncio.gather(*(client.logical.read(f"secret/data/{i}") for i in range(5)))

    results = asyncio.run(scenario())

    assert [result.data["n"] for result in results if result is not None] == [0, 1, 2, 3, 4]
