"""Step 4 of the ERR-020 mapping algorithm: the ordered Appendix B §2 rule list.

Recognition runs on the normalised server message ahead of the D-M1b-4 status table
(D-M1c-3). The rules and the D-M1c-4 capture descriptors are **generated** from
Appendix B (`_generated/error_catalog_data.py`); this module is only the matcher and the
capture interpreter, and it is deliberately regex-free apart from the one normalisation
suffix so the three SDKs implement the same four capture shapes rather than three regex
dialects (D-M1c-14 item 4).

Recognition is private. It implements ERR-020, not a supported extension point, and
making it public would freeze the rule representation (D-M1c-7).
"""

from __future__ import annotations

import re
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any, Final

from ._generated.error_catalog_data import (
    CAPTURES,
    RULES,
    DetailsCaptureRow,
    RecognitionRow,
)

_RETRY_AFTER_SUFFIX: Final = re.compile(r"\s*\(\s*retry after\s+(\d+)\s*s\s*\)\s*$", re.IGNORECASE)
_INTEGER: Final = re.compile(r"\d+")
_SEPARATORS: Final = re.compile(r"[,\s]+")


@dataclass(frozen=True)
class Recognised:
    """The outcome of step 4: a code, plus whatever ERR-035 could capture."""

    code: str
    details: Mapping[str, Any]


def normalise(message: str) -> str:
    """D-M1c-3's matching-time normalisation.

    Trim; strip a single trailing ``.``; strip a trailing ``(retry after Ns)``;
    lower-case. The original server message is never modified -- it stays on
    `BastionVaultError.server_message` exactly as the server sent it. Lower-casing (not
    case-folding) is the inverse of the generator's own lower-casing of every rule
    literal.
    """

    text = message.strip()
    if text.endswith("."):
        text = text[:-1]
    text = _RETRY_AFTER_SUFFIX.sub("", text)
    return text.strip().lower()


def recognise(server_message: str | None, status_code: int, path: str) -> Recognised | None:
    """The first Appendix B §2 rule whose text and guards hold, else `None`.

    `None` means "fall through to the D-M1b-4 status table, unchanged" (D-M1c-3).
    """

    if not server_message:
        return None
    original = server_message.strip()
    normalised = normalise(server_message)
    if not normalised:
        return None
    for rule in RULES:
        if not _matches(rule, normalised, status_code, path):
            continue
        capture_index = rule[7]
        details: Mapping[str, Any] = (
            _capture(CAPTURES[capture_index], original) if capture_index is not None else {}
        )
        return Recognised(code=rule[6], details=details)
    return None


def _matches(rule: RecognitionRow, normalised: str, status_code: int, path: str) -> bool:
    kind, text, contains_all, status, status_class, path_contains = rule[:6]
    if path_contains is not None and path_contains.lower() not in path.lower():
        return False
    if status is not None and status_code != status:
        return False
    if status_class is not None and status_code // 100 != status_class:
        return False
    # The rule text keeps Appendix B's own spelling, including the load-bearing trailing
    # space in `machine `/`key `/`version `/`role `/`ip `; only `prefix` can observe it,
    # so `exact` and `contains` compare against the trimmed literal (D-M1c-14 item 2).
    if kind == "exact":
        text_matches = normalised == text.strip()
    elif kind == "prefix":
        text_matches = normalised.startswith(text)
    else:
        text_matches = text.strip() in normalised
    if not text_matches:
        return False
    return all(required.strip() in normalised for required in contains_all)


def _capture(capture: DetailsCaptureRow, original: str) -> dict[str, Any]:
    """Apply one D-M1c-4 capture to the original (case-preserving) server message.

    A capture that fails is **not** an error: the code is still assigned and the key is
    simply absent, because recognition must never be more fragile than the code it
    produces (D-M1c-4).
    """

    kind, keys, prefix, suffix = capture
    details: dict[str, Any] = {}
    if kind == "token_after_prefix":
        token = _token_after_prefix(original, len(prefix))
        if token is not None:
            details[keys[0]] = token
    elif kind == "retry_after_secs":
        match = _RETRY_AFTER_SUFFIX.search(_trim_trailing_stop(original))
        if match is not None:
            details[keys[0]] = int(match.group(1))
    elif kind == "two_ints":
        numbers = [int(found) for found in _INTEGER.findall(original)]
        if len(numbers) >= 2:
            details[keys[0]] = numbers[0]
            details[keys[1]] = numbers[1]
    else:
        items = _token_list_between(original, len(prefix), suffix)
        if items:
            details[keys[0]] = items
    return details


def _token_after_prefix(original: str, start: int) -> str | None:
    """The first whitespace-delimited token after the capture's prefix.

    Every D-M1c-4 capture attaches to a `prefix` rule -- the generator hard-fails if one
    ever does not -- so the prefix is always at index 0 of the trimmed message and there
    is no "prefix not found" case to branch on (D-M1c-14 item 4).
    """

    tokens = original[start:].split()
    token = _clean(tokens[0]) if tokens else ""
    return token or None


def _token_list_between(original: str, start: int, suffix: str) -> tuple[str, ...]:
    """The comma/space separated tokens between the capture's prefix and its suffix.

    A missing suffix yields an empty span and therefore no key, which D-M1c-4 explicitly
    allows -- it is not an error and needs no branch of its own.
    """

    rest = original[start:]
    end = rest.lower().find(suffix.lower())
    head = rest[: end if end >= 0 else 0]
    return tuple(cleaned for item in _SEPARATORS.split(head) if (cleaned := _clean(item)))


def _clean(value: str) -> str:
    return value.strip().strip("`\"'").rstrip(".,;:")


def _trim_trailing_stop(value: str) -> str:
    trimmed = value.strip()
    return trimmed[:-1].rstrip() if trimmed.endswith(".") else trimmed


__all__ = ["Recognised", "normalise", "recognise"]
