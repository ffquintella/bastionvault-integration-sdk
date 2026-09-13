"""Synthetic tests for the traceability parser and ratchet gate."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

from tools.traceability.traceability import (
    TestReference,
    derive_integration_scenarios,
    evaluate_gate,
    parse_appendix_d,
    parse_dotnet_file,
    parse_python_file,
    parse_rust_file,
    scan_tests,
)


class TraceabilityFixtures(unittest.TestCase):
    def setUp(self) -> None:
        parent = Path(__file__).resolve().parent
        self.root = parent / f"tmp-{next(tempfile._get_candidate_names())}"
        subprocess.run(
            ["cmd.exe", "/d", "/c", "mkdir", str(self.root)],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)
        if os.name == "nt":
            subprocess.run(
                ["cmd.exe", "/d", "/c", "rd", "/s", "/q", str(self.root)],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )

    def write(self, relative: str, content: str) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def test_dotnet_requirement_trait_and_stacked_markers(self) -> None:
        path = self.write(
            "dotnet/Bastion.Tests/Unit.cs",
            """\
using Xunit;

[Requirement("KV2-004")]
[Trait("Requirement", "TRN-040")]
[Fact]
public void Test_attribute_and_trait() { }

[Fact] [Requirement("AUT-001")] [Requirement("ITG-S14")]
public async Task Test_stacked() { }
""",
        )

        references = parse_dotnet_file(path, self.root)
        actual = {(item.requirement_id, item.test_name) for item in references}
        self.assertEqual(
            {
                ("KV2-004", "Test_attribute_and_trait"),
                ("TRN-040", "Test_attribute_and_trait"),
                ("AUT-001", "Test_stacked"),
                ("ITG-S14", "Test_stacked"),
            },
            actual,
        )
        self.assertTrue(all(item.file == "dotnet/Bastion.Tests/Unit.cs" for item in references))

    def test_dotnet_only_tests_directories_are_scanned(self) -> None:
        path = self.write(
            "dotnet/Production/NotTests.cs",
            '[Requirement("KV2-004")] public void NotATest() { }',
        )
        self.assertEqual([], parse_dotnet_file(path, self.root))

    def test_rust_test_directory_and_cfg_test_module_suffixes(self) -> None:
        integration = self.write(
            "rust/sdk/tests/integration.rs",
            """\
#[test]
fn covers_kv2_004_trn_040() {}

#[tokio::test]
async fn covers_itg_s14() {}

fn helper_kv2_004() {}
""",
        )
        library = self.write(
            "rust/sdk/src/lib.rs",
            """\
pub fn production_kv2_004() {}

#[cfg(test)]
mod tests {
    #[test]
    fn covers_sys_001() {}
}
""",
        )

        integration_refs = parse_rust_file(integration, self.root)
        library_refs = parse_rust_file(library, self.root)
        self.assertEqual(
            {item.requirement_id for item in integration_refs},
            {"KV2-004", "TRN-040", "ITG-S14"},
        )
        self.assertEqual({item.requirement_id for item in library_refs}, {"SYS-001"})
        self.assertEqual([], [item for item in integration_refs if item.test_name == "helper_kv2_004"])

    def test_python_docstring_tags_are_whitespace_or_comma_separated(self) -> None:
        path = self.write(
            "python/tests/test_markers.py",
            """\
def test_docstring_tags():
    \"\"\"@req CFG-001, TRN-040\n    @req ITG-S14\"\"\"
    pass

def helper_with_a_tag():
    \"\"\"@req ERR-001\"\"\"
    pass
""",
        )
        references = parse_python_file(path, self.root)
        self.assertEqual(
            {item.requirement_id for item in references},
            {"CFG-001", "TRN-040", "ITG-S14"},
        )
        self.assertEqual({item.test_name for item in references}, {"test_docstring_tags"})

    def test_appendix_d_reads_ids_and_checks_stated_total(self) -> None:
        path = self.write(
            "appendix.md",
            """\
# Appendix D

Total requirements: **3**

## All requirement IDs

### AUT
| ID | Document |
|----|----------|
| AUT-001 | x |
| AUT-010 | x |

### KV2
| ID | Document |
|----|----------|
| KV2-004 | x |
""",
        )
        self.assertEqual({"AUT-001", "AUT-010", "KV2-004"}, parse_appendix_d(path))

    def test_appendix_d_count_mismatch_fails_loudly(self) -> None:
        path = self.write(
            "appendix.md",
            """\
Total requirements: **2**
## All requirement IDs
| ID | Document |
|----|----------|
| AUT-001 | x |
""",
        )
        with self.assertRaisesRegex(ValueError, "expected 2 IDs, parsed 1"):
            parse_appendix_d(path)

    def test_integration_scenarios_are_derived_from_numbered_list(self) -> None:
        path = self.write(
            "testing.md",
            """\
### Required scenarios
1. First
2. Second
4. Fourth

### Assertions the integration harness MUST make globally
""",
        )
        self.assertEqual(
            {"ITG-S01", "ITG-S02", "ITG-S04"},
            derive_integration_scenarios(path),
        )

    def test_gate_rejects_uncovered_id_without_baseline(self) -> None:
        violations = evaluate_gate({"KV2-004"}, {}, set())
        self.assertEqual({"KV2-004"}, set(violations))

    def test_gate_rejects_stale_baseline_entry(self) -> None:
        coverage = {"KV2-004": [TestReference("KV2-004", "x.py", 1, "test_x")]}
        violations = evaluate_gate({"KV2-004"}, coverage, {"KV2-004"})
        self.assertEqual({"KV2-004"}, set(violations))

    def test_gate_rejects_id_in_neither_covered_set_nor_baseline(self) -> None:
        violations = evaluate_gate({"AUT-001", "KV2-004"}, {"AUT-001": []}, set())
        self.assertIn("KV2-004", violations)

    def test_gate_rejects_baseline_id_outside_applicable_set(self) -> None:
        violations = evaluate_gate({"KV2-004"}, {}, {"AUT-001"})
        self.assertEqual({"AUT-001", "KV2-004"}, set(violations))

    def test_scan_succeeds_when_no_test_files_exist(self) -> None:
        self.assertEqual({}, scan_tests(self.root))


if __name__ == "__main__":
    unittest.main()
