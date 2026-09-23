"""Reads ``PublicApiSurface.txt`` to find every public operation and its canonical name.

DOC-005/DOC-006 name operations by the path a caller reaches them through from
``BastionVaultClient`` (e.g. ``Kv.V2.ReadSecret``), including the root itself
(``Client.Connect``, ``Client.Dispose``, ...) — several ``*Operations`` classes
(``AuthRoleAdminOperations``, ``CertOperations`` reached through both ``Auth.Cert`` and
elsewhere, etc.) are reachable from more than one place. ``PublicApiSurface.txt`` is the
authoritative, generated list of what is public (per the brief); this module never
re-derives "is this public" from the .cs source, only *what it is called* and *where its
body lives*, both of which the surface file cannot say.

Three member kinds on ``BastionVaultClient`` (the root) are deliberately outside this
corpus, and are excluded by construction rather than by omission:

* ``: ctor`` lines — construction is governed by CFG-005, not a per-operation DOC-005
  unit.
* ``: property`` lines whose return type is itself a facade (``*Operations``) type —
  these are not operations, they are the navigation edges the BFS below follows to build
  every other type's canonical name.
* ``: property`` lines whose return type is *not* a facade type (e.g. ``Config``,
  ``Transport``) — these are state/config accessors, not operations either. Unlike the
  facade case, these are exhaustively enumerated in ``NON_NAVIGATION_PROPERTIES`` below,
  because "not a facade" is not a syntactic fact the way "ends in ``Operations``" is: a
  future facade mount could return a type that does not happen to end in ``Operations``,
  and silently treating it as "just another accessor" would be exactly the defect this
  module exists to avoid. ``build_public_operations`` raises if it finds one that is not
  on that list (see ``_check_navigation_completeness``).

The corpus's unit is the public *method*, for every reachable type including the root —
that has always been true; the root's absence from earlier versions of this module was
the bug DOC-005/DOC-006 requirement CFG-070/CFG-071/DSC-036/DSC-046 exposed, not a
deliberate exclusion.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

NAMESPACE = "BastionVault.IntegrationSdk."

METHOD_LINE_RE = re.compile(
    r"^(?P<type>[A-Za-z0-9_.]+) : method (?P<name>[A-Za-z_][A-Za-z0-9_]*)\("
)
PROPERTY_LINE_RE = re.compile(
    r"^(?P<type>[A-Za-z0-9_.]+) : property (?P<name>[A-Za-z_][A-Za-z0-9_]*) : "
    r"(?P<return_type>[A-Za-z0-9_.<>,\[\]? ]+?)\s*\{"
)

OPERATIONS_SUFFIX = "Operations"
ROOT_TYPE = "BastionVaultClient"
ROOT_PREFIX = "Client"

# Every public property, declared on ROOT_TYPE or on a facade type reachable from it,
# whose return type does not itself end in ``Operations`` — so it is not followed as a
# navigation edge by ``build_canonical_prefixes`` — and is therefore, by elimination, a
# state/config accessor rather than an operation. Enumerated explicitly, with a reason
# each, instead of left as an implicit "everything the BFS didn't follow": see
# ``_check_navigation_completeness``, which raises if a reachable property is found that
# is neither a navigation edge nor on this list.
NON_NAVIGATION_PROPERTIES: dict[tuple[str, str], str] = {
    (ROOT_TYPE, "Config"): "the resolved client configuration snapshot, not a facade",
    (ROOT_TYPE, "InputLabel"): "the address as configured (DSC-035), not a facade",
    (ROOT_TYPE, "IsInsecure"): "a TLS posture flag (CFG-018), not a facade",
    (ROOT_TYPE, "Namespace"): "the active namespace string, not a facade",
    (ROOT_TYPE, "RateGateState"): "a client-side rate-gate state snapshot, not a facade",
    (ROOT_TYPE, "SelectedNode"): "the pinned discovery node (DSC-035), not a facade",
    (ROOT_TYPE, "Transport"): "the transport handle (OVR-001), not a facade",
    ("AuthOperations", "CurrentToken"): "the current token value, not a facade",
    ("AuthOperations", "TokenInfo"): "a token metadata snapshot, not a facade",
    ("AuthOperations", "TokenSource"): "the token provenance, not a facade",
}


@dataclass(frozen=True)
class PublicOperation:
    """One public method reachable from the SDK's facade tree, including the root."""

    declaring_type: str  # short name, e.g. "KvV2Operations" or "BastionVaultClient"
    method_name: str  # as declared, e.g. "ReadSecretAsync"
    canonical_names: tuple[str, ...]  # every dotted path this method is reachable by


def _short_type(qualified: str) -> str:
    return qualified[len(NAMESPACE) :] if qualified.startswith(NAMESPACE) else qualified


def _operation_name(method_name: str) -> str:
    return method_name[: -len("Async")] if method_name.endswith("Async") else method_name


def _is_facade_type(declaring_type: str) -> bool:
    return declaring_type == ROOT_TYPE or declaring_type.endswith(OPERATIONS_SUFFIX)


