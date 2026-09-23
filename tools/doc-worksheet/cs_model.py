"""Lightweight static model of the .NET SDK's C# source (D-M11-20 worksheet generator).

This is a text-based scanner, not a Roslyn/AST parser: the repository has no C# tooling
dependency available to this Python tool, and the source style is disciplined enough
(one statement per line, Allman braces, no macros, no verbatim strings on the paths this
tool cares about) that a brace/paren-balancing scan resolves every case this generator
needs. Where that assumption would silently produce a wrong answer, the caller is
expected to report "unresolved" rather than guess (R-23) — see ``resolve.py``, which is
the only place HTTP semantics are interpreted.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

CLASS_HEADER_RE = re.compile(
    r"^[ \t]*"
    r"(?:(?:public|internal|private|protected|sealed|static|abstract|partial|readonly)\s+)*"
    r"(?:class|record|struct)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)",
    re.MULTILINE,
)

MEMBER_RE = re.compile(
    r"^[ \t]*"
    r"(?:(?:public|internal|private|protected|static|async|virtual|override|"
    r"sealed|new|extern|unsafe|readonly|const)\s+){1,}"
    r"(?P<returns>[A-Za-z_][A-Za-z0-9_<>.,?\[\]\s]*?)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*"
    r"(?P<rest>\(|=>|\{|;)",
    re.MULTILINE,
)

CONST_RE = re.compile(
    r"^\s*(?:public|internal|private|protected)?\s*(?:static\s+)?(?:readonly\s+)?const\s+"
    r"string\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?P<expr>[^;]+);",
    re.MULTILINE,
)

# `private readonly LogicalOperations logical;`-style fields with no initializer (the
# constructor assigns them). This is how ``resolve.py`` learns that a call through
# `logical.Foo(...)` or `runner.Foo(...)` means "look up `Foo` on `LogicalOperations` /
# `LoginRunner`" instead of hard-coding the handful of names this SDK happens to use.
FIELD_RE = re.compile(
    r"^\s*(?:public\s+|internal\s+|private\s+|protected\s+)*(?:static\s+)?readonly\s+"
    r"(?P<type>[A-Za-z_][A-Za-z0-9_]*)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*;\s*$",
    re.MULTILINE,
)

DOC_LINE_RE = re.compile(r"^\s*///\s?(.*)$")

_CONTROL_KEYWORDS = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return", "get", "set"}


@dataclass
class MethodInfo:
    """One method or property-with-body declaration."""

    class_name: str
    name: str
    is_public: bool
    params: list[str]  # bare parameter names, in declared order
    body: str  # raw source text of the body (excluding braces), or the `=>` expression
    doc_comment: str  # the /// block immediately preceding, joined with newlines
    file: str
    line: int  # 1-based line of the declaration
    kind: str  # "method" or "property"


@dataclass
class ClassInfo:
    name: str
    file: str
    consts: dict[str, str]  # const name -> its raw (unevaluated) RHS expression text
    fields: dict[str, str] = field(default_factory=dict)  # field name -> declared type
    methods: dict[str, list[MethodInfo]] = field(default_factory=dict)


def _relative(path: Path, root: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def _scan_balanced(text: str, start: int, open_char: str, close_char: str) -> int:
    """``start`` points at ``open_char``. Return the index of its match.

    Skips string literals (``"..."``), char literals (``'...'``), and ``//``/``/* */``
    comments. Does not special-case C# verbatim (``@"..."``) or interpolated
    (``$"..."``) strings beyond their leading ``"`` — acceptable here because no path
    this tool resolves needs a brace/paren balanced *inside* such a string on the paths
    this generator walks (interpolation holes are handled separately in ``resolve.py``).
    """

    depth = 0
    index = start
    length = len(text)
    while index < length:
        char = text[index]
        if char == '"':
            index += 1
            while index < length and text[index] != '"':
                index += 2 if text[index] == "\\" else 1
            index += 1
            continue
        if char == "'":
            index += 1
            while index < length and text[index] != "'":
                index += 2 if text[index] == "\\" else 1
            index += 1
            continue
        if char == "/" and index + 1 < length and text[index + 1] == "/":
            while index < length and text[index] != "\n":
                index += 1
            continue
        if char == "/" and index + 1 < length and text[index + 1] == "*":
            index += 2
            while index + 1 < length and not (text[index] == "*" and text[index + 1] == "/"):
                index += 1
            index += 2
            continue
        if char == open_char:
            depth += 1
        elif char == close_char:
            depth -= 1
            if depth == 0:
                return index
        index += 1
    return length - 1


def strip_comments(text: str) -> str:
    """Blank out ``//`` and ``/* */`` comments, replacing every comment character with a
    space (newlines kept as newlines) so the result is exactly the same length and every
    downstream offset (used by ``resolve.py``'s call-site walk: ``match.start()``,
    catch-block ranges, and so on) still lines up with the original text.

    Applied once, here, to every ``MethodInfo.body`` this module produces, so every
    consumer in ``resolve.py`` sees comment-free text without re-deriving the stripping
    itself. Without it, a comment is just more body text to the call-site walker: a
    full-line ``//`` comment sitting between an ``Execute*Async(`` call's open paren and
    its real arguments is textually indistinguishable from an argument, and a comma
    inside the comment's prose (not inside a string, so the arg-splitter cannot tell)
    splits it into a bogus first "argument" that gets reported as an unrecognised
    expression form. Skips string and char literals first, exactly as ``_scan_balanced``
    already does, so a literal containing ``//`` is never mistaken for a comment.
    """

    result: list[str] = []
    index = 0
    length = len(text)
    while index < length:
        char = text[index]
        if char == '"':
            end = index + 1
            while end < length and text[end] != '"':
                end += 2 if text[end] == "\\" else 1
            end += 1
            result.append(text[index:end])
            index = end
            continue
        if char == "'":
            end = index + 1
            while end < length and text[end] != "'":
                end += 2 if text[end] == "\\" else 1
            end += 1
            result.append(text[index:end])
            index = end
            continue
        if char == "/" and index + 1 < length and text[index + 1] == "/":
            while index < length and text[index] != "\n":
                result.append(" ")
                index += 1
            continue
        if char == "/" and index + 1 < length and text[index + 1] == "*":
            result.append("  ")
            index += 2
            while index + 1 < length and not (text[index] == "*" and text[index + 1] == "/"):
                result.append("\n" if text[index] == "\n" else " ")
                index += 1
            result.append("  ")
            index += 2
            continue
        result.append(char)
        index += 1
    return "".join(result)


def find_top_level_char(text: str, start: int, target: str) -> int:
    """Return the index of the first ``target`` char at bracket-depth 0 from ``start``.

    Returns ``-1`` when no such occurrence exists — used (unlike ``_scan_balanced``,
    whose contract assumes a match exists) wherever "not found" is itself meaningful,
    e.g. detecting the end of the last statement in a method body.
    """

    depth = 0
    index = start
    length = len(text)
    while index < length:
        char = text[index]
        if char == '"':
            index += 1
            while index < length and text[index] != '"':
                index += 2 if text[index] == "\\" else 1
            index += 1
            continue
        if char == "'":
            index += 1
            while index < length and text[index] != "'":
                index += 2 if text[index] == "\\" else 1
            index += 1
            continue
        if char == "/" and index + 1 < length and text[index + 1] == "/":
            while index < length and text[index] != "\n":
                index += 1
            continue
        if char == "/" and index + 1 < length and text[index + 1] == "*":
            index += 2
            while index + 1 < length and not (text[index] == "*" and text[index + 1] == "/"):
                index += 1
            index += 2
            continue
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        elif char == target and depth == 0:
            return index
        index += 1
    return -1


def _split_params(raw: str) -> list[str]:
    """Return bare parameter names from a C# parameter list, best effort."""

    if not raw.strip():
        return []
    parts: list[str] = []
    depth = 0
    current = ""
    for char in raw:
        if char in "<([":
            depth += 1
        elif char in ">)]":
            depth -= 1
        if char == "," and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += char
    parts.append(current)

    names: list[str] = []
    for part in parts:
        part = part.strip()
        if not part:
            continue
        part = re.sub(r"^\[[^\]]*\]\s*", "", part)  # drop [attributes]
        part = part.split("=", 1)[0].strip()  # drop default value
        tokens = part.replace("(", " ").split()
        if tokens:
            names.append(tokens[-1].rstrip(","))
    return names


