"""Structural tests for the `BastionVaultError` shape (ERR-001/002/006, D-M1a-1)."""

from datetime import timedelta

from bastionvault_integration_sdk.errors import (
    RETRYABLE_CODES,
    BastionVaultError,
    ErrorCategory,
    make_config_error,
)


def test_config_error_one_line_form_has_no_http_or_server_suffix() -> None:
    error = make_config_error("BV-CONFIG-001")

    assert str(error) == (
        "BV-CONFIG-001: The server address is missing or not a valid URL or cluster "
        "name. — Set `Address` (or `BASTIONVAULT_ADDR`) to `https://host:8200`, or to "
        "a bare DNS name for cluster discovery. IPv6 literals must be bracketed."
    )
    assert error.retryable is False
    assert error.attempts == 0
    assert error.status_code is None


def test_error_one_line_form_appends_http_and_server_suffixes_when_present() -> None:
    error = BastionVaultError(
        code="BV-KV-003",
        category=ErrorCategory.CONFLICT,
        message="The check-and-set version did not match.",
        hint="Re-read the secret and retry.",
        retryable=False,
        attempts=1,
        status_code=400,
        method="POST",
        path="secret/data/app",
        server_message="check-and-set parameter did not match",
        retry_after=timedelta(seconds=1),
    )

    text = str(error)

    assert text.startswith("BV-KV-003: The check-and-set version did not match. — Re-read")
    assert "[HTTP 400 POST secret/data/app]" in text
    assert '(server: "check-and-set parameter did not match")' in text
    assert repr(error) == "BastionVaultError(code='BV-KV-003')"


def test_retryable_codes_are_exactly_the_err_006_set() -> None:
    assert RETRYABLE_CODES == {
        "BV-TRANSPORT-001",
        "BV-TRANSPORT-002",
        "BV-TRANSPORT-003",
        "BV-SERVER-002",
        "BV-SERVER-003",
        "BV-RATE-002",
        "BV-DISCOVERY-003",
    }
