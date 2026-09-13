"""Tests for the redacting secret holder (D-M1a-9, D-M1a-17)."""

from bastionvault_integration_sdk import SecretString


def test_secret_string_never_reveals_value_in_str_or_repr() -> None:
    secret = SecretString("s.super-secret-token")

    assert "super-secret-token" not in str(secret)
    assert "super-secret-token" not in repr(secret)
    assert secret.reveal() == "s.super-secret-token"
