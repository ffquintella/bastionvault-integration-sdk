"""Secret-bearing settings use a redacting type (D-M1a-9, CNF-031/032, D-M2-3)."""

from __future__ import annotations

from typing import Final

#: CNF-032's redaction marker, identical in all three SDKs (D-M2-3). Python rendered
#: `SecretString(***)` until M2a while .NET and Rust rendered `[REDACTED]`; a
#: developer-facing string that differs across languages is the R-9 defect class, and
#: here it also made TST-051's "no secret appears in any captured line" assertion three
#: assertions instead of one.
REDACTION_MARKER: Final = "[REDACTED]"


class SecretString:
    """Holds a secret value whose default string form never reveals it.

    Revealing the value is an explicitly named method (`reveal`), never `__str__`,
    `__repr__`, or a plain attribute, so every read of secret material is a
    searchable call site (D-M1a-17).
    """

    __slots__ = ("_value",)

    def __init__(self, value: str) -> None:
        self._value = value

    def reveal(self) -> str:
        """Return the underlying secret value. The only way to read it."""
        return self._value

    def __str__(self) -> str:
        return REDACTION_MARKER

    def __repr__(self) -> str:
        return f'SecretString("{REDACTION_MARKER}")'