def parse_public_api_surface(
    path: Path,
) -> tuple[list[tuple[str, str]], list[tuple[str, str, str]]]:
    """Return (method entries, property entries).

    ``method entries`` is a list of ``(declaring_type, method_name)`` for every
    ``: method`` line whose declaring type is ``ROOT_TYPE`` or ends in ``Operations``.

    ``property entries`` is a list of ``(declaring_type, property_name, return_type)``
    for every ``: property ... {get...}`` line with the same declaring-type filter. This
    includes both navigation edges (return type ends in ``Operations``) and
    non-navigation accessors — the caller (``build_canonical_prefixes`` /
    ``_check_navigation_completeness``) tells them apart, never this parser.
    """

    methods: list[tuple[str, str]] = []
    properties: list[tuple[str, str, str]] = []

    for line in path.read_text(encoding="utf-8").splitlines():
        method_match = METHOD_LINE_RE.match(line)
        if method_match:
            declaring_type = _short_type(method_match.group("type"))
            if _is_facade_type(declaring_type):
                methods.append((declaring_type, method_match.group("name")))
            continue

        property_match = PROPERTY_LINE_RE.match(line)
        if property_match:
            declaring_type = _short_type(property_match.group("type"))
            if _is_facade_type(declaring_type):
                return_type = _short_type(property_match.group("return_type").strip())
                properties.append((declaring_type, property_match.group("name"), return_type))

    return methods, properties


def _navigation_edges(properties: list[tuple[str, str, str]]) -> dict[str, list[tuple[str, str]]]:
    """The edges of the facade reachability graph: every property whose return type also
    ends in ``Operations``, mapped declaring type -> [(property_name, target_type)]."""

    edges: dict[str, list[tuple[str, str]]] = {}
    for declaring_type, property_name, return_type in properties:
        if return_type.endswith(OPERATIONS_SUFFIX):
            edges.setdefault(declaring_type, []).append((property_name, return_type))
    return edges


def build_canonical_prefixes(edges: dict[str, list[tuple[str, str]]]) -> dict[str, list[str]]:
    """BFS from ``BastionVaultClient`` over the navigation edges.

    Returns declaring-type -> list of dotted prefixes it is reachable by (usually one;
    more than one means the type is reachable through more than one facade path, which
    is reported rather than silently collapsed). The root itself is not a key here: its
    canonical prefix is the fixed ``ROOT_PREFIX`` ("Client"), not something the BFS
    derives, since nothing navigates *to* the root.
    """

    prefixes: dict[str, list[str]] = {}
    # Seed with the root's own children; each child's prefix is just its property name.
    queue: list[tuple[str, str]] = [
        (target_type, property_name) for property_name, target_type in edges.get(ROOT_TYPE, [])
    ]

    visited_prefix_pairs: set[tuple[str, str]] = set()
    while queue:
        current_type, current_prefix = queue.pop(0)
        pair = (current_type, current_prefix)
        if pair in visited_prefix_pairs:
            continue
        visited_prefix_pairs.add(pair)
        prefixes.setdefault(current_type, [])
        if current_prefix not in prefixes[current_type]:
            prefixes[current_type].append(current_prefix)
        for property_name, target_type in edges.get(current_type, []):
            next_prefix = f"{current_prefix}.{property_name}"
            queue.append((target_type, next_prefix))

    return prefixes


def _check_navigation_completeness(
    properties: list[tuple[str, str, str]],
    prefixes: dict[str, list[str]],
) -> None:
    """Tripwire (requirement 2): every public property declared on ``ROOT_TYPE`` or on a
    type reachable from it MUST be either a navigation edge (return type ends in
    ``Operations``) or explicitly listed in ``NON_NAVIGATION_PROPERTIES``. A property
    that is neither means a future facade mount (or new state accessor) landed and this
    module was not updated to match — that must fail loudly, not be silently dropped from
    the corpus the way the pre-fix implicit exclusion did.
    """

    reachable_types = set(prefixes.keys()) | {ROOT_TYPE}
    for declaring_type, property_name, return_type in properties:
        if declaring_type not in reachable_types:
            continue
        if return_type.endswith(OPERATIONS_SUFFIX):
            continue  # a navigation edge, followed by the BFS
        if (declaring_type, property_name) in NON_NAVIGATION_PROPERTIES:
            continue
        raise ValueError(
            f"{declaring_type}.{property_name} : {return_type} is a public property on a "
            "facade type reachable from BastionVaultClient. It is not followed as a "
            "navigation edge (its return type does not end in 'Operations') and it is not "
            "listed in apisurface.NON_NAVIGATION_PROPERTIES. Add it to that constant with "
            "a one-line reason if it is genuinely a state/config accessor, or add the "
            "missing navigation edge if it is in fact a new facade mount."
        )


def build_public_operations(surface_path: Path) -> list[PublicOperation]:
    methods, properties = parse_public_api_surface(surface_path)
    edges = _navigation_edges(properties)
    prefixes = build_canonical_prefixes(edges)
    _check_navigation_completeness(properties, prefixes)

    operations: list[PublicOperation] = []
    for declaring_type, method_name in methods:
        if declaring_type == ROOT_TYPE:
            canonical_names: tuple[str, ...] = (f"{ROOT_PREFIX}.{_operation_name(method_name)}",)
        else:
            type_prefixes = prefixes.get(declaring_type)
            if not type_prefixes:
                # Unreachable from BastionVaultClient by a navigation property chain: still
                # a public method (e.g. a static helper reached only via
                # `new KvV2Operations(...)` would not occur here since these are all
                # internal constructors, but a type this generator's reachability BFS
                # missed is reported, not silently dropped).
                canonical_names = ()
            else:
                canonical_names = tuple(
                    f"{prefix}.{_operation_name(method_name)}" for prefix in sorted(type_prefixes)
                )
        operations.append(PublicOperation(declaring_type, method_name, canonical_names))
    return operations
