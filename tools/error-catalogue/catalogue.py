"""Appendix B parser and rule compiler for the generated error catalogue (D-M1c-1).

Parser contract
===============

Two sections of ``specifications/appendix-b-error-catalogue.md`` are parsed, and
nothing else.

``## 1. Codes``
    A sequence of ``### <title> (`BV-X-*`[, `BV-Y-*`]) — <note>`` headings, each
    followed by exactly one markdown table.  The table header is either
    ``| Code | Name | Message | Hint |`` (retryability comes from the section
    note, which MUST then contain ``R = no``) or
    ``| Code | Name | R | Message | Hint |`` (retryability is per row, ``yes`` or
    ``no``).  A row whose column count differs from its header, a code that is
    not ``BV-<PREFIX>-<NNN>``, a prefix with no entry in :data:`CATEGORY_TOKENS`,
    a duplicate code, a duplicate constant name and a duplicate message are all
    hard errors (D-M1c-1).

``## 2. Server-message recognition``
    A single ``| Match | Server text | Code |`` table.  Each row compiles to one
    or more ordered rules; the table's order is normative (D-M1c-3) and is
    preserved.  A row naming a code ``§1`` does not define is a hard error.

The ``Server text`` cell is read left to right over backtick-delimited literals:

* ``/`` separates alternatives of the *stem*;
* ``+`` closes the stem and opens a qualifier list that attaches to the **last**
  stem alternative only — earlier alternatives stay standalone rules.  This is
  what makes ``` `backup hmac verification failed` / `backup` + `invalid magic`/
  `unsupported version`/`corrupted` ``` compile to one plain rule plus three
  compound rules, while ``` `version ` + contains `is below
  min_decryption_version` / `not found on key` ``` compiles to two compound
  rules;
* a parenthesised group is a **status guard** when it contains only status
  tokens (``5xx``, ``500``, ``409``), a **qualifier list** when it contains
  backticked literals, and otherwise a **scope note** (``(ssh mount)``,
  ``(policy write)``) which is recorded but not enforced — see
  :data:`SCOPE_NOTES_ARE_ADVISORY`;
* a bare ``exact``/``prefix``/``contains`` word before a literal overrides the
  row's ``Match`` kind for that alternative only.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Sequence


from captures import ENFORCED_PATH_SCOPES


class CatalogueError(Exception):
    """A malformed appendix. Always fatal: the generator never warns (D-M1c-1)."""


# D-M1c-2, verbatim.
CATEGORY_TOKENS: dict[str, str] = {
    "BV-CONFIG": "Config",
    "BV-INPUT": "Input",
    "BV-TRANSPORT": "Transport",
    "BV-PROTOCOL": "Protocol",
    "BV-AUTH": "Auth",
    "BV-AUTHZ": "Authz",
    "BV-NOTFOUND": "NotFound",
    "BV-CONFLICT": "Conflict",
    "BV-RATE": "Rate",
    "BV-QUOTA": "Quota",
    "BV-SERVER": "Server",
    "BV-DISCOVERY": "Discovery",
    "BV-KV": "Kv",
    "BV-TRANSIT": "Transit",
    "BV-PKI": "Pki",
    "BV-SSH": "Ssh",
    "BV-TOTP": "Totp",
    "BV-IDENTITY": "Identity",
    "BV-RUSTION": "Rustion",
}

# 04-error-model.md "Categories and code ranges".
CATEGORIES: dict[str, str] = {
    "BV-CONFIG": "Configuration",
    "BV-INPUT": "Input",
    "BV-TRANSPORT": "Transport",
    "BV-PROTOCOL": "Protocol",
    "BV-AUTH": "Authentication",
    "BV-AUTHZ": "Authorization",
    "BV-NOTFOUND": "NotFound",
    "BV-CONFLICT": "Conflict",
    "BV-RATE": "RateLimit",
    "BV-QUOTA": "Quota",
    "BV-SERVER": "ServerState",
    "BV-DISCOVERY": "Discovery",
    "BV-KV": "Engine",
    "BV-TRANSIT": "Engine",
    "BV-PKI": "Engine",
    "BV-SSH": "Engine",
    "BV-TOTP": "Engine",
    "BV-IDENTITY": "Engine",
    "BV-RUSTION": "Engine",
}

# ERR-006's normative true-set, cross-checked against Appendix B's R column (D-M1c-8).
ERR_006_RETRYABLE: frozenset[str] = frozenset(
    {
        "BV-TRANSPORT-001",
        "BV-TRANSPORT-002",
        "BV-TRANSPORT-003",
        "BV-SERVER-002",
        "BV-SERVER-003",
        "BV-RATE-002",
        "BV-DISCOVERY-003",
    }
)

# A scope note names knowledge the logical layer does not have at M1c (which mount
# a path belongs to, which engine produced a 409). Recorded for the record, never
# compiled into a runtime guard.
SCOPE_NOTES_ARE_ADVISORY = True

CODE_RE = re.compile(r"^BV-[A-Z]+-\d{3}$")
STATUS_TOKEN_RE = re.compile(r"^(?:[1-5]xx|[1-5]\d{2})$")
RANGE_CODE_RE = re.compile(r"^(BV-[A-Z]+)-(\d{3})…(\d{3})\s+respectively$")
DETAILS_NOTE_RE = re.compile(r"^(BV-[A-Z]+-\d{3})\s*\(Details\.([a-z_]+)\)$")
SECTION_PREFIX_RE = re.compile(r"`(BV-[A-Z]+)-\*`")


@dataclass(frozen=True)
class Code:
    code: str
    name: str
    constant: str
    screaming: str
    category: str
    message: str
    hint: str
    retryable: bool

    def as_json(self) -> dict[str, object]:
        return {
            "code": self.code,
            "name": self.name,
            "constant": self.constant,
            "screaming": self.screaming,
            "category": self.category,
            "message": self.message,
            "hint": self.hint,
            "retryable": self.retryable,
        }


@dataclass(frozen=True)
class StatusGuard:
    """``(5xx)`` → ``class_=5``; ``(500)`` → ``status=500``."""

    status: int | None
    status_class: int | None

    def as_json(self) -> dict[str, object]:
        return {"status": self.status, "statusClass": self.status_class}


@dataclass(frozen=True)
class Rule:
    """One compiled recognition rule, in Appendix B §2 table order (D-M1c-3)."""

    order: int
    row: int
    kind: str  # exact | prefix | contains
    text: str  # already normalised for matching
    contains_all: tuple[str, ...]
    guard: StatusGuard | None
    scope_note: str | None
    code: str
    details_key: str | None
    path_contains: str | None

    def as_json(self) -> dict[str, object]:
        return {
            "order": self.order,
            "row": self.row,
            "kind": self.kind,
            "text": self.text,
            "containsAll": list(self.contains_all),
            "guard": self.guard.as_json() if self.guard else None,
            "scopeNote": self.scope_note,
            "pathContains": self.path_contains,
            "code": self.code,
            "detailsKey": self.details_key,
        }


@dataclass
class Catalogue:
    codes: list[Code] = field(default_factory=list)
    rules: list[Rule] = field(default_factory=list)

    def by_code(self) -> dict[str, Code]:
        return {entry.code: entry for entry in self.codes}


# --------------------------------------------------------------------------- #
# Matching-time normalisation (D-M1c-3). Applied to the server message once,
# before any rule runs, and to every rule literal at generation time so the two
# sides are comparable without runtime work.
# --------------------------------------------------------------------------- #

RETRY_AFTER_SUFFIX_RE = re.compile(r"\s*\(\s*retry after\s+\d+\s*s\s*\)\s*$", re.IGNORECASE)


def rule_literal(text: str) -> str:
    """Lower-case only.

    Appendix B authors every rule literal already trimmed and in the server's own
    spelling, and the trailing space in the five stem literals that carry one
    ("machine ", "key ", "version ", "role ", "ip ") is load-bearing: it is what
    stops ``prefix key`` from swallowing ``key_name``.  Trimming here would
    silently widen five rules, so the literal is lower-cased and otherwise passed
    through byte for byte.
    """
    return text.lower()


def normalise(message: str) -> str:
    """trim; strip a single trailing ``.``; strip ``(retry after Ns)``; lower-case."""
    text = message.strip()
    if text.endswith("."):
        text = text[:-1]
    text = RETRY_AFTER_SUFFIX_RE.sub("", text)
    return text.strip().lower()


# --------------------------------------------------------------------------- #
# Markdown plumbing
# --------------------------------------------------------------------------- #


def _split_row(line: str) -> list[str]:
    stripped = line.strip()
    if not stripped.startswith("|") or not stripped.endswith("|"):
        raise CatalogueError(f"not a table row: {line!r}")
    return [cell.strip() for cell in stripped[1:-1].split("|")]


def _is_divider(cells: Sequence[str]) -> bool:
    return all(re.fullmatch(r":?-{2,}:?", cell) for cell in cells)


def _section(text: str, heading: str, next_heading: str | None) -> list[str]:
    lines = text.splitlines()
    try:
        start = next(index for index, line in enumerate(lines) if line.strip() == heading)
    except StopIteration as exc:  # pragma: no cover - guarded by tests
        raise CatalogueError(f"heading not found: {heading!r}") from exc
    end = len(lines)
    if next_heading is not None:
        for index in range(start + 1, len(lines)):
            if lines[index].strip() == next_heading:
                end = index
                break
        else:
            raise CatalogueError(f"heading not found: {next_heading!r}")
    return lines[start + 1 : end]


# --------------------------------------------------------------------------- #
# §1 — codes
# --------------------------------------------------------------------------- #


def constant_name(prefix: str, name: str) -> str:
    """``<CategoryToken><Name>``, never doubled (D-M1c-2)."""
    token = CATEGORY_TOKENS[prefix]
    return name if name.startswith(token) else token + name


def screaming_name(constant: str) -> str:
    """The ``SCREAMING_SNAKE_CASE`` of the same identifier (D-M1c-2)."""
    with_breaks = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", constant)
    with_breaks = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", "_", with_breaks)
    return with_breaks.upper()


def parse_codes(text: str) -> list[Code]:
    lines = _section(text, "## 1. Codes", "## 2. Server-message recognition")
    codes: list[Code] = []
    seen_codes: dict[str, int] = {}
    seen_constants: dict[str, str] = {}
    seen_messages: dict[str, str] = {}

    section_prefixes: list[str] = []
    section_all_no = False
    header: list[str] | None = None

    for raw in lines:
        line = raw.rstrip()
        if line.startswith("### "):
            section_prefixes = SECTION_PREFIX_RE.findall(line)
            if not section_prefixes:
                raise CatalogueError(f"section heading names no code prefix: {line!r}")
            section_all_no = "R = no" in line
            header = None
            continue
        if not line.strip().startswith("|"):
            continue

        cells = _split_row(line)
        if header is None:
            if cells not in (["Code", "Name", "Message", "Hint"], ["Code", "Name", "R", "Message", "Hint"]):
                raise CatalogueError(f"unrecognised code-table header: {cells!r}")
            if len(cells) == 4 and not section_all_no:
                raise CatalogueError(
                    f"table without an R column must sit under a 'R = no' section heading: {cells!r}"
                )
            header = cells
            continue
        if _is_divider(cells):
            continue
        if len(cells) != len(header):
            raise CatalogueError(f"malformed row: expected {len(header)} columns, got {len(cells)}: {cells!r}")

        if len(header) == 5:
            code, name, retry_cell, message, hint = cells
            if retry_cell not in ("yes", "no"):
                raise CatalogueError(f"R column must be 'yes' or 'no', got {retry_cell!r} for {code}")
            retryable = retry_cell == "yes"
        else:
            code, name, message, hint = cells
            retryable = False

        if not CODE_RE.match(code):
            raise CatalogueError(f"malformed code: {code!r}")
        prefix = code.rsplit("-", 1)[0]
        if prefix not in CATEGORY_TOKENS:
            raise CatalogueError(f"unknown category prefix: {prefix!r}")
        if prefix not in section_prefixes:
            raise CatalogueError(f"{code} appears under a section for {section_prefixes!r}")
        if code in seen_codes:
            raise CatalogueError(f"duplicate code: {code}")
        if not re.fullmatch(r"[A-Z][A-Za-z0-9]*", name):
            raise CatalogueError(f"malformed name for {code}: {name!r}")
        if not message or not hint:
            raise CatalogueError(f"{code} has an empty message or hint (ERR-037)")

        constant = constant_name(prefix, name)
        if constant in seen_constants:
            raise CatalogueError(f"duplicate constant {constant} for {code} and {seen_constants[constant]}")
        if message in seen_messages:
            raise CatalogueError(f"duplicate message for {code} and {seen_messages[message]} (ERR-037)")

        seen_codes[code] = 1
        seen_constants[constant] = code
        seen_messages[message] = code
        codes.append(
            Code(
                code=code,
                name=name,
                constant=constant,
                screaming=screaming_name(constant),
                category=CATEGORIES[prefix],
                message=message,
                hint=hint,
                retryable=retryable,
            )
        )

    if not codes:
        raise CatalogueError("§1 defined no codes")
    return codes


# ERR-031: a hint is at most two sentences. Backticked spans are masked first (a hint may
# name `Sys.Batch` or `*-info` without that being a sentence end) and a decimal point is not
# a sentence end either. The same masking is mirrored by the .NET catalogue test so both
# sides count the same way.
MAX_HINT_SENTENCES = 2
_BACKTICKED_RE = re.compile(r"`[^`]*`")
_DECIMAL_POINT_RE = re.compile(r"(?<=\d)\.(?=\d)")
_SENTENCE_SPLIT_RE = re.compile(r"[.!?](?:\s|$)")


def sentence_count(text: str) -> int:
    masked = _DECIMAL_POINT_RE.sub("", _BACKTICKED_RE.sub("X", text))
    return len([part for part in _SENTENCE_SPLIT_RE.split(masked) if part.strip()])


def check_hint_length(codes: Sequence[Code]) -> None:
    """D-M1c-13: ERR-031's "≤ 2 sentences" is asserted here, not eyeballed.

    Appendix B carried one three-sentence hint (``BV-RATE-001``) from M0 to M1c without any
    gate seeing it. Generation now fails on the next one.
    """
    offenders = [
        (entry.code, sentence_count(entry.hint))
        for entry in codes
        if sentence_count(entry.hint) > MAX_HINT_SENTENCES
    ]
    if offenders:
        raise CatalogueError(
            "ERR-031 allows at most "
            f"{MAX_HINT_SENTENCES} sentences per hint (D-M1c-13); over budget: "
            + ", ".join(f"{code} ({count})" for code, count in offenders)
        )


def check_retryable(codes: Sequence[Code]) -> None:
    """D-M1c-8: the R column's true-set must equal ERR-006's, exactly."""
    from_column = {entry.code for entry in codes if entry.retryable}
    if from_column != ERR_006_RETRYABLE:
        missing = sorted(ERR_006_RETRYABLE - from_column)
        extra = sorted(from_column - ERR_006_RETRYABLE)
        raise CatalogueError(
            "Appendix B column R disagrees with ERR-006 (D-M1c-8): "
            f"missing={missing} unexpected={extra}"
        )


# --------------------------------------------------------------------------- #
# §2 — recognition
# --------------------------------------------------------------------------- #

_TOKEN_RE = re.compile(r"`([^`]*)`|(\()|(\))|(/)|(\+)|(,)|([^`()/+,\s]+)")


@dataclass(frozen=True)
class _Token:
    kind: str  # literal | ( | ) | / | + | , | word
    value: str


def _tokenise(cell: str) -> list[_Token]:
    tokens: list[_Token] = []
    position = 0
    for match in _TOKEN_RE.finditer(cell):
        if match.start() != position and cell[position : match.start()].strip():
            raise CatalogueError(f"unparsable text {cell[position:match.start()]!r} in {cell!r}")
        position = match.end()
        literal, lparen, rparen, slash, plus, comma, word = match.groups()
        if literal is not None:
            tokens.append(_Token("literal", literal))
        elif lparen:
            tokens.append(_Token("(", lparen))
        elif rparen:
            tokens.append(_Token(")", rparen))
        elif slash:
            tokens.append(_Token("/", slash))
        elif plus:
            tokens.append(_Token("+", plus))
        elif comma:
            tokens.append(_Token(",", comma))
        else:
            tokens.append(_Token("word", word))
    if cell[position:].strip():
        raise CatalogueError(f"unparsable trailing text {cell[position:]!r} in {cell!r}")
    return tokens


def _parse_paren(tokens: list[_Token], index: int) -> tuple[int, list[_Token]]:
    assert tokens[index].kind == "("
    depth = 0
    for end in range(index, len(tokens)):
        if tokens[end].kind == "(":
            depth += 1
        elif tokens[end].kind == ")":
            depth -= 1
            if depth == 0:
                return end + 1, tokens[index + 1 : end]
    raise CatalogueError("unbalanced parenthesis in a recognition row")


def _guard_from(words: Sequence[str]) -> StatusGuard:
    status: int | None = None
    status_class: int | None = None
    for word in words:
        if word.endswith("xx"):
            status_class = int(word[0])
        else:
            status = int(word)
    return StatusGuard(status=status, status_class=status_class)


@dataclass
class _Alternative:
    kind: str | None = None
    text: str = ""
    contains_all: list[str] = field(default_factory=list)
    guard: StatusGuard | None = None
    scope_note: str | None = None


def parse_server_text(cell: str, default_kind: str) -> list[_Alternative]:
    """Compile one ``Server text`` cell into ordered alternatives."""
    tokens = _tokenise(cell)
    alternatives: list[_Alternative] = []
    current = _Alternative(kind=default_kind)
    have_literal = False
    in_qualifier = False
    pending_kind: str | None = None

    def flush() -> None:
        nonlocal current, have_literal, in_qualifier, pending_kind
        if have_literal:
            alternatives.append(current)
        current = _Alternative(kind=default_kind)
        have_literal = False
        in_qualifier = False
        pending_kind = None

    index = 0
    while index < len(tokens):
        token = tokens[index]
        if token.kind == "literal":
            if in_qualifier:
                current.contains_all.append(token.value)
            elif have_literal:
                raise CatalogueError(f"two stem literals with no separator in {cell!r}")
            else:
                current.text = token.value
                if pending_kind:
                    current.kind = pending_kind
                have_literal = True
            index += 1
        elif token.kind == "word":
            if token.value in ("exact", "prefix", "contains"):
                if in_qualifier or have_literal:
                    # `contains` introducing a qualifier list, e.g. "+ contains `x`".
                    in_qualifier = True
                else:
                    pending_kind = token.value
            else:
                raise CatalogueError(f"unexpected word {token.value!r} in {cell!r}")
            index += 1
        elif token.kind == "+":
            if not have_literal:
                raise CatalogueError(f"'+' with no stem literal in {cell!r}")
            in_qualifier = True
            index += 1
        elif token.kind in ("/", ","):
            if in_qualifier:
                index += 1  # another alternative of the same qualifier list
            else:
                flush()
                index += 1
        elif token.kind == "(":
            index, inner = _parse_paren(tokens, index)
            literals = [item.value for item in inner if item.kind == "literal"]
            words = [item.value for item in inner if item.kind == "word"]
            if literals:
                current.contains_all.extend(literals)
                in_qualifier = True
            elif words and all(STATUS_TOKEN_RE.match(word) for word in words):
                current.guard = _guard_from(words)
            else:
                current.scope_note = " ".join(words)
        else:  # pragma: no cover - ')' is always consumed by _parse_paren
            raise CatalogueError(f"unexpected token {token!r} in {cell!r}")
    flush()

    if not alternatives:
        raise CatalogueError(f"no literal in server-text cell {cell!r}")
    return alternatives


def _expand_codes(cell: str, count: int) -> tuple[list[str], str | None]:
    match = RANGE_CODE_RE.match(cell)
    if match:
        prefix, first, last = match.group(1), int(match.group(2)), int(match.group(3))
        codes = [f"{prefix}-{number:03d}" for number in range(first, last + 1)]
        if len(codes) != count:
            raise CatalogueError(
                f"code range {cell!r} names {len(codes)} codes for {count} alternatives"
            )
        return codes, None
    note = DETAILS_NOTE_RE.match(cell)
    if note:
        return [note.group(1)] * count, note.group(2)
    if not CODE_RE.match(cell):
        raise CatalogueError(f"malformed code cell: {cell!r}")
    return [cell] * count, None


def parse_recognition(text: str, defined: set[str]) -> list[Rule]:
    lines = _section(text, "## 2. Server-message recognition", "## 3. Invariants tested by every SDK")
    rules: list[Rule] = []
    header: list[str] | None = None
    row_number = 0

    for raw in lines:
        line = raw.rstrip()
        if not line.strip().startswith("|"):
            continue
        cells = _split_row(line)
        if header is None:
            if cells != ["Match", "Server text", "Code"]:
                raise CatalogueError(f"unrecognised recognition header: {cells!r}")
            header = cells
            continue
        if _is_divider(cells):
            continue
        if len(cells) != 3:
            raise CatalogueError(f"malformed recognition row: {cells!r}")

        row_number += 1
        match_cell, text_cell, code_cell = cells
        match = re.fullmatch(r"(exact|prefix|contains)(?:\s*\(([^)]*)\))?", match_cell)
        if not match:
            raise CatalogueError(f"malformed Match cell: {match_cell!r}")
        default_kind = match.group(1)
        row_guard: StatusGuard | None = None
        row_scope: str | None = None
        if match.group(2):
            parts = [part.strip() for part in match.group(2).split(",") if part.strip()]
            status_parts = [part for part in parts if STATUS_TOKEN_RE.match(part)]
            other_parts = [part for part in parts if not STATUS_TOKEN_RE.match(part)]
            if status_parts:
                row_guard = _guard_from(status_parts)
            if other_parts:
                row_scope = " ".join(other_parts)

        alternatives = parse_server_text(text_cell, default_kind)
        codes, details_key = _expand_codes(code_cell, len(alternatives))

        for alternative, code in zip(alternatives, codes, strict=True):
            if code not in defined:
                raise CatalogueError(f"recognition row {row_number} names undefined code {code}")
            literal = rule_literal(alternative.text)
            if not literal.strip():
                raise CatalogueError(f"recognition row {row_number} has an empty literal")
            scope = alternative.scope_note or row_scope
            rules.append(
                Rule(
                    order=len(rules),
                    row=row_number,
                    kind=alternative.kind or default_kind,
                    text=literal,
                    contains_all=tuple(rule_literal(item) for item in alternative.contains_all),
                    guard=alternative.guard or row_guard,
                    scope_note=scope,
                    code=code,
                    details_key=details_key,
                    path_contains=ENFORCED_PATH_SCOPES.get(scope or ""),
                )
            )

    if not rules:
        raise CatalogueError("§2 defined no recognition rules")
    return rules


def parse(appendix_b: Path) -> Catalogue:
    text = appendix_b.read_text(encoding="utf-8")
    codes = parse_codes(text)
    check_retryable(codes)
    check_hint_length(codes)
    rules = parse_recognition(text, {entry.code for entry in codes})
    return Catalogue(codes=codes, rules=rules)
