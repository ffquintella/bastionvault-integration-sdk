"""Unit tests for the D-M1b-4 status->code mapping and TRN-052 body parsing."""

from __future__ import annotations

import pytest

from bastionvault_integration_sdk.errors import (
    ErrorCodes,
    make_error,
    map_status_to_code,
    parse_error_body,
)


@pytest.mark.parametrize(
    ("status", "server_message", "retry_after_present", "expected"),
    [
        (400, None, False, ErrorCodes.INPUT_INVALID_ARGUMENT),  # D-M1b-21 fallback
        (409, "digest mismatch", False, ErrorCodes.INPUT_INVALID_ARGUMENT),  # D-M1b-23 best-effort
        (401, None, False, ErrorCodes.AUTH_UNAUTHENTICATED),
        (403, None, False, ErrorCodes.AUTHZ_PERMISSION_DENIED),
        (404, None, False, ErrorCodes.NOTFOUND_PATH_NOT_FOUND),
        (405, None, False, ErrorCodes.PROTOCOL_METHOD_NOT_ALLOWED),
        (416, None, False, ErrorCodes.INPUT_CHUNK_INDEX_OUT_OF_RANGE),
        (429, None, True, ErrorCodes.RATE_LIMITED_BY_DOS_GUARD),
        (429, None, False, ErrorCodes.NAMESPACE_RATE_QUOTA_EXCEEDED),
        (503, "BastionVault is Sealed.", False, ErrorCodes.SERVER_SEALED),
        (503, "no leader", False, ErrorCodes.SERVER_UNAVAILABLE),
        (503, None, False, ErrorCodes.SERVER_UNAVAILABLE),
        (502, None, False, ErrorCodes.SERVER_UNAVAILABLE),
        (504, None, False, ErrorCodes.SERVER_UNAVAILABLE),
        (507, None, False, ErrorCodes.QUOTA_NAMESPACE_QUOTA_EXCEEDED),
        (307, None, False, ErrorCodes.PROTOCOL_UNEXPECTED_REDIRECT),
        (450, None, False, ErrorCodes.INPUT_INVALID_ARGUMENT),
        (500, None, False, ErrorCodes.SERVER_INTERNAL_ERROR),
        (599, None, False, ErrorCodes.SERVER_INTERNAL_ERROR),
        (199, None, False, ErrorCodes.PROTOCOL_UNEXPECTED_RESPONSE),
    ],
)
def test_map_status_to_code(
    status: int, server_message: str | None, retry_after_present: bool, expected: str
) -> None:
    """@req TRN-054"""
    assert (
        map_status_to_code(status, server_message=server_message, retry_after_present=retry_after_present)
        == expected
    )


def test_parse_error_body_empty_is_no_message() -> None:
    """@req TRN-052"""
    assert parse_error_body("") == parse_error_body("   ")
    result = parse_error_body("")
    assert result.server_message is None
    assert result.server_errors == ()


def test_parse_error_body_singular_error_shape() -> None:
    """@req TRN-052"""
    result = parse_error_body('{"error": "invalid request"}')
    assert result.server_message == "invalid request"
    assert result.server_errors == ()


def test_parse_error_body_errors_array_shape_is_joined() -> None:
    """@req TRN-052"""
    result = parse_error_body('{"errors": ["a problem", "another problem"]}')
    assert result.server_message == "a problem; another problem"
    assert result.server_errors == ("a problem", "another problem")


def test_parse_error_body_unparsable_json_is_no_message() -> None:
    """@req TRN-052 @req TRN-053"""
    result = parse_error_body("not json at all")
    assert result.server_message is None


def test_parse_error_body_non_string_fields_are_ignored() -> None:
    """@req TRN-052"""
    assert parse_error_body('{"error": 42}').server_message is None
    assert parse_error_body('{"errors": [1, 2]}').server_message is None
    assert parse_error_body("[1, 2, 3]").server_message is None


def test_make_error_appends_extra_hint() -> None:
    error = make_error(ErrorCodes.SERVER_SEALED, extra_hint="Extra context.")
    assert error.hint.endswith("Extra context.")
    assert error.retryable is False
    assert error.category.value == "server_state"
