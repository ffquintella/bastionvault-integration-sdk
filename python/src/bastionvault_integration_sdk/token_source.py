"""AUT-001's token source, and D-M2-9's asynchronous resolution seam.

A `Client` holds exactly one `TokenSource`, reachable as `Client.auth.token_source`;
`Client.set_token` and `Client.auth.token.use` replace it with a `Static` one.

`TokenSource.resolve` is **asynchronous** in all three SDKs (D-M2-9), because two of the
three variants perform I/O. The executor resolves *through* this type rather than reading a
field, and it does so exactly once per pass, above the retry loop, which is CFG-070's
"in-flight requests keep the token they started with" (D-M1b-9 -- the snapshot did not move,
only its source changed).

The `Login` variant's public factory lands with its credential types in M2b, together with
AUT-002's login call. M2a ships the variant's *contract*: its kind, and the single-flight
guarantee of ruling D-M2-11(a), reachable internally so the concurrency invariants are
asserted in the slice that decides them rather than the slice that first uses them. A
factory added later is not a breaking change; a factory that throws today would be a stub,
which D-M1c-25 forbids.
"""

from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable
from enum import Enum

from .secrets import SecretString

#: The performer signature behind the `Login` variant, and the shape `Callback` exposes
#: publicly. `Callback` is asynchronous because the specification's own example for it is a
#: KMS lookup (D-M2-6); a synchronous signature would force every real implementation to
#: block the event loop inside the request path. Python takes no cancellation-token
#: parameter -- `asyncio` cancellation is delivered to the awaiting task, which is the
#: idiom mapping of .NET's `CancellationToken` argument.
TokenPerformer = Callable[[], Awaitable[SecretString]]


class TokenSourceKind(Enum):
    """Which of AUT-001's three token-source variants a `TokenSource` is.

    The minimal observable that makes AUT-001's "exactly one" and "`set_token` replaces it
    with `Static`" assertable at all (D-M2-12).
    """

    STATIC = "Static"
    """A token held directly: from configuration, `set_token`, or the token file."""

    LOGIN = "Login"
    """A login performed on first use and cached (AUT-002). The login call itself is M2b."""

    CALLBACK = "Callback"
    """An application-provided asynchronous function (for example, secrets from a KMS)."""


