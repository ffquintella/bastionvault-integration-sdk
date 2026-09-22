"""Reads ``PublicApiSurface.txt`` to find every public operation and its canonical name.

DOC-005/DOC-006 name operations by the path a caller reaches them through from
``BastionVaultClient`` (e.g. ``Kv.V2.ReadSecret``), not by their declaring type alone —
several ``*Operations`` classes (``AuthRoleAdminOperations``, `CertOperations`` reached
through both ``Auth.Cert`` and elsewhere, etc.) are reachable from more than one place.
``PublicApiSurface.txt`` is the authoritative, generated list of what is public (per the
brief); this module never re-derives "is this public" from the .cs source, only *what it
is called* and *where its body lives*, both of which the surface file cannot say.
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


@dataclass(frozen=True)
class PublicOperation:
    """One public method reachable from the SDK's facade tree."""

    declaring_type: str  # short name, e.g. "KvV2Operations"
    method_name: str  # as declared, e.g. "ReadSecretAsync"
    canonical_names: tuple[str, ...]  # every dotted path this method is reachable by


def _short_type(qualified: str) -> str:
    return qualified[len(NAMESPACE) :] if qualified.startswith(NAMESPACE) else qualified


def _operation_name(method_name: str) -> str:
    return method_name[: -len("Async")] if method_name.endswith("Async") else method_name


def parse_public_api_surface(path: Path) -> tuple[list[tuple[str, str]], dict[str, list[tuple[str, str]]]]:
    """Return (method entries, navigation edges).

    ``method entries`` is a list of ``(declaring_type, method_name)`` for every
    ``: method`` line whose declaring type ends in ``Operations``.

    ``navigation edges`` maps a declaring type to a list of ``(property_name,
    target_type)`` for every ``: property ... {get}`` line whose target type also ends
    in ``Operations`` — the edges of the facade reachability graph.
    """

    methods: list[tuple[str, str]] = []
    edges: dict[str, list[tuple[str, str]]] = {}

    for line in path.read_text(encoding="utf-8").splitlines():
        method_match = METHOD_LINE_RE.match(line)
        if method_match:
            declaring_type = _short_type(method_match.group("type"))
            if declaring_type.endswith(OPERATIONS_SUFFIX):
                methods.append((declaring_type, method_match.group("name")))
            continue

        property_match = PROPERTY_LINE_RE.match(line)
        if property_match:
            declaring_type = _short_type(property_match.group("type"))
            return_type = _short_type(property_match.group("return_type").strip())
            if return_type.endswith(OPERATIONS_SUFFIX):
                edges.setdefault(declaring_type, []).append((property_match.group("name"), return_type))

    return methods, edges


def build_canonical_prefixes(edges: dict[str, list[tuple[str, str]]]) -> dict[str, list[str]]:
    """BFS from ``BastionVaultClient`` over the navigation edges.

    Returns declaring-type -> list of dotted prefixes it is reachable by (usually one;
    more than one means the type is reachable through more than one facade path, which
    is reported rather than silently collapsed).
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


def build_public_operations(surface_path: Path) -> list[PublicOperation]:
    methods, edges = parse_public_api_surface(surface_path)
    prefixes = build_canonical_prefixes(edges)

    operations: list[PublicOperation] = []
    for declaring_type, method_name in methods:
        type_prefixes = prefixes.get(declaring_type)
        if not type_prefixes:
            # Unreachable from BastionVaultClient by a navigation property chain: still a
            # public method (e.g. a static helper reached only via `new KvV2Operations(...)`
            # would not occur here since these are all internal constructors, but a type
            # this generator's reachability BFS missed is reported, not silently dropped).
            canonical_names: tuple[str, ...] = ()
        else:
            canonical_names = tuple(
                f"{prefix}.{_operation_name(method_name)}" for prefix in sorted(type_prefixes)
            )
        operations.append(PublicOperation(declaring_type, method_name, canonical_names))
    return operations
