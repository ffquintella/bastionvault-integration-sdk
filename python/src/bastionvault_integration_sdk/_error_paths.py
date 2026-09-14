"""ERR-002's one-line form and ERR-003's path redaction.

Both are applied once, where the error is built (`BastionVaultError.__init__` and
`__str__`), so the one-line form, any verbose form and any hint that interpolates the
path are redacted by the same rule rather than by three that can drift (D-M1c-14 item 6).
"""

from __future__ import annotations

import unicodedata
from typing import Final

#: The four token-bearing route shapes ERR-003 names.
_TOKEN_SEGMENTS: Final = ("lookup", "renew", "revoke", "revoke-orphan")


def redact(path: str | None) -> str | None:
    """Replace the segment following a token-bearing segment with ``<redacted>`` (ERR-003)."""

    if not path or "/" not in path:
        return path
    segments = path.split("/")
    changed = False
    for index in range(len(segments) - 1):
        if not segments[index + 1]:
            continue
        if segments[index].casefold() in _TOKEN_SEGMENTS:
            segments[index + 1] = "<redacted>"
            changed = True
    return "/".join(segments) if changed else path


def one_line(value: str) -> str:
    """Collapse every newline and control character to a single space (ERR-002)."""

    builder: list[str] = []
    last_was_space = False
    for character in value:
        if character in "\r\n" or unicodedata.category(character) == "Cc":
            if not last_was_space:
                builder.append(" ")
                last_was_space = True
            continue
        builder.append(character)
        last_was_space = character == " "
    return "".join(builder)


__all__ = ["one_line", "redact"]