class TokenSource:
    """AUT-001's token source. A `Client` holds exactly one."""

    __slots__ = ("_kind", "_static_token", "_callback", "_login", "_cached", "_flight", "_lock", "_lock_loop")

    def __init__(
        self,
        kind: TokenSourceKind,
        *,
        static_token: SecretString | None = None,
        callback: TokenPerformer | None = None,
    ) -> None:
        """Python cannot hide a constructor the way .NET's private `.ctor` does, so this
        signature is the public surface whether or not it is meant to be used. It therefore
        deliberately takes **no login performer**: D-M2-12 defers the public `Login` factory
        to M2b, and a `login=` keyword here would be that factory by another name. The
        performer is attached by `_login_with` instead. Prefer `static` or `callback`.
        """
        self._kind = kind
        self._static_token = static_token
        self._callback = callback
        self._login: TokenPerformer | None = None
        self._cached: SecretString | None = None
        self._flight: asyncio.Task[SecretString] | None = None
        self._lock: asyncio.Lock | None = None
        self._lock_loop: asyncio.AbstractEventLoop | None = None

    @property
    def kind(self) -> TokenSourceKind:
        """Which variant this is (D-M2-12)."""
        return self._kind

    @staticmethod
    def static(token: SecretString) -> "TokenSource":
        """A source holding `token` directly (AUT-001)."""
        return TokenSource(TokenSourceKind.STATIC, static_token=token)

    @staticmethod
    def callback(callback: TokenPerformer) -> "TokenSource":
        """A source that calls `callback` on every resolution (AUT-001).

        Deliberately **not** de-duplicated: a callback is the application's own coroutine
        function and the SDK does not get to coalesce its calls on its behalf
        (D-M2-11(a)).
        """
        return TokenSource(TokenSourceKind.CALLBACK, callback=callback)

    @staticmethod
    def _login_with(login: TokenPerformer) -> "TokenSource":
        """The `Login` variant with its login performed by `login`.

        Private at M2a: the performer is M2b's login call (AUT-002), and the single-flight
        machinery around it is M2a's (D-M2-11(a)).
        """
        source = TokenSource(TokenSourceKind.LOGIN)
        source._login = login
        return source

    async def resolve(self) -> SecretString | None:
        """D-M2-9's seam: the token this source stands for, resolving it if that takes I/O.

        A `Login` resolution is single-flighted (D-M2-11(a)): N concurrent first-use
        resolutions await **one** login rather than queueing N. An awaiting caller that is
        cancelled abandons only its own wait -- the shared login keeps running, because one
        caller's cancellation must not fail the other callers awaiting the same flight.

        A flight that *fails* is not cached (D-M2-17): its awaiters all observe its failure
        and none of them retries inside its own call, while the next resolution after it
        attempts again. Without the re-arm, one transient login failure at startup would
        brick the client for its whole lifetime -- emphatically not what D-M2-11(a) decided,
        which made concurrent callers share one login *attempt*, not one permanent verdict.
        """

        if self._callback is not None:
            return await self._callback()

        if self._login is None:
            # A `Static` resolve performs no I/O, so there is nothing to cancel and no
            # await point. Cancellation of a `Static`-sourced request is mapped where the
            # request is actually made (`BV-TRANSPORT-005`), not here, because raising a
            # bare `CancelledError` on this path is what ERR-020/TRN-054 forbid.
            return self._static_token

        cached = self._cached
        if cached is not None:
            return cached

        async with self._flight_lock():
            # The second half of D-M2-11(a)'s double-checked cache read: a loser of the
            # race finds the winner's token already cached and never starts a login. The
            # lock is held only long enough to create or adopt the flight -- never across
            # the login itself, which would serialise unrelated resolutions.
            cached = self._cached
            if cached is not None:
                return cached
            flight = self._flight
            if flight is None:
                # `_invoke_login` is a coroutine function, so calling it cannot raise: a
                # performer that throws *synchronously*, before its first await, faults the
                # task instead of this line. That matters because a synchronous throw
                # escaping here would propagate to the caller holding the lock with no
                # flight recorded, and D-M2-17 requires the failure to be observed by the
                # awaiters of a flight and then re-armed.
                flight = asyncio.ensure_future(self._invoke_login())
                self._flight = flight

        try:
            # `shield` is why one awaiter's cancellation does not cancel the shared login.
            token = await asyncio.shield(flight)
        except BaseException:
            # D-M2-17's re-arm, conditional on *this flight* having actually finished
            # badly. A caller whose own await was cancelled while the login is still
            # running must leave the flight alone -- it is still the live attempt for
            # everybody else. `cancelled()` is checked alongside a raised exception because
            # a performer that raises `CancelledError` itself lands the task in the
            # cancelled state, and that is just as poisonous to cache. `CancelledError`
            # derives from `BaseException`, not `Exception`, so this guard is deliberately
            # `except BaseException` -- a bare `except Exception` would not see it
            # (D-M2-18 item 1).
            if flight.done() and (flight.cancelled() or flight.exception() is not None):
                # Compare-and-set, so the N awaiters of one failed flight re-arm it once
                # between them and a later resolution's fresh flight is never clobbered by
                # a straggler.
                if self._flight is flight:
                    self._flight = None
            raise

        self._cached = token
        self._flight = None
        return token

    def _invalidate(self) -> None:
        """Discard a cached `Login` result so the next `resolve` logs in again.

        A no-op on the other two variants: `Static` has nothing to re-resolve and
        `Callback` already resolves every time.
        """
        if self._login is not None:
            self._cached = None
            self._flight = None

    def _flight_lock(self) -> asyncio.Lock:
        """D-M2-11(a)'s named primitive, bound to the loop that is actually running.

        The ruling names "a module-private `asyncio.Lock` with a double-checked cache
        read". The lock is private to this module, as ruled, but it is created per source
        and re-created if the running loop changed: an `asyncio.Lock` is affine to the loop
        it was first awaited on, and a single module-level instance would raise as soon as
        a second `asyncio.run` -- which is every fixture in the harness -- touched it. The
        concurrency contract the ruling pinned is unaffected, because the lock's only job
        is to make the cache read and the flight creation atomic with respect to each
        other.
        """
        loop = asyncio.get_running_loop()
        if self._lock is None or self._lock_loop is not loop:
            self._lock = asyncio.Lock()
            self._lock_loop = loop
        return self._lock

    async def _invoke_login(self) -> SecretString:
        assert self._login is not None
        return await self._login()


__all__ = ["TokenPerformer", "TokenSource", "TokenSourceKind"]
