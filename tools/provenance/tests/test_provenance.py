"""Synthetic tests for the specification provenance tool (DR-0011).

Runnable directly as ``python tools/provenance/tests/test_provenance.py`` with no
``PYTHONPATH`` set: the repo root is put on ``sys.path`` so the
``tools.provenance.provenance`` import form below resolves either way.

No network access is used: ``--check``/``--update`` are exercised against a stubbed
``fetch`` callable, and ``--verify-local`` against a throwaway local git repository.
"""

from __future__ import annotations

import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from tools.provenance.provenance import (  # noqa: E402
    ProvenanceUnknown,
    Source,
    Upstream,
    Manifest,
    build_report,
    classify_sources,
    group_by_document,
    has_blocking_drift,
    load_manifest,
    run_check,
    run_update,
    run_verify_local,
    write_manifest,
)

UPSTREAM = Upstream(
    repository="https://github.com/ffquintella/BastionVault",
    ref="v0.42.0",
    release="0.42.0",
    commit="1111111111111111111111111111111111111111",
    tree_sha="2222222222222222222222222222222222222222",
    pinned_on="2026-09-15",
)


def make_manifest(sources: tuple[Source, ...]) -> Manifest:
    return Manifest(specification_version="1.0.0", upstream=UPSTREAM, sources=sources)


class ProvenanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="provenance-tests-"))
        self.manifest_path = self.root / "specifications" / "provenance.json"

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    # -- classification -----------------------------------------------------------

    def test_classify_unchanged_changed_missing(self) -> None:
        sources = (
            Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),
            Source("docs/b.md", "blob", "bbbb", "authoritative", ("00-overview.md",)),
            Source("docs/c.md", "blob", "cccc", "authoritative", ("00-overview.md",)),
        )
        lookup = {"docs/a.md": "aaaa", "docs/b.md": "zzzz"}.get
        classified = classify_sources(sources, lookup)
        statuses = {item.path: item.status for item in classified}
        self.assertEqual(
            {"docs/a.md": "unchanged", "docs/b.md": "changed", "docs/c.md": "missing"},
            statuses,
        )

    def test_authoritative_drift_is_blocking_corroborating_is_not(self) -> None:
        sources = (
            Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),
            Source("crates/x/src", "tree", "bbbb", "corroborating", ("00-overview.md",)),
        )
        # Only the corroborating source drifted.
        lookup = {"docs/a.md": "aaaa", "crates/x/src": "zzzz"}.get
        classified = classify_sources(sources, lookup)
        self.assertFalse(has_blocking_drift(classified, strict=False))
        self.assertTrue(has_blocking_drift(classified, strict=True))

        # Now the authoritative source drifts too.
        lookup2 = {"docs/a.md": "zzzz", "crates/x/src": "bbbb"}.get
        classified2 = classify_sources(sources, lookup2)
        self.assertTrue(has_blocking_drift(classified2, strict=False))
        self.assertTrue(has_blocking_drift(classified2, strict=True))

    def test_missing_authoritative_source_is_blocking(self) -> None:
        sources = (Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        classified = classify_sources(sources, lambda path: None)
        self.assertEqual("missing", classified[0].status)
        self.assertTrue(has_blocking_drift(classified, strict=False))

    def test_grouping_is_by_specification_document(self) -> None:
        sources = (
            Source("docs/a.md", "blob", "aaaa", "authoritative", ("07-kv-engine.md", "08-transit-engine.md")),
            Source("crates/x/src", "tree", "bbbb", "corroborating", ("07-kv-engine.md",)),
        )
        classified = classify_sources(sources, lambda path: "aaaa" if path == "docs/a.md" else "cccc")
        by_document = group_by_document(classified)
        self.assertEqual({"07-kv-engine.md", "08-transit-engine.md"}, set(by_document))
        self.assertEqual(2, len(by_document["07-kv-engine.md"]))
        self.assertEqual(1, len(by_document["08-transit-engine.md"]))

    def test_report_needs_review_flag_respects_strict(self) -> None:
        sources = (Source("crates/x/src", "tree", "bbbb", "corroborating", ("07-kv-engine.md",)),)
        classified = classify_sources(sources, lambda path: "zzzz")
        loose = build_report("main", "deadbeef", classified, strict=False)
        strict = build_report("main", "deadbeef", classified, strict=True)
        self.assertFalse(loose["documents"][0]["needsReview"])
        self.assertTrue(strict["documents"][0]["needsReview"])

    # -- --check via stubbed fetch --------------------------------------------------

    def _write_manifest(self, sources: tuple[Source, ...]) -> None:
        write_manifest(self.manifest_path, make_manifest(sources))

    def test_check_exits_zero_when_clean(self) -> None:
        sources = (Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {"docs/a.md": ("aaaa", "blob")}

        out = io.StringIO()
        with redirect_stdout(out):
            code = run_check(self.manifest_path, "main", strict=False, out=None, token=None, fetch=fetch)
        self.assertEqual(0, code)

    def test_check_exits_one_on_authoritative_drift(self) -> None:
        sources = (Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {"docs/a.md": ("zzzz", "blob")}

        out = io.StringIO()
        with redirect_stdout(out):
            code = run_check(self.manifest_path, "main", strict=False, out=None, token=None, fetch=fetch)
        self.assertEqual(1, code)
        self.assertIn("REVIEW 00-overview.md", out.getvalue())

    def test_check_exits_zero_on_corroborating_drift_without_strict(self) -> None:
        sources = (Source("crates/x/src", "tree", "bbbb", "corroborating", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {"crates/x/src": ("zzzz", "tree")}

        out = io.StringIO()
        with redirect_stdout(out):
            code = run_check(self.manifest_path, "main", strict=False, out=None, token=None, fetch=fetch)
        self.assertEqual(0, code)

    def test_check_exits_one_on_corroborating_drift_with_strict(self) -> None:
        sources = (Source("crates/x/src", "tree", "bbbb", "corroborating", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {"crates/x/src": ("zzzz", "tree")}

        out = io.StringIO()
        with redirect_stdout(out):
            code = run_check(self.manifest_path, "main", strict=True, out=None, token=None, fetch=fetch)
        self.assertEqual(1, code)

    def test_check_reports_missing_path_distinctly_from_changed(self) -> None:
        sources = (Source("docs/gone.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {}

        out = io.StringIO()
        with redirect_stdout(out):
            code = run_check(self.manifest_path, "main", strict=False, out=None, token=None, fetch=fetch)
        self.assertEqual(1, code)
        self.assertIn("missing", out.getvalue())

    def test_check_writes_report_grouped_by_document(self) -> None:
        sources = (
            Source("docs/a.md", "blob", "aaaa", "authoritative", ("07-kv-engine.md",)),
        )
        self._write_manifest(sources)
        out_dir = self.root / "report"

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {"docs/a.md": ("zzzz", "blob")}

        with redirect_stdout(io.StringIO()):
            run_check(self.manifest_path, "main", strict=False, out=out_dir, token=None, fetch=fetch)

        report = json.loads((out_dir / "report.json").read_text(encoding="utf-8"))
        self.assertEqual(["07-kv-engine.md"], [entry["document"] for entry in report["documents"]])
        self.assertTrue((out_dir / "report.md").is_file())

    # -- unknown outcomes (network failure / truncation) -----------------------------

    def test_check_returns_unknown_exit_code_on_fetch_failure(self) -> None:
        sources = (Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            raise ProvenanceUnknown("network is down")

        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()) as err:
            code = run_check(self.manifest_path, "main", strict=False, out=None, token=None, fetch=fetch)
        self.assertEqual(2, code)
        self.assertIn("UNKNOWN", err.getvalue())

    def test_check_returns_unknown_exit_code_on_truncated_tree(self) -> None:
        sources = (Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            raise ProvenanceUnknown("upstream tree was truncated")

        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()) as err:
            code = run_check(self.manifest_path, "main", strict=False, out=None, token=None, fetch=fetch)
        self.assertEqual(2, code)
        self.assertIn("truncated", err.getvalue())

    # -- --update preserves feeds/sensitivity/notes, rewrites objectId --------------

    def test_update_preserves_feeds_and_rewrites_object_ids(self) -> None:
        sources = (
            Source(
                "docs/a.md",
                "blob",
                "aaaa",
                "authoritative",
                ("00-overview.md", "01-conformance-and-quality.md"),
                notes="a note",
            ),
        )
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {"docs/a.md": ("newsha", "blob")}

        with redirect_stdout(io.StringIO()):
            code = run_update(self.manifest_path, "v0.44.4", token=None, fetch=fetch)
        self.assertEqual(0, code)

        updated = load_manifest(self.manifest_path)
        self.assertEqual("v0.44.4", updated.upstream.ref)
        self.assertEqual("0.44.4", updated.upstream.release)
        self.assertEqual("deadbeef", updated.upstream.commit)
        source = updated.sources[0]
        self.assertEqual("newsha", source.object_id)
        self.assertEqual(("00-overview.md", "01-conformance-and-quality.md"), source.feeds)
        self.assertEqual("authoritative", source.sensitivity)
        self.assertEqual("a note", source.notes)

    def test_update_fails_when_a_pinned_path_no_longer_exists(self) -> None:
        sources = (Source("docs/gone.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        def fetch(repository: str, ref: str, token: str | None):
            return "deadbeef", "cafef00d", {}

        with self.assertRaises(Exception):
            run_update(self.manifest_path, "v0.44.4", token=None, fetch=fetch)

    # -- --verify-local ---------------------------------------------------------------

    def _init_local_repo(self) -> Path:
        checkout = self.root / "checkout"
        checkout.mkdir()
        subprocess.run(["git", "init", "-q"], cwd=checkout, check=True)
        subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=checkout, check=True)
        subprocess.run(["git", "config", "user.name", "Test"], cwd=checkout, check=True)
        (checkout / "docs").mkdir()
        (checkout / "docs" / "a.md").write_text("hello\n", encoding="utf-8")
        subprocess.run(["git", "add", "."], cwd=checkout, check=True)
        subprocess.run(["git", "commit", "-q", "-m", "initial"], cwd=checkout, check=True)
        return checkout

    def test_verify_local_matches_git_rev_parse_object_id(self) -> None:
        checkout = self._init_local_repo()
        blob_sha = subprocess.run(
            ["git", "-C", str(checkout), "rev-parse", "HEAD:docs/a.md"],
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()

        sources = (Source("docs/a.md", "blob", blob_sha, "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        with redirect_stdout(io.StringIO()):
            code = run_verify_local(self.manifest_path, checkout, "HEAD", strict=False, out=None)
        self.assertEqual(0, code)

    def test_verify_local_reports_drift_against_a_different_ref(self) -> None:
        checkout = self._init_local_repo()
        # A blob id that does not match the committed content.
        sources = (Source("docs/a.md", "blob", "0" * 40, "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        with redirect_stdout(io.StringIO()):
            code = run_verify_local(self.manifest_path, checkout, "HEAD", strict=False, out=None)
        self.assertEqual(1, code)

    def test_verify_local_returns_unknown_for_a_non_git_directory(self) -> None:
        not_a_repo = self.root / "not-a-repo"
        not_a_repo.mkdir()
        sources = (Source("docs/a.md", "blob", "aaaa", "authoritative", ("00-overview.md",)),)
        self._write_manifest(sources)

        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()) as err:
            code = run_verify_local(self.manifest_path, not_a_repo, "HEAD", strict=False, out=None)
        self.assertEqual(2, code)
        self.assertIn("UNKNOWN", err.getvalue())


if __name__ == "__main__":
    unittest.main()
