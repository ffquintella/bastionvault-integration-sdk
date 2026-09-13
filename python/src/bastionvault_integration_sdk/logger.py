"""A public logger seam so CNF-030's warning is observable in tests (D-M1a-17)."""

from __future__ import annotations

from typing import Protocol, runtime_checkable


@runtime_checkable
class ClientLogger(Protocol):
    """Runtime-idiomatic logging hook (the `Logger` setting)."""

    def warning(self, message: str) -> None:
        """Emit a warning-level line. Never receives secret material."""
        ...


class NoOpClientLogger:
    """The default `Logger`: emits nothing."""

    def warning(self, message: str) -> None:
        return None
