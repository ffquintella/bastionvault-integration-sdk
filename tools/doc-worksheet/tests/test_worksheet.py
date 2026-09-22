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


if __name__ == "__main__":
    unittest.main()
