"""The injected environment seam (D-M1a-3).

Resolution never calls the process environment directly. It reads an
`EnvironmentSource`, which has three provided forms: Process (default), None, and Map.
This is also CFG-005's env-ignoring constructor (`NoneEnvironmentSource`) and the way
tests inject environment values without mutating `os.environ` (OVR-004).
"""

from __future__ import annotations

import os
from collections.abc import Mapping
from typing import Protocol, runtime_checkable


@runtime_checkable
class EnvironmentSource(Protocol):
    """A source of environment variable values, injected at `Client` construction."""

    def get(self, name: str) -> str | None:
        """Return the value of `name`, or `None` if it is not set."""
        ...


class ProcessEnvironmentSource:
    """Reads the real process environment (the default form, D-M1a-3)."""

    def get(self, name: str) -> str | None:
        return os.environ.get(name)


class NoneEnvironmentSource:
    """Reads nothing; only built-in defaults and explicit values apply (CFG-005)."""

    def get(self, name: str) -> str | None:
        return None


class MapEnvironmentSource:
    """Reads a caller-supplied key/value map (Appendix C fixture injection)."""

    def __init__(self, values: Mapping[str, str]) -> None:
        self._values = dict(values)

    def get(self, name: str) -> str | None:
        return self._values.get(name)
