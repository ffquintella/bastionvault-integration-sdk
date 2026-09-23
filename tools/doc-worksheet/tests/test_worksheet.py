"""Integration tests: build the worksheet against the real repository.

Mirrors ``tools/traceability/tests`` and ``tools/error-catalogue/tests``' habit of
running a handful of assertions against the real specification/source alongside the
synthetic unit tests, so a drift here shows up in this suite rather than only being
noticed by a human reading the generated report.
"""

from __future__ import annotations

import shutil
import sys
import tempfile
import unittest
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import worksheet  # noqa: E402
import generate  # noqa: E402

REPO_ROOT = TOOL_DIR.parents[1]


def _skip_if_repo_missing() -> bool:
    return not (REPO_ROOT / "dotnet" / "BastionVault.IntegrationSdk" / "PublicApiSurface.txt").is_file()


class WorksheetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if _skip_if_repo_missing():
            raise unittest.SkipTest("real repository checkout not present")
        cls.rows = worksheet.build_rows(REPO_ROOT)

    def test_row_count_matches_public_api_surface(self) -> None:
        surface = REPO_ROOT / "dotnet" / "BastionVault.IntegrationSdk" / "PublicApiSurface.txt"
        method_lines = [
            line
            for line in surface.read_text(encoding="utf-8").splitlines()
            if " : method " in line and line.split(" : ", 1)[0].split(".")[-1].endswith("Operations")
        ]
        self.assertEqual(len(method_lines), len(self.rows))

    def test_f1_hand_authored_operations_are_found_and_carry_their_tag(self) -> None:
        f1_files = {
            "KvOperations",
            "KvV1Operations",
            "KvV2Operations",
            "LogicalOperations",
            "AuthOperations",
            "AuthRoleAdminOperations",
        }
        f1_rows = [row for row in self.rows if row.declaring_type in f1_files]
        self.assertGreater(len(f1_rows), 0)
        for row in f1_rows:
            self.assertTrue(row.has_spec_tag, f"{row.declaring_type}.{row.method_name} should already carry a tag")

    def test_known_clean_resolution_matches_its_hand_authored_doc_comment(self) -> None:
        # AuthRoleAdminOperations.ReadRoleAsync's doc comment states `GET auth/{mount}/role/{name}`
        # literally; this is the "resolves cleanly" anchor the brief asks for.
        row = next(
            row
            for row in self.rows
            if row.declaring_type == "AuthRoleAdminOperations" and row.method_name == "ReadRoleAsync"
        )
        self.assertEqual("GET", row.http_verb)
        self.assertEqual("auth/{mount}/role/{name}", row.http_path_template)

    def test_known_dynamic_query_operation_is_unresolved_with_a_reason(self) -> None:
        # KvV2Operations.ReadSecretAsync's route helper builds an optional `?version&env`
        # query with a StringBuilder — the documented "cannot guess" case.
        row = next(
            row for row in self.rows if row.declaring_type == "KvV2Operations" and row.method_name == "ReadSecretAsync"
        )
        self.assertIsNone(row.http_path_template)
        self.assertIsNotNone(row.http_path_reason)

    def test_client_side_helper_reports_no_http_call(self) -> None:
        row = next(row for row in self.rows if row.declaring_type == "KvV2Operations" and row.method_name == "DataPath")
        self.assertIsNone(row.http_verb)
        self.assertEqual(0, row.call_site_count)
        self.assertIn("no HTTP call", row.http_verb_reason)

    def test_generate_does_not_touch_the_sdk_or_the_public_api_surface(self) -> None:
        surface = REPO_ROOT / "dotnet" / "BastionVault.IntegrationSdk" / "PublicApiSurface.txt"
        before = surface.read_bytes()
        before_mtime = surface.stat().st_mtime_ns

        out_dir = Path(tempfile.mkdtemp(prefix="doc-worksheet-out-"))
        try:
            exit_code = generate.run(REPO_ROOT, out_dir)
            self.assertEqual(0, exit_code)
            self.assertTrue((out_dir / "report.json").is_file())
            self.assertTrue((out_dir / "report.md").is_file())
        finally:
            shutil.rmtree(out_dir, ignore_errors=True)

        self.assertEqual(before, surface.read_bytes())
        self.assertEqual(before_mtime, surface.stat().st_mtime_ns)


