"""Unit tests for the shared boolean/duration/integer parsers (D-M1a-4/17)."""

import pytest

from bastionvault_integration_sdk.settings import (
    InvalidSettingValueError,
    parse_bool,
    parse_duration_seconds,
    parse_int,
)


def test_parse_bool_rejects_unknown_values() -> None:
    with pytest.raises(InvalidSettingValueError):
        parse_bool("maybe")


def test_parse_duration_seconds_rejects_empty_string() -> None:
    with pytest.raises(InvalidSettingValueError):
        parse_duration_seconds("")


def test_parse_duration_seconds_rejects_gaps_between_tokens() -> None:
    with pytest.raises(InvalidSettingValueError):
        parse_duration_seconds("5s 3m")


def test_parse_duration_seconds_rejects_trailing_garbage() -> None:
    with pytest.raises(InvalidSettingValueError):
        parse_duration_seconds("5sx")


def test_parse_int_rejects_non_integers() -> None:
    with pytest.raises(InvalidSettingValueError):
        parse_int("not-a-number")


def test_parse_int_accepts_negative_values() -> None:
    assert parse_int("-3") == -3
