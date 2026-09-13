"""Shared settings-table primitives (D-M1a-4/13/17).

One boolean parser, one duration parser, one integer parser, shared by every setting
that needs one, plus the small value objects for settings that are not scalar
(`RetryPolicy` lives in `transport.py`; `RateGate` and `AutoRenew` live here).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import timedelta
from typing import Final

# CFG-017 / TRN-012: reserved header names, matched case-insensitively.
RESERVED_HEADERS: Final[tuple[str, ...]] = (
    "X-BastionVault-Token",
    "X-Vault-Token",
    "Authorization",
    "Cookie",
    "X-BastionVault-Namespace",
    "Host",
    "Content-Length",
)
RESERVED_HEADERS_CASEFOLD: Final[frozenset[str]] = frozenset(
    name.casefold() for name in RESERVED_HEADERS
)

_TRUE_VALUES: Final[frozenset[str]] = frozenset({"1", "true", "yes", "on"})
_FALSE_VALUES: Final[frozenset[str]] = frozenset({"0", "false", "no", "off", ""})

_DURATION_UNIT_SECONDS: Final[dict[str, float]] = {
    "ns": 1e-9,
    "us": 1e-6,
    "µs": 1e-6,  # micro sign, µs
    "ms": 1e-3,
    "s": 1.0,
    "m": 60.0,
    "h": 3600.0,
}
_DURATION_TOKEN_RE = re.compile(r"(\d+(?:\.\d+)?)(ns|us|µs|ms|s|m|h)")
_BARE_INTEGER_RE = re.compile(r"[+-]?\d+")


class InvalidSettingValueError(ValueError):
    """Raised by the shared parsers on a malformed value (CFG-003/CFG-004/CFG-003)."""


def parse_bool(raw: str) -> bool:
    """CFG-003: 1/true/yes/on (true), 0/false/no/off/empty (false), case-insensitive."""

    normalized = raw.strip().casefold()
    if normalized in _TRUE_VALUES:
        return True
    if normalized in _FALSE_VALUES:
        return False
    raise InvalidSettingValueError(f"not a valid boolean: {raw!r}")


def parse_duration_seconds(raw: str) -> float:
    """CFG-004: Go/Vault-style durations (`30s`, `1m30s`, `500ms`, `2h`) or bare seconds."""

    text = raw.strip()
    if not text:
        raise InvalidSettingValueError("empty duration")
    if _BARE_INTEGER_RE.fullmatch(text):
        return float(int(text))

    total = 0.0
    position = 0
    matched_any = False
    for match in _DURATION_TOKEN_RE.finditer(text):
        if match.start() != position:
            raise InvalidSettingValueError(f"not a valid duration: {raw!r}")
        total += float(match.group(1)) * _DURATION_UNIT_SECONDS[match.group(2)]
        position = match.end()
        matched_any = True
    if not matched_any or position != len(text):
        raise InvalidSettingValueError(f"not a valid duration: {raw!r}")
    return total


def parse_duration(raw: str) -> timedelta:
    return timedelta(seconds=parse_duration_seconds(raw))


def parse_int(raw: str) -> int:
    """Shared integer parser for `MAX_RETRIES`, `RATE_PER_SEC`, `RATE_BURST`."""

    text = raw.strip()
    if not _BARE_INTEGER_RE.fullmatch(text):
        raise InvalidSettingValueError(f"not a valid integer: {raw!r}")
    return int(text)


@dataclass(frozen=True)
class RateGate:
    """Client-side token bucket settings (D-M1a-13). Values only; the gate itself is M8."""

    rate_per_second: int = 8
    burst: int = 16


@dataclass(frozen=True)
class AutoRenew:
    """Background token renewal settings (D-M1a-13). Materialised, disabled; loop is M2."""

    enabled: bool = False
