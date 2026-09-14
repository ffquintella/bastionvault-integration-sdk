"""Hand-authored generator input.

Everything in this module is written by a human and checked *against* Appendix B
by the generator; nothing here is inferred from prose (D-M1c-4).  Three tables
live here:

``DETAILS_CAPTURES``
    The seven ERR-035 capture rows pinned by D-M1c-4, keyed by the recognition
    stem literal exactly as Appendix B §2 spells it.  A capture that fails to
    match a message its rule matched is not an error — the code is still
    assigned and the key is simply absent (D-M1c-4) — so each capture is
    declarative rather than a regex, and each names the sample message the
    generated fixture drives.

``ALREADY_FIXTURED``
    The recognition rules the four hand-authored ``errors.recognition.*``
    fixtures on disk already cover.  Fixture ids are stable and new fixtures
    append (Appendix C, D-M1c-10), so the generator skips these rows rather than
    renaming or replacing the files.

``CAPTURE_KINDS``
    The closed set of capture shapes the three runtimes implement.  Keeping it
    closed and regex-free is what lets the same descriptor drive .NET, Rust and
    Python without three regex dialects to keep in parity.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Mapping, Sequence

CAPTURE_KINDS = ("token_after_prefix", "retry_after_secs", "two_ints", "token_list_between")


@dataclass(frozen=True)
class Capture:
    """One D-M1c-4 row.

    ``stem``      the Appendix B §2 stem literal this capture attaches to.
    ``kind``      one of :data:`CAPTURE_KINDS`.
    ``keys``      the ``Details`` keys produced, spelled as Appendix B spells
                  them (D-M1b-4c).
    ``prefix``    text the value follows (``token_after_prefix``,
                  ``token_list_between``).
    ``suffix``    text the value precedes (``token_list_between``).
    ``sample``    the server message the generated fixture sends.
    """

    stem: str
    kind: str
    keys: tuple[str, ...]
    sample: str
    prefix: str = ""
    suffix: str = ""

    def as_json(self) -> dict[str, object]:
        return {
            "stem": self.stem,
            "kind": self.kind,
            "keys": list(self.keys),
            "prefix": self.prefix,
            "suffix": self.suffix,
            "sample": self.sample,
        }


DETAILS_CAPTURES: tuple[Capture, ...] = (
    Capture(
        stem="cannot assign policy",
        kind="token_after_prefix",
        keys=("policy",),
        prefix="cannot assign policy",
        sample="Cannot assign policy app-admin: not granted to this token.",
    ),
    Capture(
        stem="account temporarily locked",
        kind="retry_after_secs",
        keys=("retry_after_secs",),
        sample="Account temporarily locked (retry after 300s).",
    ),
    Capture(
        stem="source address",
        kind="token_after_prefix",
        keys=("source_ip",),
        prefix="source address",
        sample="Source address 203.0.113.17 is unauthorized for this role.",
    ),
    Capture(
        stem="batch has",
        kind="two_ints",
        keys=("count", "max"),
        sample="Batch has 200 operations, exceeds max 128.",
    ),
    Capture(
        stem="meta key(s)",
        kind="token_list_between",
        keys=("keys",),
        prefix="meta key(s)",
        suffix="are reserved",
        sample="Meta key(s) username, spiffe_id are reserved.",
    ),
    Capture(
        stem="no policy named",
        kind="token_after_prefix",
        keys=("policy",),
        prefix="no policy named",
        sample="No policy named app-read.",
    ),
    Capture(
        stem="no such namespace",
        kind="token_after_prefix",
        keys=("namespace",),
        prefix="no such namespace",
        sample="No such namespace dti/esi.",
    ),
)


# (kind, text) of the recognition rules the four on-disk hand-authored fixtures
# already drive. `errors.recognition.missing-token-client-side` covers ERR-022,
# which is client-side and not an Appendix B §2 row, so it appears in no entry
# here.
ALREADY_FIXTURED: Mapping[tuple[str, str], str] = {
    ("exact", "permission denied"): "errors.recognition.permission-denied",
    ("exact", "logical backend path not supported"): "errors.recognition.path-not-supported",
    ("prefix", "api version mismatch"): "errors.recognition.api-version-mismatch",
}


# Appendix B §2 qualifies three rows with a scope the Match column does not carry:
# `(409, recordings)`, `(ssh mount)` and `(policy write)`. Only the first is decidable from the
# request path alone, so only it becomes an enforced guard; the other two would need the
# Sys.ListMounts cache (M4) or typed-operation knowledge (M2), exactly the state D-M1c-5 defers.
# Anything not listed here is recorded in catalogue.json as advisory and never compiled.
ENFORCED_PATH_SCOPES: Mapping[str, str] = {
    "recordings": "recordings",
}


def captures_by_stem() -> dict[str, Capture]:
    return {capture.stem: capture for capture in DETAILS_CAPTURES}


def check(rules: Sequence[object]) -> None:
    """Cross-check the hand-authored tables against the parsed appendix.

    Strict in both directions: a capture whose stem no §2 rule carries is a
    stale hand edit, and a §2 row annotated ``(Details.x)`` with no capture is an
    unimplemented ERR-035 obligation.  Either is a hard failure.
    """
    from catalogue import CatalogueError  # local import keeps this module standalone

    stems = {rule.text for rule in rules}  # type: ignore[attr-defined]
    for capture in DETAILS_CAPTURES:
        if capture.kind not in CAPTURE_KINDS:
            raise CatalogueError(f"unknown capture kind {capture.kind!r} for stem {capture.stem!r}")
        if capture.stem not in stems:
            raise CatalogueError(f"capture stem {capture.stem!r} matches no Appendix B §2 rule")
        # Every runtime reads the captured value from a fixed offset rather than searching for
        # the prefix, which is only sound while captures attach to `prefix` rules.
        kinds = {rule.kind for rule in rules if rule.text == capture.stem}  # type: ignore[attr-defined]
        if kinds != {"prefix"}:
            raise CatalogueError(
                f"capture stem {capture.stem!r} is a {sorted(kinds)} rule; D-M1c-4 captures "
                "must attach to a prefix rule so the value starts at a fixed offset"
            )
        if capture.prefix and capture.prefix != capture.stem:
            raise CatalogueError(
                f"capture prefix {capture.prefix!r} must equal the stem {capture.stem!r}"
            )

    by_stem = captures_by_stem()
    for rule in rules:
        key = rule.details_key  # type: ignore[attr-defined]
        if key is None:
            continue
        capture = by_stem.get(rule.text)  # type: ignore[attr-defined]
        if capture is None:
            raise CatalogueError(
                f"Appendix B §2 annotates {rule.text!r} with Details.{key} but D-M1c-4 pins no capture"
            )
        if key not in capture.keys:
            raise CatalogueError(
                f"Appendix B §2 annotates {rule.text!r} with Details.{key}, "
                f"D-M1c-4 pins {capture.keys}"
            )

    for key in ALREADY_FIXTURED:
        if key[1] not in stems:
            raise CatalogueError(f"ALREADY_FIXTURED names {key!r}, which is not an Appendix B §2 rule")