class SpecTagFormTests(unittest.TestCase):
    """Synthetic tests for the two `<spec>` tag forms (DR-0018 D-M11-21): the requirement-ID
    form and the section-file fallback. No real-repo dependency, unlike `WorksheetTests`."""

    def test_requirement_id_form_parses(self) -> None:
        has_tag, text, name, cited, form = worksheet._spec_tag_in(
            "/// <spec>Transit.DeleteKey — TRN-001</spec>"
        )
        self.assertTrue(has_tag)
        self.assertEqual("Transit.DeleteKey", name)
        self.assertEqual("TRN-001", cited)
        self.assertEqual("id", form)

    def test_section_file_fallback_form_parses(self) -> None:
        has_tag, text, name, cited, form = worksheet._spec_tag_in(
            "/// <spec>Ssh.ConfigureCa — 10-ssh-engine.md</spec>"
        )
        self.assertTrue(has_tag)
        self.assertEqual("Ssh.ConfigureCa", name)
        self.assertEqual("10-ssh-engine.md", cited)
        self.assertEqual("section-file", form)

    def test_error_code_in_surrounding_prose_is_not_mistaken_for_the_tag(self) -> None:
        doc_comment = (
            "/// Errors beyond the common set: <c>BV-KV-007</c>.\n"
            "/// <spec>Kv.Read — KV2-011</spec>"
        )
        has_tag, text, name, cited, form = worksheet._spec_tag_in(doc_comment)
        self.assertTrue(has_tag)
        self.assertEqual("KV2-011", cited)
        self.assertEqual("id", form)
        # The chain-and-filter hazard this regex guards against: `BV-KV-007` must never be
        # read as the cited id.
        self.assertNotEqual("BV-KV-007", cited)
        self.assertNotIn("BV-KV-007", worksheet._requirement_ids_in(doc_comment))

    def test_no_tag_returns_all_none(self) -> None:
        has_tag, text, name, cited, form = worksheet._spec_tag_in("/// no tag here")
        self.assertFalse(has_tag)
        self.assertIsNone(text)
        self.assertIsNone(name)
        self.assertIsNone(cited)
        self.assertIsNone(form)

    def _row(self, **overrides: object) -> worksheet.OperationRow:
        defaults = dict(
            canonical_name="X.Y",
            canonical_name_ambiguous=[],
            declaring_type="XOperations",
            method_name="Y",
            file="X.cs",
            line=1,
            http_verb="GET",
            http_verb_reason=None,
            http_path_template="x",
            http_path_reason=None,
            execute_method="ExecuteShapedAsync",
            call_site_count=1,
            appendix_a_match="exact",
            appendix_a_canonical="X.Y",
            appendix_a_verb="GET",
            appendix_a_path="x",
            appendix_a_level="Core",
            appendix_a_section="1",
            existing_requirement_ids=["ABC-001"],
            unknown_requirement_ids=[],
            has_spec_tag=True,
            spec_tag_form="id",
            spec_tag_valid=True,
            spec_tag_text="<spec>X.Y — ABC-001</spec>",
            suggested_spec_tag=None,
            complete=True,
            gaps=[],
        )
        defaults.update(overrides)
        return worksheet.OperationRow(**defaults)  # type: ignore[arg-type]

    def test_summary_counts_the_two_tag_forms_separately(self) -> None:
        rows = [
            self._row(spec_tag_form="id"),
            self._row(spec_tag_form="id"),
            self._row(spec_tag_form="section-file", spec_tag_valid=None),
            self._row(has_spec_tag=False, spec_tag_form=None, spec_tag_valid=None, spec_tag_text=None),
        ]
        summary = worksheet.build_summary(rows)
        self.assertEqual(3, summary["already_tagged"])
        self.assertEqual(2, summary["already_tagged_id_form"])
        self.assertEqual(1, summary["already_tagged_section_file_form"])


if __name__ == "__main__":
    unittest.main()
