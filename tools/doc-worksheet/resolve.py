"""Resolves a public operation's HTTP verb and path template from its C# source.

This is the one place HTTP semantics are interpreted (``cs_model`` only answers "what
does the text say"). The governing rule, per the brief and R-23's recorded incident:
**never guess**. Every branch below either produces a template built entirely from
literal text and caller-supplied parameter names found in the source, or it returns
``None`` with a plain-language reason. There is no third outcome.

Call-site discovery
====================

An operation may not call the wire executor directly; it may delegate to a private
helper, or to another public operation, which itself calls the executor. So resolution
works in two passes:

1. ``find_execute_sites`` walks the *entire* body text of the operation (and, through
   any call it recognises, the entire body text of whatever it calls, recursively,
   depth-bounded) looking for a call to one of the four ``Execute*Async`` primitives
   (``ExecuteShapedAsync``, ``ExecuteBinaryAsync``, ``ExecuteRawAsync``,
   ``ExecuteHealthAsync``) that this SDK's ``LogicalOperations``/``RequestExecutor``
   layer defines. It deliberately does not stop at the first *textual* statement level —
   a call reachable only from inside a ``try``/``catch`` (e.g.
   ``KvV2Operations.SecretExistsAsync``) still counts, because it still happens at
   runtime.
2. Each site's first two arguments (verb, path) are evaluated against the scope built up
   to that call (its own parameters, bound from the caller's already-resolved arguments,
   plus any local variables the callee assigns before using them).

Zero sites means "no HTTP call" (a real, checkable fact for the client-side path
helpers). Exactly one site is the normal case. More than one *distinct* site is reported
as-is — DOC-005 is a single verb/path per operation, so an operation whose C# reaches the
wire in more than one way is exactly the case f1 flagged for ``Kv.V2.GetSecret`` and gets
the same honest non-answer here, not an arbitrary pick.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

from cs_model import ClassInfo, MethodInfo, find_top_level_char

EXECUTE_NAME_RE = re.compile(r"^Execute[A-Za-z]*Async$")
CALL_RE = re.compile(r"(?:(?P<receiver>[A-Za-z_][A-Za-z0-9_]*)\.)?(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(")
# Wrapper calls that do not change the *identity* of the value they wrap, only its
# encoding, for templating purposes: `x.Trim('/')` and `UrlBuilder.EncodePathFragment(x)`
# both still mean "the caller's `x`". Split by calling convention: an instance-form
# wrapper (`receiver.Method(...)`) is transparent on its receiver; a static-form one
# (`Type.Method(firstArg, ...)`) is transparent on its first argument.
INSTANCE_TRANSPARENT_WRAPPERS = {"Trim", "ToString", "ToLowerInvariant", "ToUpperInvariant"}
STATIC_TRANSPARENT_WRAPPERS = {
    "EncodePathFragment",
    "EncodePathSegment",
    "EncodeQueryValue",
    "EncodeQueryComponent",
    "EncodeQuerySegment",
}
MAX_DEPTH = 14
MAX_SITES = 6  # a runaway call graph is itself a reason to stop, not to keep expanding


@dataclass(frozen=True)
class Segment:
    kind: str  # "lit" or "param"
    text: str


Resolved = tuple[list[Segment] | None, str | None]  # (segments, reason-if-unresolved)


def render_template(segments: list[Segment]) -> str:
    parts = []
    for segment in segments:
        parts.append(f"{{{segment.text}}}" if segment.kind == "param" else segment.text)
    return "".join(parts)


@dataclass
class ExecuteSite:
    execute_method: str
    verb: Resolved
    path: Resolved
    # A call reached only from inside a `catch` block is error-path enrichment (e.g.
    # `KvV1Operations.ReadAsync`'s 404 handler telling the caller "this looks like a v2
    # mount"), not a second normal way to reach the wire — DOC-005 asks for *the* HTTP
    # call an operation performs, and that reading is what a site count should reflect.
    secondary: bool = False


@dataclass
class RouteResolution:
    verb: str | None
    verb_reason: str | None
    path_template: str | None
    path_reason: str | None
    execute_method: str | None
    call_site_count: int
    reason: str | None  # set when call_site_count != 1


def _unescape(raw: str) -> str:
    return raw.encode("utf-8").decode("unicode_escape") if "\\" in raw else raw


def _strip_outer_parens(expr: str) -> str:
    """Strip one layer of parens wrapping the *whole* expression, e.g. ``(a + b)``."""

    while expr.startswith("(") and expr.endswith(")"):
        depth = 0
        balanced_to_end = True
        for index, char in enumerate(expr[1:-1]):
            if char in "([{":
                depth += 1
            elif char in ")]}":
                depth -= 1
                if depth < 0:
                    balanced_to_end = False
                    break
        if balanced_to_end and depth == 0:
            expr = expr[1:-1].strip()
            continue
        break
    return expr


def _split_top_level(expr: str, operator: str) -> list[str]:
    """Split on a single-character operator at bracket/quote depth 0."""

    parts: list[str] = []
    depth = 0
    current = ""
    index = 0
    length = len(expr)
    while index < length:
        char = expr[index]
        if char == '"':
            end = index + 1
            while end < length and expr[end] != '"':
                end += 2 if expr[end] == "\\" else 1
            current += expr[index : end + 1]
            index = end + 1
            continue
        if char == "'":
            end = index + 1
            while end < length and expr[end] != "'":
                end += 2 if expr[end] == "\\" else 1
            current += expr[index : end + 1]
            index = end + 1
            continue
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        if char == operator and depth == 0:
            parts.append(current)
            current = ""
            index += 1
            continue
        current += char
        index += 1
    parts.append(current)
    return parts


def _split_null_coalesce(expr: str) -> tuple[str, str] | None:
    """Split ``a ?? b`` at the top-level ``??``, or return ``None``."""

    depth = 0
    index = 0
    length = len(expr)
    while index < length - 1:
        char = expr[index]
        if char == '"':
            index += 1
            while index < length and expr[index] != '"':
                index += 2 if expr[index] == "\\" else 1
            index += 1
            continue
        if char == "'":
            index += 1
            while index < length and expr[index] != "'":
                index += 2 if expr[index] == "\\" else 1
            index += 1
            continue
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        elif char == "?" and expr[index + 1] == "?" and depth == 0:
            return expr[:index].strip(), expr[index + 2 :].strip()
        index += 1
    return None


def _find_ternary_parts(expr: str) -> tuple[str, str, str] | None:
    """Split ``cond ? a : b`` at the top-level ``?``/``:``, or return ``None``.

    Excludes ``?.`` (null-conditional) and ``??`` (null-coalescing), neither of which is
    a three-part ternary.
    """

    depth = 0
    index = 0
    length = len(expr)
    question_index = None
    while index < length:
        char = expr[index]
        if char == '"':
            index += 1
            while index < length and expr[index] != '"':
                index += 2 if expr[index] == "\\" else 1
            index += 1
            continue
        if char == "'":
            index += 1
            while index < length and expr[index] != "'":
                index += 2 if expr[index] == "\\" else 1
            index += 1
            continue
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        elif char == "?" and depth == 0:
            following = expr[index + 1 : index + 2]
            if following not in (".", "?"):
                question_index = index
                break
        index += 1
    if question_index is None:
        return None
    colon_index = find_top_level_char(expr, question_index + 1, ":")
    if colon_index == -1:
        return None
    return (
        expr[:question_index].strip(),
        expr[question_index + 1 : colon_index].strip(),
        expr[colon_index + 1 :].strip(),
    )


_COND_LENGTH_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\.Length\s*(==|!=)\s*0$")
_COND_IS_NULL_OR_EMPTY_RE = re.compile(r"^string\.IsNullOrEmpty\(\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)$")


def _fold_condition(
    condition: str, scope: dict[str, Resolved], class_index: dict[str, list[ClassInfo]], class_name: str, depth: int
) -> bool | None:
    """Decide a narrow set of conditions statically decidable from a literal operand.

    Only the idiom this SDK actually uses to make an optional route segment
    conditional — ``group.Length == 0`` / ``!= 0`` and ``string.IsNullOrEmpty(x)`` over
    an identifier that resolves to an all-literal value (a route "group" constant is
    always one) — is folded. Anything else is left undecided rather than guessed.
    """

    length_match = _COND_LENGTH_RE.match(condition)
    empty_match = _COND_IS_NULL_OR_EMPTY_RE.match(condition) if not length_match else None
    identifier = length_match.group(1) if length_match else (empty_match.group(1) if empty_match else None)
    if identifier is None:
        return None
    segments, _ = evaluate_expr(identifier, scope, class_index, class_name, depth + 1)
    if segments is None or not all(segment.kind == "lit" for segment in segments):
        return None
    literal_value = "".join(segment.text for segment in segments)
    is_empty = len(literal_value) == 0
    if length_match:
        return is_empty if length_match.group(2) == "==" else not is_empty
    return is_empty


def _match_call(expr: str) -> tuple[str | None, str, str] | None:
    match = re.match(r"^(?P<chain>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*\(", expr)
    if not match:
        return None
    open_paren = match.end() - 1
    close_paren = _scan_paren(expr, open_paren)
    if close_paren != len(expr) - 1:
        return None
    args_text = expr[open_paren + 1 : close_paren]
    chain = match.group("chain")
    if "." in chain:
        receiver, _, name = chain.rpartition(".")
    else:
        receiver, name = None, chain
    return receiver, name, args_text


def _scan_paren(text: str, start: int) -> int:
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
        if char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return index
        index += 1
    return length - 1


def _first_top_level_arg(args_text: str) -> str | None:
    parts = _split_top_level(args_text, ",")
    return parts[0].strip() if parts and parts[0].strip() else None


def _lookup_const(
    class_index: dict[str, list[ClassInfo]], class_name: str, const_name: str, depth: int
) -> Resolved | None:
    """Resolve a ``const string`` by evaluating its (possibly non-literal) RHS text.

    Returns ``None`` when no const of that name exists on the class at all — distinct
    from a ``Resolved`` pair, where a ``None`` segment list means "found, but could not
    be evaluated", so a caller can tell "not a const" from "an unresolvable const".
    """

    for class_info in class_index.get(class_name, []):
        if const_name in class_info.consts:
            return evaluate_expr(class_info.consts[const_name], {}, class_index, class_name, depth + 1)
    return None


def _field_type(class_index: dict[str, list[ClassInfo]], class_name: str, field_name: str) -> str | None:
    """The declared type of a ``private readonly`` field on ``class_name``, if any."""

    for class_info in class_index.get(class_name, []):
        declared = class_info.fields.get(field_name)
        if declared is not None and declared in class_index:
            return declared
    return None


def _lookup_method(
    class_index: dict[str, list[ClassInfo]], class_name: str, method_name: str, arg_count: int
) -> MethodInfo | None:
    candidates: list[MethodInfo] = []
    for class_info in class_index.get(class_name, []):
        candidates.extend(class_info.methods.get(method_name, []))
    if not candidates:
        return None
    for candidate in candidates:
        if len(candidate.params) == arg_count:
            return candidate
    return candidates[0]


def _iter_top_level_statements(body: str) -> list[str]:
    statements: list[str] = []
    cursor = 0
    length = len(body)
    while cursor < length:
        while cursor < length and body[cursor] in " \t\r\n":
            cursor += 1
        if cursor >= length:
            break
        if body[cursor] == "{":
            close = _scan_brace(body, cursor)
            cursor = close + 1
            continue
        semi = find_top_level_char(body, cursor, ";")
        brace = find_top_level_char(body, cursor, "{")
        if brace != -1 and (semi == -1 or brace < semi):
            close = _scan_brace(body, brace)
            cursor = close + 1
            continue
        if semi == -1:
            statements.append(body[cursor:])
            break
        statements.append(body[cursor:semi])
        cursor = semi + 1
    return statements


def _scan_brace(text: str, start: int) -> int:
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
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return index
        index += 1
    return length - 1


def _single_return_expr(body: str) -> tuple[str | None, str | None]:
    returns = [statement.strip() for statement in _iter_top_level_statements(body) if statement.strip().startswith("return ")]
    if not returns:
        return None, "no single top-level return statement (may be inside try/catch or a loop)"
    if len(returns) > 1:
        return None, f"{len(returns)} top-level return statements (branching return)"
    return returns[0][len("return ") :].strip(), None


def local_scope_from_body(
    body: str, base_scope: dict[str, Resolved], class_index: dict[str, list[ClassInfo]], class_name: str, depth: int
) -> dict[str, Resolved]:
    scope = dict(base_scope)
    for statement in _iter_top_level_statements(body):
        statement = statement.strip()
        if not statement or statement.startswith("return") or statement.startswith("//"):
            continue
        equals = _split_top_level(statement, "=")
        if len(equals) != 2:
            continue
        lhs, rhs = equals[0].strip(), equals[1].strip()
        if not rhs or lhs.endswith(("<", ">", "!", "=")) or rhs.startswith("="):
            continue  # `==`, `<=`, `>=`, `!=` all split here too; not an assignment
        if "." in lhs or "[" in lhs:
            continue  # property/indexer assignment, not a local
        tokens = lhs.split()
        name = tokens[-1] if tokens else ""
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
            continue
        scope[name] = evaluate_expr(rhs, scope, class_index, class_name, depth)
    return scope


def _bind_args(
    params: list[str],
    args_text: str,
    caller_scope: dict[str, Resolved],
    class_index: dict[str, list[ClassInfo]],
    caller_class: str,
    depth: int,
) -> dict[str, Resolved]:
    arg_exprs = [part.strip() for part in _split_top_level(args_text, ",") if part.strip()]
    bound: dict[str, Resolved] = {}
    for index, param_name in enumerate(params):
        if index >= len(arg_exprs):
            break
        arg_expr = arg_exprs[index]
        # Named arguments (`cancellationToken: cancellationToken`) — take the value side.
        named = re.match(r"^[A-Za-z_][A-Za-z0-9_]*\s*:\s*(?!:)(.+)$", arg_expr)
        if named:
            arg_expr = named.group(1).strip()
        bound[param_name] = evaluate_expr(arg_expr, caller_scope, class_index, caller_class, depth + 1)
    return bound


def evaluate_expr(
    expr: str, scope: dict[str, Resolved], class_index: dict[str, list[ClassInfo]], class_name: str, depth: int
) -> Resolved:
    expr = expr.strip()
    if not expr:
        return None, "empty expression"
    if depth > MAX_DEPTH:
        return None, "recursion depth exceeded while resolving the path expression"

    expr = _strip_outer_parens(expr)

    coalesce = _split_null_coalesce(expr)
    if coalesce is not None:
        left, right = coalesce
        left_resolved = evaluate_expr(left, scope, class_index, class_name, depth + 1)
        # `A ?? B`: describe the segment by whichever side actually resolves. This names
        # the caller-meaningful value (usually `A`, a parameter) the same way this SDK's
        # own hand-authored doc comments already describe an optional-with-default
        # argument — it is not a claim about which literal server-side route is taken.
        if left_resolved[0] is not None:
            return left_resolved
        return evaluate_expr(right, scope, class_index, class_name, depth + 1)

    ternary = _find_ternary_parts(expr)
    if ternary is not None:
        condition, then_expr, else_expr = ternary
        folded = _fold_condition(condition, scope, class_index, class_name, depth)
        if folded is True:
            return evaluate_expr(then_expr, scope, class_index, class_name, depth + 1)
        if folded is False:
            return evaluate_expr(else_expr, scope, class_index, class_name, depth + 1)
        return None, f"conditional path expression, condition not statically decidable: `{condition}`"

    concat_parts = _split_top_level(expr, "+")
    if len(concat_parts) > 1:
        segments: list[Segment] = []
        for part in concat_parts:
            sub, reason = evaluate_expr(part, scope, class_index, class_name, depth + 1)
            if sub is None:
                return None, reason
            segments.extend(sub)
        return segments, None

    if re.fullmatch(r'"(?:[^"\\]|\\.)*"', expr, re.DOTALL):
        return [Segment("lit", _unescape(expr[1:-1]))], None

    interpolated_body = _interpolated_string_body(expr)
    if interpolated_body is not None:
        return _resolve_interpolated(interpolated_body, scope, class_index, class_name, depth)

    if expr in ("string.Empty", "String.Empty"):
        return [Segment("lit", "")], None

    if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", expr):
        if expr in scope:
            return scope[expr]
        const_resolved = _lookup_const(class_index, class_name, expr, depth)
        if const_resolved is not None:
            return const_resolved
        return None, f"unresolved identifier `{expr}` (not a known parameter, local, or const)"

    qualified = re.fullmatch(r"([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)", expr)
    if qualified is not None:
        type_name, member_name = qualified.groups()
        if type_name in class_index:
            const_resolved = _lookup_const(class_index, type_name, member_name, depth)
            if const_resolved is not None:
                return const_resolved
        return None, f"unresolved qualified reference `{expr}`"

    call = _match_call(expr)
    if call is not None:
        receiver, name, args_text = call
        if name in INSTANCE_TRANSPARENT_WRAPPERS and receiver is not None:
            return evaluate_expr(receiver, scope, class_index, class_name, depth + 1)
        if name in STATIC_TRANSPARENT_WRAPPERS:
            first_arg = _first_top_level_arg(args_text)
            if first_arg is not None:
                return evaluate_expr(first_arg, scope, class_index, class_name, depth + 1)
            return None, f"cannot resolve wrapper call `{expr}`"

        target_class = receiver if receiver and receiver in class_index else class_name
        arg_count = len([part for part in _split_top_level(args_text, ",") if part.strip()])
        method_info = _lookup_method(class_index, target_class, name, arg_count)
        if method_info is None:
            return None, f"call to unresolved method `{expr}`"
        return_expr, reason = _single_return_expr(method_info.body)
        if return_expr is None:
            return None, f"`{name}`: {reason}"
        callee_scope = _bind_args(method_info.params, args_text, scope, class_index, class_name, depth)
        callee_local_scope = local_scope_from_body(
            method_info.body, callee_scope, class_index, method_info.class_name, depth + 1
        )
        return evaluate_expr(return_expr, callee_local_scope, class_index, method_info.class_name, depth + 1)

    return None, f"unrecognised expression form: `{expr}`"


def _interpolated_string_body(expr: str) -> str | None:
    """Return the inner text of a whole ``$"..."`` expression, or ``None``.

    A plain ``[^"\\\\]|\\\\.`` regex over the whole string breaks the moment an
    interpolation hole contains its own string literal (e.g.
    ``$"a/{Encode(x, "label")}"``, which this SDK's wire helpers do routinely) because
    the embedded, unescaped ``"`` looks like early string termination. This scans with
    brace-depth awareness instead: a ``"`` only terminates the string when it occurs
    outside every ``{...}`` hole.
    """

    if len(expr) < 3 or not expr.startswith('$"') or not expr.endswith('"'):
        return None
    index = 2
    depth = 0
    length = len(expr)
    while index < length:
        char = expr[index]
        if depth == 0 and char == "\\":
            index += 2
            continue
        if depth == 0 and char == "{" and expr[index + 1 : index + 2] == "{":
            index += 2
            continue
        if depth == 0 and char == "}" and expr[index + 1 : index + 2] == "}":
            index += 2
            continue
        if char == "{":
            depth += 1
            index += 1
            continue
        if char == "}":
            depth -= 1
            index += 1
            continue
        if char == '"' and depth == 0:
            return expr[2:index] if index == length - 1 else None
        index += 1
    return None


def _resolve_interpolated(
    inner: str, scope: dict[str, Resolved], class_index: dict[str, list[ClassInfo]], class_name: str, depth: int
) -> Resolved:
    segments: list[Segment] = []
    index = 0
    length = len(inner)
    literal = ""
    while index < length:
        char = inner[index]
        if char == "{" and index + 1 < length and inner[index + 1] == "{":
            literal += "{"
            index += 2
            continue
        if char == "}" and index + 1 < length and inner[index + 1] == "}":
            literal += "}"
            index += 2
            continue
        if char == "{":
            if literal:
                segments.append(Segment("lit", literal))
                literal = ""
            close = _scan_brace_hole(inner, index)
            hole_text = inner[index + 1 : close]
            # Format specifiers (`{value:format}`) and alignment (`{value,-8}`) are cut at
            # the first top-level `:` or `,`, matching what a caller would actually pass.
            colon = find_top_level_char(hole_text, 0, ":")
            comma = find_top_level_char(hole_text, 0, ",")
            cut_points = [point for point in (colon, comma) if point != -1]
            if cut_points:
                hole_text = hole_text[: min(cut_points)]
            resolved, reason = evaluate_expr(hole_text, scope, class_index, class_name, depth + 1)
            if resolved is None:
                return None, reason
            segments.extend(resolved)
            index = close + 1
            continue
        literal += char
        index += 1
    if literal:
        segments.append(Segment("lit", literal))
    return segments, None


def _scan_brace_hole(text: str, start: int) -> int:
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
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return index
        index += 1
    return length - 1


CATCH_HEADER_RE = re.compile(r"\bcatch\b[^{]*\{")


def _catch_block_ranges(body: str) -> list[tuple[int, int]]:
    """Byte ranges of every ``catch (...) { ... }`` block's body in ``body``."""

    ranges: list[tuple[int, int]] = []
    for match in CATCH_HEADER_RE.finditer(body):
        open_brace = match.end() - 1
        close_brace = _scan_brace(body, open_brace)
        ranges.append((open_brace, close_brace))
    return ranges


def find_execute_sites(
    body: str,
    scope: dict[str, Resolved],
    class_index: dict[str, list[ClassInfo]],
    class_name: str,
    depth: int,
    visiting: frozenset[tuple[str, str]] = frozenset(),
    inherited_secondary: bool = False,
) -> list[ExecuteSite]:
    if depth > MAX_DEPTH:
        return []
    local_scope = local_scope_from_body(body, scope, class_index, class_name, depth)
    catch_ranges = _catch_block_ranges(body)
    sites: list[ExecuteSite] = []
    for match in CALL_RE.finditer(body):
        if len(sites) >= MAX_SITES:
            break
        receiver = match.group("receiver")
        name = match.group("name")
        open_paren = match.end() - 1
        close_paren = _scan_paren(body, open_paren)
        args_text = body[open_paren + 1 : close_paren]
        is_secondary = inherited_secondary or any(start <= match.start() < end for start, end in catch_ranges)

        if EXECUTE_NAME_RE.match(name):
            arg_exprs = [part.strip() for part in _split_top_level(args_text, ",") if part.strip()]
            verb_expr = arg_exprs[0] if len(arg_exprs) > 0 else None
            path_expr = arg_exprs[1] if len(arg_exprs) > 1 else None
            verb = (
                evaluate_expr(verb_expr, local_scope, class_index, class_name, depth + 1)
                if verb_expr
                else (None, "no verb argument found")
            )
            path = (
                evaluate_expr(path_expr, local_scope, class_index, class_name, depth + 1)
                if path_expr
                else (None, "no path argument found")
            )
            sites.append(ExecuteSite(execute_method=name, verb=verb, path=path, secondary=is_secondary))
            continue

        if receiver is None:
            target_class = class_name
        elif receiver in class_index:
            target_class = receiver  # a static call, `TypeName.Method(...)`
        else:
            target_class = _field_type(class_index, class_name, receiver)
        if target_class is not None:
            arg_count = len([part for part in _split_top_level(args_text, ",") if part.strip()])
            # Keyed by arg count too: an overloaded method (e.g. `LoginRunner.LoginAsync`,
            # 4-arg and 5-arg) is two different callees, and treating them as one would
            # falsely trip the recursion guard on the second, legitimate, call.
            key = (target_class, name, arg_count)
            if key in visiting:
                continue
            method_info = _lookup_method(class_index, target_class, name, arg_count)
            if method_info is None:
                continue
            callee_scope = _bind_args(method_info.params, args_text, local_scope, class_index, class_name, depth)
            sites.extend(
                find_execute_sites(
                    method_info.body,
                    callee_scope,
                    class_index,
                    method_info.class_name,
                    depth + 1,
                    visiting | {key},
                    inherited_secondary=is_secondary,
                )
            )
    return sites


def _site_signature(site: ExecuteSite) -> tuple[str, str | None, str | None]:
    verb_text = render_template(site.verb[0]) if site.verb[0] is not None else None
    path_text = render_template(site.path[0]) if site.path[0] is not None else None
    return site.execute_method, verb_text, path_text


def resolve_route(operation: MethodInfo, class_index: dict[str, list[ClassInfo]]) -> RouteResolution:
    """Resolve one public operation's HTTP verb and path template."""

    initial_scope: dict[str, Resolved] = {name: ([Segment("param", name)], None) for name in operation.params}
    all_sites = find_execute_sites(operation.body, initial_scope, class_index, operation.class_name, 0)

    # Prefer the call(s) reachable outside any `catch` block: an error-path enrichment
    # call (e.g. `KvV1Operations.ReadAsync`'s 404 handler probing whether the mount is
    # actually a v2 mount) is not a second normal route to the wire, and folding it into
    # the ambiguity count would make an otherwise single-call operation misreport as
    # multi-call. If every site is inside a `catch`, there is no primary call to prefer,
    # so all sites stand.
    primary_sites = [site for site in all_sites if not site.secondary]
    sites = primary_sites or all_sites

    if not sites:
        return RouteResolution(
            verb=None,
            verb_reason="no HTTP call found (client-side helper, or a call this generator's scanner does not model)",
            path_template=None,
            path_reason="no HTTP call found (client-side helper, or a call this generator's scanner does not model)",
            execute_method=None,
            call_site_count=0,
            reason=None,
        )

    distinct = {_site_signature(site) for site in sites}
    if len(distinct) > 1:
        descriptions = [
            f"{site.execute_method}({render_template(site.verb[0]) if site.verb[0] else '?'}, "
            f"{render_template(site.path[0]) if site.path[0] else ('<' + (site.path[1] or 'unresolved') + '>')})"
            for site in sites
        ]
        return RouteResolution(
            verb=None,
            verb_reason=f"{len(distinct)} distinct call sites reached: {'; '.join(descriptions)}",
            path_template=None,
            path_reason=f"{len(distinct)} distinct call sites reached: {'; '.join(descriptions)}",
            execute_method=None,
            call_site_count=len(sites),
            reason="multiple distinct HTTP calls",
        )

    site = sites[0]
    verb_segments, verb_reason = site.verb
    path_segments, path_reason = site.path

    verb_text: str | None = None
    if verb_segments is not None:
        if all(segment.kind == "lit" for segment in verb_segments):
            verb_text = render_template(verb_segments)
        elif len(verb_segments) == 1 and verb_segments[0].kind == "param":
            verb_reason = f"caller-supplied HTTP method (parameter `{verb_segments[0].text}`)"
        else:
            verb_reason = f"verb is not a single literal or parameter: `{render_template(verb_segments)}`"

    path_text = render_template(path_segments) if path_segments is not None else None

    return RouteResolution(
        verb=verb_text,
        verb_reason=None if verb_text else verb_reason,
        path_template=path_text,
        path_reason=None if path_text else path_reason,
        execute_method=site.execute_method,
        call_site_count=1,
        reason=None,
    )
