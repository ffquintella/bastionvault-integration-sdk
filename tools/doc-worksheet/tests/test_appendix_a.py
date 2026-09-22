"""Synthetic tests for ``appendix_a``'s Appendix A reader, plus a real-appendix check."""

from __future__ import annotations

import shutil
import sys
import tempfile
import unittest
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import appendix_a  # noqa: E402

REPO_ROOT = TOOL_DIR.parents[1]

SAMPLE = """\
# Appendix A — Endpoint Catalogue

## Token store (`auth/token/`)

| Canonical operation | Verb | Path | Prefix | Level | Notes |
|---|---|---|---|---|---|
| `Auth.Token.Create` | W | `auth/token/create` | v1 | C | |
| `Auth.Token.Renew` / `RenewSelf` | W | `auth/token/renew/{token}` | v1 | C | `{increment}` required |

## Transit (`{mount}` = `transit`) — Level S

| Canonical operation | Verb | Path |
|---|---|---|
| `Transit.ListKeys` | L | `{mount}/keys/` |

## AppID (`auth/{mount}` = `auth/approle`)

| Canonical operation | Verb | Path | Level |
|---|---|---|---|
| `Auth.AppId.Admin.<Field>` (R/W/D) | R/W/D | `auth/{mount}/role/{name}/{field}` | X |

## LDAP, Files — Level X

| Area | Paths |
|---|---|
| LDAP | `{mount}/config` |

## Endpoints that do **not** exist (do not implement)

`sys/leases/*`
"""


class AppendixATests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="appendix-a-tests-"))

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def write(self, content: str) -> Path:
        path = self.root / "appendix-a.md"
        path.write_text(content, encoding="utf-8")
        return path

    def test_slash_alternation_shares_the_dotted_prefix(self) -> None:
        parsed = appendix_a.parse_appendix_a(self.write(SAMPLE))
        self.assertIn("Auth.Token.Create", parsed.entries)
        self.assertIn("Auth.Token.Renew", parsed.entries)
        self.assertIn("Auth.Token.RenewSelf", parsed.entries)
        entry = parsed.entries["Auth.Token.Renew"][0]
        self.assertEqual("auth/token/renew/{token}", entry.path_cell.strip("`"))
        self.assertEqual("C", entry.level)

    def test_heading_level_applies_when_no_row_level_column(self) -> None:
        parsed = appendix_a.parse_appendix_a(self.write(SAMPLE))
        entry, is_approximate = appendix_a.find_entry(parsed, "Transit.ListKeys")
        self.assertIsNotNone(entry)
        self.assertFalse(is_approximate)
        self.assertEqual("S", entry.level)

    def test_placeholder_field_matches_approximately_by_prefix(self) -> None:
        parsed = appendix_a.parse_appendix_a(self.write(SAMPLE))
        self.assertNotIn("Auth.AppId.Admin.ReadField", parsed.entries)
        entry, is_approximate = appendix_a.find_entry(parsed, "Auth.AppId.Admin.ReadField")
        self.assertIsNotNone(entry)
        self.assertTrue(is_approximate)
        self.assertEqual("X", entry.level)

    def test_prose_only_table_and_excluded_section_are_not_indexed(self) -> None:
        parsed = appendix_a.parse_appendix_a(self.write(SAMPLE))
        self.assertNotIn("LDAP", parsed.entries)
        entry, is_approximate = appendix_a.find_entry(parsed, "sys/leases/*")
        self.assertIsNone(entry)
        self.assertFalse(is_approximate)

    def test_unknown_name_reports_no_match_rather_than_a_nearest_guess(self) -> None:
        parsed = appendix_a.parse_appendix_a(self.write(SAMPLE))
        entry, is_approximate = appendix_a.find_entry(parsed, "Totally.Unknown.Operation")
        self.assertIsNone(entry)
        self.assertFalse(is_approximate)

    def test_real_appendix_a_parses_without_error_and_finds_known_names(self) -> None:
        real_appendix = REPO_ROOT / "specifications" / "appendix-a-endpoint-catalogue.md"
        if not real_appendix.is_file():
            self.skipTest("appendix-a-endpoint-catalogue.md not present in this checkout")
        parsed = appendix_a.parse_appendix_a(real_appendix)
        entry, is_approximate = appendix_a.find_entry(parsed, "Kv.V2.ReadSecret")
        self.assertIsNotNone(entry)
        self.assertFalse(is_approximate)
        self.assertEqual("C", entry.level)


if __name__ == "__main__":
    unittest.main()
