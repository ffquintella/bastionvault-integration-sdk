"""`FakeTransport`: the SDK's test-support `Transport` (TRN-100, D-M1b-15).

Ships as public API -- CNF-027 reviews it like any other public type -- so that a
fixture (or an application's own test suite) drives the *same* transport
implementation production code runs against, never a private duplicate (D-M0-2,
D-M1a-6). It only records and replays; comparing a recorded request against an
expectation is a test-harness concern, not this class's (that stays fixture-schema
knowledge, kept out of shipped code).
"""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass, field

from .errors import BastionVaultError
from .transport import TransportRequest, TransportResponse

ScriptedExchange = TransportResponse | BastionVaultError


@dataclass
class FakeTransport:
    """Replay a fixed sequence of responses (or transport-level errors), in order.

    Every call to `send` is recorded verbatim in `requests`, whatever the outcome, so a
    caller can assert on `method`, `url`, `headers` and `body` after the fact (TRN-100).
    """

    exchanges: Sequence[ScriptedExchange] = field(default_factory=tuple)
    supports_custom_verbs: bool = True
    requests: list[TransportRequest] = field(default_factory=list, init=False)
    _position: int = field(default=0, init=False)

    async def send(self, request: TransportRequest) -> TransportResponse:
        self.requests.append(request)
        if self._position >= len(self.exchanges):
            raise AssertionError(
                f"FakeTransport: request {len(self.requests)} has no scripted exchange "
                f"(only {len(self.exchanges)} were scripted)"
            )
        exchange = self.exchanges[self._position]
        self._position += 1
        if isinstance(exchange, BastionVaultError):
            raise exchange
        return exchange

    def assert_exhausted(self) -> None:
        """Fail loudly if not every scripted exchange was consumed."""
        if self._position != len(self.exchanges):
            remaining = len(self.exchanges) - self._position
            raise AssertionError(f"FakeTransport: {remaining} scripted exchange(s) were not consumed")


__all__ = ["FakeTransport"]
