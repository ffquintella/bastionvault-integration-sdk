"""The minimal M1a `Client` (D-M1a-6): a resolved `ClientConfig` plus a transport.

No operations (`Read`/`Write`/`List`), no `Auth`, no engines. No `set_address` (CFG-072) —
changing the server requires a new `Client`.
"""

from __future__ import annotations

from dataclasses import replace

from .config import ClientConfig, ClientOptions
from .environment import EnvironmentSource
from .transport import Transport


class Client:
    """A `BastionVault` client, holding a resolved `ClientConfig` and a transport."""

    def __init__(
        self,
        options: ClientOptions | None = None,
        *,
        environment: EnvironmentSource | None = None,
        transport: Transport | None = None,
    ) -> None:
        base_options = options if options is not None else ClientOptions()
        # Never mutate a caller-owned `ClientOptions` (the same hazard CFG-017's
        # headers-aliasing regression guards against, applied to the options object).
        resolved_options = (
            replace(base_options, transport=transport) if transport is not None else base_options
        )
        self._config = ClientConfig.resolve(resolved_options, environment)

    @property
    def config(self) -> ClientConfig:
        """The fully-resolved configuration this `Client` was constructed with."""
        return self._config

    @property
    def is_insecure(self) -> bool:
        """CFG-018: true when `TlsSkipVerify` disabled certificate verification."""
        return self._config.is_insecure
