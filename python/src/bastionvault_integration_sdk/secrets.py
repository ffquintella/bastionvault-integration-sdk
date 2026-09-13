"""Secret-bearing settings use a redacting type (D-M1a-9, CNF-031/032)."""

from __future__ import annotations


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
        return "SecretString(***)"

    def __repr__(self) -> str:
        return "SecretString(***)"
