#!/usr/bin/env python3
"""Specification provenance tool (DR-0011, CNF-044...CNF-046).

``specifications/`` is derived from the BastionVault server repository. This tool
maintains and checks ``specifications/provenance.json``, which pins the upstream ref the
specification was derived from and the git object id of every upstream source that feeds
a specification document.

Modes
-----

* ``--update [--ref REF]`` resolves ``REF`` (default: the manifest's own pinned ref),
  fetches the recursive git tree at that ref, and rewrites the manifest's ``upstream``
  block and every ``sources[].objectId``. ``path``, ``feeds``, ``sensitivity`` and
  ``notes`` are preserved exactly (D-PRV-1).

* ``--check [--ref REF] [--strict] [--out DIR]`` (default ``REF``: ``main``) compares the
  pinned object ids against ``REF`` without needing a local checkout, and reports drift
  grouped by the specification document that needs review (D-PRV-4). Exits non-zero when
  an ``authoritative`` source changed or went missing; ``--strict`` also fails on
  ``corroborating`` drift (D-PRV-3).

* ``--verify-local PATH [--ref REF]`` (default ``REF``: ``HEAD``) performs the same
  comparison against a local checkout using ``git rev-parse <ref>:<path>``, proving
  D-PRV-2's claim that one hash format verifies identically online and offline.

Any source path that no longer exists at the compared ref is reported as ``missing``,
which is a distinct outcome from ``changed`` (D-PRV-5's consequences: a renamed or removed
upstream path must not silently read as "no drift").

A network or HTTP failure, or a truncated GitHub tree response, is reported as *unknown*
(exit code 2) rather than as "no drift" (D-PRV-7).
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping, Sequence

MANIFEST_PATH = Path("specifications") / "provenance.json"
SCHEMA_URI = "https://bastionvault.dev/sdk/provenance.schema.json"
GITHUB_API = "https://api.github.com"
REQUEST_TIMEOUT_SECONDS = 30

STATUS_UNCHANGED = "unchanged"
STATUS_CHANGED = "changed"
STATUS_MISSING = "missing"
SENSITIVITIES = ("authoritative", "corroborating")


class ProvenanceError(Exception):
    """A usage or manifest-shape error. Distinct from an unknown-drift condition."""


class ProvenanceUnknown(Exception):
    """The comparison could not be performed at all (D-PRV-7). Never treat as clean."""


@dataclass(frozen=True)
class Upstream:
    repository: str
    ref: str
    release: str
    commit: str
    tree_sha: str
    pinned_on: str

    def to_json(self) -> dict[str, str]:
        return {
            "repository": self.repository,
            "ref": self.ref,
            "release": self.release,
            "commit": self.commit,
            "treeSha": self.tree_sha,
            "pinnedOn": self.pinned_on,
        }


@dataclass(frozen=True)
class Source:
    path: str
    type: str
    object_id: str
    sensitivity: str
    feeds: tuple[str, ...]
    notes: str | None = None

    def to_json(self) -> dict[str, object]:
        data: dict[str, object] = {
            "path": self.path,
            "type": self.type,
            "objectId": self.object_id,
            "sensitivity": self.sensitivity,
            "feeds": list(self.feeds),
        }
        if self.notes is not None:
            data["notes"] = self.notes
        return data


@dataclass(frozen=True)
class Manifest:
    specification_version: str
    upstream: Upstream
    sources: tuple[Source, ...]

    def to_json(self) -> dict[str, object]:
        return {
            "$schema": SCHEMA_URI,
            "specificationVersion": self.specification_version,
            "upstream": self.upstream.to_json(),
            "sources": [source.to_json() for source in self.sources],
        }


@dataclass(frozen=True)
class Classified:
    path: str
    type: str
    sensitivity: str
    feeds: tuple[str, ...]
    pinned_object_id: str
    actual_object_id: str | None
    status: str


# --------------------------------------------------------------------------------------
# Manifest load/save
# --------------------------------------------------------------------------------------


def load_manifest(path: Path) -> Manifest:
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise ProvenanceError(f"manifest not found: {path}") from error
    except json.JSONDecodeError as error:
        raise ProvenanceError(f"manifest is not valid JSON: {error}") from error
    return _manifest_from_dict(raw)


def _require(data: Mapping[str, object], key: str, container: str) -> object:
    if key not in data:
        raise ProvenanceError(f"{container} is missing required field '{key}'")
    return data[key]


def _manifest_from_dict(raw: object) -> Manifest:
    if not isinstance(raw, dict):
        raise ProvenanceError("manifest root must be a JSON object")

    version = _require(raw, "specificationVersion", "manifest")
    if not isinstance(version, str):
        raise ProvenanceError("manifest.specificationVersion must be a string")

    upstream_raw = _require(raw, "upstream", "manifest")
    if not isinstance(upstream_raw, dict):
        raise ProvenanceError("manifest.upstream must be an object")
    upstream = Upstream(
        repository=str(_require(upstream_raw, "repository", "manifest.upstream")),
        ref=str(_require(upstream_raw, "ref", "manifest.upstream")),
        release=str(_require(upstream_raw, "release", "manifest.upstream")),
        commit=str(_require(upstream_raw, "commit", "manifest.upstream")),
        tree_sha=str(_require(upstream_raw, "treeSha", "manifest.upstream")),
        pinned_on=str(_require(upstream_raw, "pinnedOn", "manifest.upstream")),
    )

    sources_raw = _require(raw, "sources", "manifest")
    if not isinstance(sources_raw, list) or not sources_raw:
        raise ProvenanceError("manifest.sources must be a non-empty array")

    sources: list[Source] = []
    seen_paths: set[str] = set()
    for index, entry in enumerate(sources_raw):
        container = f"manifest.sources[{index}]"
        if not isinstance(entry, dict):
            raise ProvenanceError(f"{container} must be an object")
        path = str(_require(entry, "path", container))
        if path in seen_paths:
            raise ProvenanceError(f"duplicate source path: {path}")
        seen_paths.add(path)
        source_type = str(_require(entry, "type", container))
        if source_type not in ("blob", "tree"):
            raise ProvenanceError(f"{container}.type must be 'blob' or 'tree'")
        sensitivity = str(_require(entry, "sensitivity", container))
        if sensitivity not in SENSITIVITIES:
            raise ProvenanceError(f"{container}.sensitivity must be one of {SENSITIVITIES}")
        feeds_raw = _require(entry, "feeds", container)
        if not isinstance(feeds_raw, list) or not feeds_raw:
            raise ProvenanceError(f"{container}.feeds must be a non-empty array")
        notes = entry.get("notes")
        if notes is not None and not isinstance(notes, str):
            raise ProvenanceError(f"{container}.notes must be a string")
        sources.append(
            Source(
                path=path,
                type=source_type,
                object_id=str(_require(entry, "objectId", container)),
                sensitivity=sensitivity,
                feeds=tuple(str(feed) for feed in feeds_raw),
                notes=notes,
            )
        )

    return Manifest(specification_version=version, upstream=upstream, sources=tuple(sources))


def write_manifest(path: Path, manifest: Manifest) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest.to_json(), indent=2) + "\n", encoding="utf-8")


# --------------------------------------------------------------------------------------
# Upstream access (GitHub API, unauthenticated with optional GITHUB_TOKEN — D-PRV-7)
# --------------------------------------------------------------------------------------


def _owner_repo(repository_url: str) -> str:
    parsed = urllib.parse.urlparse(repository_url)
    owner_repo = parsed.path.strip("/")
    if owner_repo.endswith(".git"):
        owner_repo = owner_repo[: -len(".git")]
    if not owner_repo:
        raise ProvenanceError(f"could not derive owner/repo from {repository_url!r}")
    return owner_repo


def _api_get(url: str, token: str | None) -> dict:
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "bastionvault-integration-sdk-provenance-tool",
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=REQUEST_TIMEOUT_SECONDS) as response:
            payload = response.read()
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        raise ProvenanceUnknown(f"could not reach {url}: {error}") from error
    try:
        return json.loads(payload)
    except json.JSONDecodeError as error:
        raise ProvenanceUnknown(f"unexpected (non-JSON) response from {url}: {error}") from error


def fetch_upstream_tree(
    repository_url: str, ref: str, token: str | None, api_get: Callable[[str, str | None], dict] = _api_get
) -> tuple[str, str, dict[str, tuple[str, str]]]:
    """Resolve ``ref`` to a commit, then fetch its recursive tree.

    Returns ``(commit_sha, tree_sha, {path: (object_id, type)})``. Raises
    :class:`ProvenanceUnknown` on any network/HTTP failure or a truncated tree response
    (D-PRV-7) — never treat those as "no drift".
    """

    owner_repo = _owner_repo(repository_url)
    commit_json = api_get(f"{GITHUB_API}/repos/{owner_repo}/commits/{ref}", token)
    commit_sha = commit_json.get("sha")
    tree_sha = (commit_json.get("commit") or {}).get("tree", {}).get("sha")
    if not commit_sha or not tree_sha:
        raise ProvenanceUnknown(f"could not resolve ref {ref!r} for {owner_repo}: malformed commit response")

    tree_json = api_get(f"{GITHUB_API}/repos/{owner_repo}/git/trees/{tree_sha}?recursive=1", token)
    if tree_json.get("truncated"):
        raise ProvenanceUnknown(
            f"upstream tree at {ref!r} ({owner_repo}) was truncated by the GitHub API; "
            "refusing to report drift against a partial listing"
        )
    entries = tree_json.get("tree")
    if not isinstance(entries, list):
        raise ProvenanceUnknown(f"malformed tree response for {ref!r} ({owner_repo})")

    lookup: dict[str, tuple[str, str]] = {}
    for entry in entries:
        path = entry.get("path")
        sha = entry.get("sha")
        entry_type = entry.get("type")
        if path and sha and entry_type in ("blob", "tree"):
            lookup[path] = (sha, entry_type)
    return commit_sha, tree_sha, lookup


def local_lookup(checkout: Path, ref: str) -> Callable[[str], str | None]:
    """Build a lookup function backed by ``git rev-parse <ref>:<path>`` in ``checkout``."""

    def lookup(path: str) -> str | None:
        result = subprocess.run(
            ["git", "-C", str(checkout), "rev-parse", f"{ref}:{path}"],
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            return None
        return result.stdout.strip() or None

    return lookup


# --------------------------------------------------------------------------------------
# Comparison
# --------------------------------------------------------------------------------------


def classify_sources(sources: Sequence[Source], lookup: Callable[[str], str | None]) -> list[Classified]:
    classified: list[Classified] = []
    for source in sources:
        actual = lookup(source.path)
        if actual is None:
            status = STATUS_MISSING
        elif actual == source.object_id:
            status = STATUS_UNCHANGED
        else:
            status = STATUS_CHANGED
        classified.append(
            Classified(
                path=source.path,
                type=source.type,
                sensitivity=source.sensitivity,
                feeds=source.feeds,
                pinned_object_id=source.object_id,
                actual_object_id=actual,
                status=status,
            )
        )
    return classified


def has_blocking_drift(classified: Sequence[Classified], strict: bool) -> bool:
    for item in classified:
        if item.status == STATUS_UNCHANGED:
            continue
        if item.sensitivity == "authoritative":
            return True
        if strict and item.sensitivity == "corroborating":
            return True
    return False


def group_by_document(classified: Sequence[Classified]) -> dict[str, list[Classified]]:
    """Group drift by the specification document that needs review (D-PRV-4)."""

    by_document: dict[str, list[Classified]] = defaultdict(list)
    for item in classified:
        for document in item.feeds:
            by_document[document].append(item)
    return dict(by_document)


def build_report(
    ref: str, resolved_commit: str | None, classified: Sequence[Classified], strict: bool
) -> dict[str, object]:
    by_document = group_by_document(classified)
    documents_report = []
    for document in sorted(by_document):
        entries = sorted(by_document[document], key=lambda item: item.path)
        needs_review = any(
            entry.status != STATUS_UNCHANGED
            and (entry.sensitivity == "authoritative" or (strict and entry.sensitivity == "corroborating"))
            for entry in entries
        )
        documents_report.append(
            {
                "document": document,
                "needsReview": needs_review,
                "sources": [
                    {
                        "path": entry.path,
                        "sensitivity": entry.sensitivity,
                        "status": entry.status,
                        "pinnedObjectId": entry.pinned_object_id,
                        "actualObjectId": entry.actual_object_id,
                    }
                    for entry in entries
                ],
            }
        )

    summary = {
        "unchanged": sum(1 for item in classified if item.status == STATUS_UNCHANGED),
        "changed": sum(1 for item in classified if item.status == STATUS_CHANGED),
        "missing": sum(1 for item in classified if item.status == STATUS_MISSING),
        "total": len(classified),
        "blocking": has_blocking_drift(classified, strict),
    }
    return {
        "ref": ref,
        "resolvedCommit": resolved_commit,
        "strict": strict,
        "summary": summary,
        "documents": documents_report,
    }


def render_report(out_dir: Path, report: Mapping[str, object]) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    out_dir.joinpath("report.json").write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    summary = report["summary"]
    assert isinstance(summary, dict)
    lines = [
        "# Specification provenance report",
        "",
        f"Ref: `{report['ref']}` · Resolved commit: `{report['resolvedCommit']}` · Strict: {report['strict']}",
        "",
        (
            f"Unchanged: {summary['unchanged']} · Changed: {summary['changed']} · "
            f"Missing: {summary['missing']} · Total: {summary['total']} · "
            f"Blocking drift: {summary['blocking']}"
        ),
        "",
        "## Specification documents needing review",
        "",
        "| Document | Needs review | Sources |",
        "|----------|--------------|---------|",
    ]
    documents = report["documents"]
    assert isinstance(documents, list)
    for entry in documents:
        assert isinstance(entry, dict)
        sources_cell = "<br>".join(
            f"{source['path']} ({source['sensitivity']}, {source['status']})" for source in entry["sources"]
        )
        lines.append(f"| {entry['document']} | {entry['needsReview']} | {sources_cell} |")
    out_dir.joinpath("report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def print_report(report: Mapping[str, object]) -> None:
    summary = report["summary"]
    assert isinstance(summary, dict)
    documents = report["documents"]
    assert isinstance(documents, list)
    for entry in documents:
        assert isinstance(entry, dict)
        if not entry["needsReview"]:
            continue
        print(f"REVIEW {entry['document']}")
        for source in entry["sources"]:
            if source["status"] == STATUS_UNCHANGED:
                continue
            print(
                f"  - {source['path']} [{source['sensitivity']}] {source['status']}: "
                f"pinned {source['pinnedObjectId']} -> actual {source['actualObjectId']}"
            )
    print(
        f"unchanged: {summary['unchanged']} / changed: {summary['changed']} / "
        f"missing: {summary['missing']} / total: {summary['total']}"
    )


# --------------------------------------------------------------------------------------
# Modes
# --------------------------------------------------------------------------------------


def run_update(
    manifest_path: Path,
    ref: str | None,
    token: str | None,
    fetch: Callable[[str, str, str | None], tuple[str, str, dict[str, tuple[str, str]]]] = fetch_upstream_tree,
) -> int:
    manifest = load_manifest(manifest_path)
    resolved_ref = ref or manifest.upstream.ref
    commit_sha, tree_sha, lookup = fetch(manifest.upstream.repository, resolved_ref, token)

    missing = [source.path for source in manifest.sources if source.path not in lookup]
    if missing:
        raise ProvenanceError(
            "cannot update: the following pinned paths no longer exist at "
            f"{resolved_ref!r}: {', '.join(sorted(missing))}. Repoint or remove them first."
        )

    new_sources = tuple(
        Source(
            path=source.path,
            type=lookup[source.path][1],
            object_id=lookup[source.path][0],
            sensitivity=source.sensitivity,
            feeds=source.feeds,
            notes=source.notes,
        )
        for source in manifest.sources
    )

    release = _derive_release(resolved_ref, manifest.upstream.release)
    new_upstream = Upstream(
        repository=manifest.upstream.repository,
        ref=resolved_ref,
        release=release,
        commit=commit_sha,
        tree_sha=tree_sha,
        pinned_on=_today(),
    )
    new_manifest = Manifest(
        specification_version=manifest.specification_version, upstream=new_upstream, sources=new_sources
    )
    write_manifest(manifest_path, new_manifest)
    print(f"updated manifest: ref={resolved_ref} commit={commit_sha} sources={len(new_sources)}")
    return 0


def _derive_release(ref: str, previous_release: str) -> str:
    if ref.startswith("v") and ref[1:2].isdigit():
        return ref[1:]
    return previous_release


def _today() -> str:
    import datetime

    return datetime.date.today().isoformat()


def run_check(
    manifest_path: Path,
    ref: str,
    strict: bool,
    out: Path | None,
    token: str | None,
    fetch: Callable[[str, str, str | None], tuple[str, str, dict[str, tuple[str, str]]]] = fetch_upstream_tree,
) -> int:
    manifest = load_manifest(manifest_path)
    try:
        commit_sha, _tree_sha, lookup = fetch(manifest.upstream.repository, ref, token)
    except ProvenanceUnknown as error:
        print(f"UNKNOWN: {error}", file=sys.stderr)
        return 2

    classified = classify_sources(manifest.sources, lambda path: lookup.get(path, (None, None))[0])
    report = build_report(ref, commit_sha, classified, strict)
    if out:
        render_report(out, report)
    print_report(report)
    return 1 if has_blocking_drift(classified, strict) else 0


def run_verify_local(manifest_path: Path, checkout: Path, ref: str, strict: bool, out: Path | None) -> int:
    manifest = load_manifest(manifest_path)
    if not (checkout / ".git").exists():
        print(f"UNKNOWN: {checkout} does not look like a git checkout (.git not found)", file=sys.stderr)
        return 2

    lookup = local_lookup(checkout, ref)
    classified = classify_sources(manifest.sources, lookup)
    report = build_report(ref, None, classified, strict)
    if out:
        render_report(out, report)
    print_report(report)
    return 1 if has_blocking_drift(classified, strict) else 0


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--update", action="store_true", help="re-pin the manifest at --ref")
    modes.add_argument("--check", action="store_true", help="compare the manifest against --ref via the GitHub API")
    modes.add_argument("--verify-local", type=Path, metavar="PATH", help="compare against a local checkout")
    parser.add_argument("--ref", type=str, default=None, help="upstream ref (default: main for --check, HEAD for --verify-local, the pinned ref for --update)")
    parser.add_argument("--strict", action="store_true", help="also fail on corroborating drift")
    parser.add_argument("--out", type=Path, default=None, help="directory for report.md and report.json")
    parser.add_argument("--root", type=Path, default=None, help="repository root (default: inferred)")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = _build_parser()
    args = parser.parse_args(argv)
    root = (args.root or Path(__file__).resolve().parents[2]).resolve()
    manifest_path = root / MANIFEST_PATH
    token = _github_token()

    try:
        if args.update:
            return run_update(manifest_path, args.ref, token)
        if args.check:
            return run_check(manifest_path, args.ref or "main", args.strict, args.out, token)
        return run_verify_local(manifest_path, args.verify_local, args.ref or "HEAD", args.strict, args.out)
    except ProvenanceError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2
    except ProvenanceUnknown as error:
        print(f"UNKNOWN: {error}", file=sys.stderr)
        return 2


def _github_token() -> str | None:
    import os

    return os.environ.get("GITHUB_TOKEN") or None


if __name__ == "__main__":
    sys.exit(main())