def _extract_doc_comment(text: str, declaration_offset: int) -> str:
    """Walk backwards from a declaration's line start over ``///`` and ``[...]`` lines."""

    line_start = text.rfind("\n", 0, declaration_offset) + 1
    lines_above: list[str] = []
    cursor = line_start
    while True:
        previous_newline = text.rfind("\n", 0, cursor - 1) if cursor > 0 else -1
        prev_line_start = previous_newline + 1
        prev_line = text[prev_line_start : cursor - 1] if cursor > 0 else ""
        stripped = prev_line.strip()
        if stripped.startswith("["):
            cursor = prev_line_start
            continue
        if stripped.startswith("///"):
            lines_above.append(stripped)
            cursor = prev_line_start
            continue
        break
    lines_above.reverse()
    doc_lines = [DOC_LINE_RE.match(line).group(1) if DOC_LINE_RE.match(line) else "" for line in lines_above]
    return "\n".join(doc_lines)


def _line_of(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def _parse_class_body(
    class_name: str, body: str, body_offset: int, full_text: str, relative_file: str
) -> ClassInfo:
    # Raw RHS text, not a resolved value: a `const string` need not be a plain literal
    # (`Root + "/self"` is common in this SDK), so evaluating it is `resolve.py`'s job.
    consts = {match.group("name"): match.group("expr").strip() for match in CONST_RE.finditer(body)}
    fields = {match.group("name"): match.group("type") for match in FIELD_RE.finditer(body)}

    methods: dict[str, list[MethodInfo]] = {}
    cursor = 0
    length = len(body)
    while cursor < length:
        match = MEMBER_RE.search(body, cursor)
        if match is None:
            break
        name = match.group("name")
        rest = match.group("rest")
        if name in _CONTROL_KEYWORDS:
            cursor = match.end()
            continue

        declaration_offset = body_offset + match.start()
        is_public = bool(re.match(r"^[ \t]*public\b", body[match.start() :]))

        if rest == ";":
            # Field declaration, auto-property, or a body-less method (interface/abstract):
            # none of these carry a resolvable HTTP call, so skip past it.
            cursor = match.end()
            continue

        if rest == "(":
            open_paren = match.end() - 1
            close_paren = _scan_balanced(body, open_paren, "(", ")")
            params_text = body[open_paren + 1 : close_paren]
            after_index = close_paren + 1
            while after_index < length and body[after_index] in " \t\r\n":
                after_index += 1
            if after_index < length and body[after_index] == "{":
                body_start = after_index
                body_end = _scan_balanced(body, body_start, "{", "}")
                member_body = strip_comments(body[body_start + 1 : body_end])
                cursor = body_end + 1
            elif body[after_index : after_index + 2] == "=>":
                semicolon = find_top_level_char(body, after_index + 2, ";")
                if semicolon == -1:
                    semicolon = length
                member_body = strip_comments(body[after_index + 2 : semicolon])
                cursor = semicolon + 1
            elif after_index < length and body[after_index] == ";":
                member_body = ""
                cursor = after_index + 1
            else:
                # Generic constraint clause or another form this scanner does not model;
                # skip past the header rather than mis-parse it.
                cursor = match.end()
                continue

            info = MethodInfo(
                class_name=class_name,
                name=name,
                is_public=is_public,
                params=_split_params(params_text),
                body=member_body,
                doc_comment=_extract_doc_comment(full_text, declaration_offset),
                file=relative_file,
                line=_line_of(full_text, declaration_offset),
                kind="method",
            )
            methods.setdefault(name, []).append(info)
            continue

        # rest in ("=>", "{"): a property.
        if rest == "=>":
            semicolon = find_top_level_char(body, match.end(), ";")
            if semicolon == -1:
                semicolon = length
            member_body = strip_comments(body[match.end() : semicolon])
            cursor = semicolon + 1
        else:
            body_start = match.end() - 1
            body_end = _scan_balanced(body, body_start, "{", "}")
            member_body = strip_comments(body[body_start + 1 : body_end])
            cursor = body_end + 1

        info = MethodInfo(
            class_name=class_name,
            name=name,
            is_public=is_public,
            params=[],
            body=member_body,
            doc_comment=_extract_doc_comment(full_text, declaration_offset),
            file=relative_file,
            line=_line_of(full_text, declaration_offset),
            kind="property",
        )
        methods.setdefault(name, []).append(info)

    return ClassInfo(name=class_name, file=relative_file, consts=consts, fields=fields, methods=methods)


def parse_file(path: Path, root: Path) -> list[ClassInfo]:
    """Parse one .cs file into its top-level class/record/struct blocks."""

    text = path.read_text(encoding="utf-8")
    relative_file = _relative(path, root)

    classes: list[ClassInfo] = []
    cursor = 0
    while True:
        match = CLASS_HEADER_RE.search(text, cursor)
        if match is None:
            break
        brace_index = text.find("{", match.end())
        if brace_index == -1:
            cursor = match.end()
            continue
        close_index = _scan_balanced(text, brace_index, "{", "}")
        body = text[brace_index + 1 : close_index]
        classes.append(_parse_class_body(match.group("name"), body, brace_index + 1, text, relative_file))
        cursor = close_index + 1
    return classes


def build_index(root: Path, sub_paths: list[str]) -> dict[str, list[ClassInfo]]:
    """Parse every ``*.cs`` file under the given sub-paths into a name -> classes index.

    A class name may legitimately repeat (none do today in this SDK, but the shape
    tolerates it) so each name maps to a list.
    """

    index: dict[str, list[ClassInfo]] = {}
    for sub_path in sub_paths:
        base = root / sub_path
        if not base.exists():
            continue
        for path in sorted(base.rglob("*.cs")):
            for class_info in parse_file(path, root):
                index.setdefault(class_info.name, []).append(class_info)
    return index
