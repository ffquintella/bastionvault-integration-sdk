"""Synthetic tests for the Appendix B parser, the §2 rule compiler and the emitters.

Mirrors ``tools/traceability/tests`` in shape: plain ``unittest``, synthetic inputs
written to a temporary directory, plus a handful of assertions against the real
appendix so a specification edit that the parser silently mis-reads shows up here
and not three languages downstream.

``tools/error-catalogue`` is not an importable package name (the directory has a
hyphen, and DR-0005 D-M1c-1 pins the path), so the modules are reached the same
way ``generate.py`` reaches them: by putting the tool's own directory on
``sys.path``.
"""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import captures  # noqa: E402
import catalogue  # noqa: E402
import emitters  # noqa: E402
import generate  # noqa: E402
from catalogue import CatalogueError  # noqa: E402

REPO_ROOT = TOOL_DIR.parents[1]
APPENDIX_B = REPO_ROOT / "specifications" / "appendix-b-error-catalogue.md"


def appendix(codes_section: str, recognition_rows: str = "| exact | `permission denied` | BV-AUTHZ-001 |") -> str:
    return (
        "# Appendix B\n\n## 1. Codes\n\n"
        + codes_section
        + "\n## 2. Server-message recognition\n\n"
        + "| Match | Server text | Code |\n|-------|-------------|------|\n"
        + recognition_rows
        + "\n\n## 3. Invariants tested by every SDK\n\n- nothing\n"
    )


MINIMAL_CODES = """### Authorization (`BV-AUTHZ-*`) — R = no

| Code | Name | Message | Hint |
|------|------|---------|------|
| BV-AUTHZ-001 | PermissionDenied | Denied. | Check policies. |
"""

RETRYABLE_CODES = """### Transport (`BV-TRANSPORT-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-TRANSPORT-001 | ConnectionFailed | yes | Could not connect. | Check the address. |
| BV-TRANSPORT-002 | Timeout | yes | Timed out. | Raise the timeout. |
| BV-TRANSPORT-003 | TlsError | yes | TLS failed. | Provide a CA. |

### Server state (`BV-SERVER-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-SERVER-002 | Unavailable | yes | Unavailable. | Check the cluster. |
| BV-SERVER-003 | Standby | yes | Standby. | Reconnect. |

### Rate limit (`BV-RATE-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-RATE-002 | NamespaceRateQuotaExceeded | yes | Quota exceeded. | Slow down. |

### Discovery (`BV-DISCOVERY-*`)

| Code | Name | R | Message | Hint |
|------|------|---|---------|------|
| BV-DISCOVERY-003 | NodeUnavailable | yes | Node gone. | Reconnect. |
"""


class TemporaryAppendix(unittest.TestCase):
    def write(self, text: str) -> Path:
        directory = Path(tempfile.mkdtemp(prefix="error-catalogue-"))
        self.addCleanup(lambda: None)
        path = directory / "appendix-b-error-catalogue.md"
        path.write_text(text, encoding="utf-8")
        return path


class CodeTableParsing(TemporaryAppendix):
    def test_parses_both_table_shapes(self) -> None:
        codes = catalogue.parse_codes(appendix(MINIMAL_CODES + "\n" + RETRYABLE_CODES))

        self.assertEqual(
            [entry.code for entry in codes],
            [
                "BV-AUTHZ-001",
                "BV-TRANSPORT-001",
                "BV-TRANSPORT-002",
                "BV-TRANSPORT-003",
                "BV-SERVER-002",
                "BV-SERVER-003",
                "BV-RATE-002",
                "BV-DISCOVERY-003",
            ],
        )
        self.assertFalse(codes[0].retryable)
        self.assertTrue(codes[1].retryable)

    def test_a_malformed_row_is_fatal(self) -> None:
        broken = MINIMAL_CODES.replace(
            "| BV-AUTHZ-001 | PermissionDenied | Denied. | Check policies. |",
            "| BV-AUTHZ-001 | PermissionDenied | Denied. |",
        )
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(broken))
        self.assertIn("malformed row", str(raised.exception))

    def test_a_duplicate_code_is_fatal(self) -> None:
        doubled = MINIMAL_CODES + "| BV-AUTHZ-001 | Other | Other. | Other. |\n"
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(doubled))
        self.assertIn("duplicate code", str(raised.exception))

    def test_an_unknown_category_prefix_is_fatal(self) -> None:
        unknown = MINIMAL_CODES.replace("BV-AUTHZ", "BV-WIDGET")
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(unknown))
        self.assertIn("BV-WIDGET", str(raised.exception))

    def test_a_duplicate_message_is_fatal(self) -> None:
        # ERR-037: no two codes share a message.
        doubled = MINIMAL_CODES + "| BV-AUTHZ-002 | Other | Denied. | Other. |\n"
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(doubled))
        self.assertIn("ERR-037", str(raised.exception))

    def test_an_empty_hint_is_fatal(self) -> None:
        empty = MINIMAL_CODES.replace("| Check policies. |", "|  |")
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(empty))
        self.assertIn("ERR-037", str(raised.exception))

    def test_a_four_column_table_outside_an_R_is_no_section_is_fatal(self) -> None:
        loose = MINIMAL_CODES.replace(" — R = no", "")
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(loose))
        self.assertIn("R = no", str(raised.exception))

    def test_an_unrecognised_R_value_is_fatal(self) -> None:
        broken = RETRYABLE_CODES.replace("| yes | Could not connect.", "| maybe | Could not connect.")
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(broken))
        self.assertIn("'maybe'", str(raised.exception))

    def test_a_code_under_the_wrong_section_is_fatal(self) -> None:
        misfiled = MINIMAL_CODES + "| BV-KV-001 | SecretNotFound | Missing. | Check. |\n"
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_codes(appendix(misfiled))
        self.assertIn("BV-KV-001", str(raised.exception))


class RetryableCrossCheck(TemporaryAppendix):
    def test_the_R_column_must_equal_the_ERR_006_set(self) -> None:
        codes = catalogue.parse_codes(appendix(MINIMAL_CODES + "\n" + RETRYABLE_CODES))
        catalogue.check_retryable(codes)  # the honest set passes

        dropped = [entry for entry in codes if entry.code != "BV-RATE-002"]
        with self.assertRaises(CatalogueError) as raised:
            catalogue.check_retryable(dropped)
        self.assertIn("D-M1c-8", str(raised.exception))
        self.assertIn("BV-RATE-002", str(raised.exception))


class HintLength(TemporaryAppendix):
    def test_the_real_appendix_satisfies_ERR_031(self) -> None:
        codes = catalogue.parse(APPENDIX_B).codes

        self.assertEqual(0, len([e for e in codes if catalogue.sentence_count(e.hint) > 2]))
        # The row D-M1c-13 corrected, pinned so a later edit cannot quietly re-split it.
        rate = next(entry for entry in codes if entry.code == "BV-RATE-001")
        self.assertEqual(2, catalogue.sentence_count(rate.hint))

    def test_a_three_sentence_hint_is_fatal(self) -> None:
        wordy = MINIMAL_CODES.replace(
            "| Check policies. |", "| Check policies. Then check the namespace. Then retry. |"
        )
        # check_hint_length directly: parse() would trip the D-M1c-8 retryable cross-check first
        # on a minimal appendix that carries no retryable rows.
        with self.assertRaises(CatalogueError) as raised:
            catalogue.check_hint_length(catalogue.parse_codes(appendix(wordy)))
        self.assertIn("ERR-031", str(raised.exception))
        self.assertIn("BV-AUTHZ-001 (3)", str(raised.exception))

    def test_backticked_names_and_decimals_are_not_sentence_ends(self) -> None:
        self.assertEqual(1, catalogue.sentence_count("Use `Sys.Batch` or `*-info` pages."))
        self.assertEqual(1, catalogue.sentence_count("Keep bodies under 32.5 MiB."))
        self.assertEqual(2, catalogue.sentence_count("Do this. Then do that."))


class ConstantNaming(unittest.TestCase):
    def test_the_category_token_is_never_doubled(self) -> None:
        self.assertEqual("RateLimitedByDosGuard", catalogue.constant_name("BV-RATE", "RateLimitedByDosGuard"))
        self.assertEqual("RateNamespaceRateQuotaExceeded", catalogue.constant_name("BV-RATE", "NamespaceRateQuotaExceeded"))
        self.assertEqual("NotFoundPathNotFound", catalogue.constant_name("BV-NOTFOUND", "PathNotFound"))
        self.assertEqual("ConfigInvalidPem", catalogue.constant_name("BV-CONFIG", "InvalidPem"))

    def test_screaming_snake_case_is_the_same_identifier(self) -> None:
        self.assertEqual("RATE_NAMESPACE_RATE_QUOTA_EXCEEDED", catalogue.screaming_name("RateNamespaceRateQuotaExceeded"))
        self.assertEqual("KV_CAS_MISMATCH", catalogue.screaming_name("KvCasMismatch"))
        self.assertEqual("CONFIG_INVALID_PEM", catalogue.screaming_name("ConfigInvalidPem"))
        self.assertEqual("SSH_PQC_ONLY_CLASSICAL_CA", catalogue.screaming_name("SshPqcOnlyClassicalCa"))


class Normalisation(unittest.TestCase):
    def test_matching_time_normalisation(self) -> None:
        self.assertEqual("permission denied", catalogue.normalise("  Permission denied.  "))
        self.assertEqual("account temporarily locked", catalogue.normalise("Account temporarily locked (retry after 300s)."))
        self.assertEqual("account temporarily locked", catalogue.normalise("Account temporarily locked (RETRY AFTER 5 s)"))
        self.assertEqual("a. b", catalogue.normalise("A. b."))

    def test_a_rule_literal_keeps_its_significant_trailing_space(self) -> None:
        self.assertEqual("key ", catalogue.rule_literal("key "))
        self.assertEqual("no policy named", catalogue.rule_literal("No policy named"))


class ServerTextCompiler(unittest.TestCase):
    def compile(self, cell: str, kind: str = "prefix") -> list[tuple[str, str, tuple[str, ...]]]:
        """(kind, stem, qualifier group). The group is an alternation (D-M8-2)."""
        alternatives = catalogue.parse_server_text(cell, kind)
        # No Appendix B row expresses a genuine conjunction yet, so a parse that filled
        # `contains_all` would be R-23 returning under a different field name.
        self.assertEqual([[] for _ in alternatives], [a.contains_all for a in alternatives])
        return [
            (alternative.kind or kind, alternative.text, tuple(alternative.contains_any))
            for alternative in alternatives
        ]

    def test_a_qualifier_group_is_an_alternation_not_a_conjunction(self) -> None:
        """R-23/D-M8-2: the three tokens are alternatives, so no message carries two."""
        rules = catalogue.parse_recognition(
            APPENDIX_B.read_text(encoding="utf-8"),
            {entry.code for entry in catalogue.parse_codes(APPENDIX_B.read_text(encoding="utf-8"))},
        )
        backup = next(rule for rule in rules if rule.text == "backup")
        self.assertEqual((), backup.contains_all)
        self.assertEqual(("invalid magic", "unsupported version", "corrupted"), backup.contains_any)
        self.assertTrue(emitters.matches(backup, "backup corrupted", 500))
        self.assertFalse(emitters.matches(backup, "backup is fine", 500))

    def test_two_qualifier_groups_on_one_stem_are_refused(self) -> None:
        """A genuine conjunction needs `containsAll` support in three emitters first.

        Both raise sites are covered: the `+` form and the parenthesised form. Flattening
        either into one alternation would be R-23 with the operands swapped (D-M8-11).
        """
        for cell in ("`stem` + `a` + `b`", "`stem` + `a` (`b`)", "`stem` (`a`) (`b`)"):
            with self.subTest(cell=cell), self.assertRaises(catalogue.CatalogueError):
                catalogue.parse_server_text(cell, "prefix")

    def test_plain_alternatives(self) -> None:
        self.assertEqual(
            [("exact", "missing client token", ()), ("exact", "request client token is missing", ())],
            self.compile("`missing client token` / `request client token is missing`", "exact"),
        )

    def test_a_plus_qualifier_binds_only_to_the_last_alternative(self) -> None:
        # `backup hmac verification failed` stands alone; `backup` carries the three-way contains.
        self.assertEqual(
            [
                ("prefix", "backup hmac verification failed", ()),
                ("prefix", "backup", ("invalid magic", "unsupported version", "corrupted")),
            ],
            self.compile(
                "`backup hmac verification failed` / `backup` + `invalid magic`/`unsupported version`/`corrupted`"
            ),
        )

    def test_alternatives_after_a_plus_join_the_qualifier_list(self) -> None:
        self.assertEqual(
            [("prefix", "version ", ("is below min_decryption_version", "not found on key"))],
            self.compile("`version ` + contains `is below min_decryption_version` / `not found on key`"),
        )

    def test_a_parenthesised_literal_list_is_a_qualifier(self) -> None:
        self.assertEqual(
            [("prefix", "machine_token", ()), ("prefix", "machine ", ("is not bound", "is not approved"))],
            self.compile("`machine_token` / `machine ` (`is not bound`, `is not approved`)"),
        )

    def test_a_per_alternative_kind_word_and_status_guard(self) -> None:
        alternatives = catalogue.parse_server_text(
            "`bastionvault is sealed` / contains `is sealed` (5xx)", "exact"
        )
        self.assertEqual(["exact", "contains"], [alternative.kind for alternative in alternatives])
        self.assertIsNone(alternatives[0].guard)
        self.assertEqual(5, alternatives[1].guard.status_class)  # type: ignore[union-attr]

    def test_a_parenthesis_with_no_literal_and_no_status_is_a_scope_note(self) -> None:
        alternatives = catalogue.parse_server_text("`unknown role` (ssh mount)", "prefix")
        self.assertEqual("ssh mount", alternatives[0].scope_note)

    def test_slashes_inside_a_literal_are_not_alternative_separators(self) -> None:
        self.assertEqual(
            [("contains", "does not support /", ()), ("contains", "do not support /", ())],
            self.compile("`does not support /` / `do not support /`", "contains"),
        )

    def test_an_unparsable_cell_is_fatal(self) -> None:
        with self.assertRaises(CatalogueError):
            catalogue.parse_server_text("no literal at all", "exact")


class RecognitionTable(TemporaryAppendix):
    def test_a_row_naming_an_undefined_code_is_fatal(self) -> None:
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_recognition(
                appendix(MINIMAL_CODES, "| exact | `nope` | BV-AUTHZ-999 |"), {"BV-AUTHZ-001"}
            )
        self.assertIn("BV-AUTHZ-999", str(raised.exception))

    def test_a_code_range_expands_one_code_per_alternative(self) -> None:
        rules = catalogue.parse_recognition(
            appendix(MINIMAL_CODES, "| exact | `a` / `b` / `c` | BV-AUTHZ-001…003 respectively |"),
            {"BV-AUTHZ-001", "BV-AUTHZ-002", "BV-AUTHZ-003"},
        )
        self.assertEqual(["BV-AUTHZ-001", "BV-AUTHZ-002", "BV-AUTHZ-003"], [rule.code for rule in rules])

    def test_a_code_range_of_the_wrong_length_is_fatal(self) -> None:
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_recognition(
                appendix(MINIMAL_CODES, "| exact | `a` / `b` | BV-AUTHZ-001…003 respectively |"),
                {"BV-AUTHZ-001", "BV-AUTHZ-002", "BV-AUTHZ-003"},
            )
        self.assertIn("3 codes for 2 alternatives", str(raised.exception))

    def test_a_details_annotation_is_carried_onto_the_rule(self) -> None:
        rules = catalogue.parse_recognition(
            appendix(MINIMAL_CODES, "| prefix | `cannot assign policy` | BV-AUTHZ-001 (Details.policy) |"),
            {"BV-AUTHZ-001"},
        )
        self.assertEqual("policy", rules[0].details_key)

    def test_a_malformed_match_cell_is_fatal(self) -> None:
        with self.assertRaises(CatalogueError) as raised:
            catalogue.parse_recognition(
                appendix(MINIMAL_CODES, "| startswith | `a` | BV-AUTHZ-001 |"), {"BV-AUTHZ-001"}
            )
        self.assertIn("Match cell", str(raised.exception))

    def test_only_a_listed_scope_becomes_an_enforced_path_guard(self) -> None:
        rules = catalogue.parse_recognition(
            appendix(
                MINIMAL_CODES,
                "| contains (409, recordings) | `sha256` | BV-AUTHZ-001 |\n"
                "| prefix | `unknown role` (ssh mount) | BV-AUTHZ-001 |",
            ),
            {"BV-AUTHZ-001"},
        )
        self.assertEqual("recordings", rules[0].path_contains)
        self.assertEqual(409, rules[0].guard.status)  # type: ignore[union-attr]
        self.assertEqual("ssh mount", rules[1].scope_note)
        self.assertIsNone(rules[1].path_contains)


class HandAuthoredTables(unittest.TestCase):
    def setUp(self) -> None:
        self.catalogue = catalogue.parse(APPENDIX_B)

    def test_the_real_capture_table_cross_checks_clean(self) -> None:
        captures.check(self.catalogue.rules)

    def test_a_capture_whose_stem_no_rule_carries_is_fatal(self) -> None:
        stale = captures.Capture(stem="no such rule", kind="token_after_prefix", keys=("x",), sample="x")
        original = captures.DETAILS_CAPTURES
        captures.DETAILS_CAPTURES = original + (stale,)
        try:
            with self.assertRaises(CatalogueError) as raised:
                captures.check(self.catalogue.rules)
            self.assertIn("no such rule", str(raised.exception))
        finally:
            captures.DETAILS_CAPTURES = original

    def test_a_capture_on_a_non_prefix_rule_is_fatal(self) -> None:
        # `permission denied` is an `exact` row; a capture there would read from a fixed offset
        # that the runtime cannot justify.
        bad = captures.Capture(
            stem="permission denied", kind="token_after_prefix", keys=("x",), prefix="permission denied", sample="x"
        )
        original = captures.DETAILS_CAPTURES
        captures.DETAILS_CAPTURES = original + (bad,)
        try:
            with self.assertRaises(CatalogueError) as raised:
                captures.check(self.catalogue.rules)
            self.assertIn("must attach to a prefix rule", str(raised.exception))
        finally:
            captures.DETAILS_CAPTURES = original

    def test_an_unknown_capture_kind_is_fatal(self) -> None:
        bad = captures.Capture(stem="no policy named", kind="regex", keys=("x",), sample="x")
        original = captures.DETAILS_CAPTURES
        captures.DETAILS_CAPTURES = original + (bad,)
        try:
            with self.assertRaises(CatalogueError) as raised:
                captures.check(self.catalogue.rules)
            self.assertIn("unknown capture kind", str(raised.exception))
        finally:
            captures.DETAILS_CAPTURES = original


class RealAppendix(unittest.TestCase):
    def setUp(self) -> None:
        self.catalogue = catalogue.parse(APPENDIX_B)

    def test_every_section_of_the_real_appendix_parses(self) -> None:
        # 122 = 119 through M1c, plus the two codes DR-0006 D-M2-16 added for M2a
        # (BV-AUTH-017 TokenSourceFailed, BV-CONFIG-011 TokenFileNotWritable), plus
        # BV-DISCOVERY-004 StrictDiscoveryRefused, minted closing R-16 in 0.14.1. None of the
        # three adds a section-2 recognition rule, which is why the rule count is unchanged:
        # all are raised client-side, not recognised from a server message.
        self.assertEqual(122, len(self.catalogue.codes))
        self.assertEqual(127, len(self.catalogue.rules))

    def test_the_one_deliberate_rename_is_the_only_shipped_constant_that_moves(self) -> None:
        constants = {entry.code: entry.constant for entry in self.catalogue.codes}
        self.assertEqual("RateNamespaceRateQuotaExceeded", constants["BV-RATE-002"])
        self.assertEqual("RateLimitedByDosGuard", constants["BV-RATE-001"])
        self.assertEqual("QuotaNamespaceQuotaExceeded", constants["BV-QUOTA-001"])

    def test_rule_order_is_appendix_order(self) -> None:
        rows = [rule.row for rule in self.catalogue.rules]
        self.assertEqual(rows, sorted(rows))
        self.assertEqual("permission denied", self.catalogue.rules[0].text)
        self.assertEqual("hmac verification failed", self.catalogue.rules[-1].text)

    def test_every_rule_names_a_defined_code(self) -> None:
        defined = {entry.code for entry in self.catalogue.codes}
        self.assertTrue({rule.code for rule in self.catalogue.rules} <= defined)

    def test_every_recognition_row_is_covered_by_at_least_one_rule(self) -> None:
        # Appendix B §3's fourth invariant, at the row level.
        rows = {rule.row for rule in self.catalogue.rules}
        self.assertEqual(set(range(1, max(rows) + 1)), rows)


class FirstMatchWins(unittest.TestCase):
    def setUp(self) -> None:
        self.catalogue = catalogue.parse(APPENDIX_B)

    def test_the_generator_refuses_a_fixture_an_earlier_row_would_win(self) -> None:
        documents = emitters.fixture_documents(self.catalogue)
        emitters.check_first_match(self.catalogue, documents)  # the honest set passes

        shadowed = json.loads(json.dumps(documents[0][1]))
        shadowed["exchanges"][0]["respond"]["body"]["error"] = "Permission denied."
        shadowed["exchanges"][0]["respond"]["status"] = 403
        with self.assertRaises(CatalogueError) as raised:
            emitters.check_first_match(self.catalogue, [("seeded", shadowed)])
        self.assertIn("first-match-wins", str(raised.exception))

    def test_the_generator_refuses_a_fixture_the_status_table_alone_would_pass(self) -> None:
        document = {
            "exchanges": [{"respond": {"status": 403, "body": {"error": "Permission denied."}}}],
            "expect": {"error": {"code": "BV-AUTHZ-001"}},
            "operation": {"args": {"path": "secret/data/x"}},
        }
        with self.assertRaises(CatalogueError) as raised:
            emitters.check_first_match(self.catalogue, [("seeded", document)])
        self.assertIn("status table alone", str(raised.exception))

    def test_a_fixture_no_rule_matches_is_fatal(self) -> None:
        document = {
            "exchanges": [{"respond": {"status": 400, "body": {"error": "nothing matches this"}}}],
            "expect": {"error": {"code": "BV-AUTHZ-001"}},
            "operation": {"args": {"path": "secret/data/x"}},
        }
        with self.assertRaises(CatalogueError) as raised:
            emitters.check_first_match(self.catalogue, [("seeded", document)])
        self.assertIn("matches no recognition rule", str(raised.exception))


class Emitters(unittest.TestCase):
    def setUp(self) -> None:
        self.catalogue = catalogue.parse(APPENDIX_B)
        self.files = emitters.artefacts(self.catalogue)

    def test_generation_is_deterministic(self) -> None:
        self.assertEqual(self.files, emitters.artefacts(catalogue.parse(APPENDIX_B)))

    def test_every_named_artefact_path_is_produced(self) -> None:
        for path in (emitters.DOTNET_PATH, emitters.RUST_PATH, emitters.PYTHON_PATH, "tools/error-catalogue/catalogue.json"):
            self.assertIn(path, self.files)

    def test_the_checked_in_artefacts_are_up_to_date(self) -> None:
        # The same property repo-gates.yml proves with `git diff --exit-code`; asserted here too so
        # a local run catches it before CI does.
        self.assertEqual(0, generate.run(REPO_ROOT, check=True))

    def test_the_intermediate_records_what_the_emitters_consumed(self) -> None:
        payload = json.loads(self.files["tools/error-catalogue/catalogue.json"])
        self.assertEqual(len(self.catalogue.codes), len(payload["codes"]))
        self.assertEqual(len(self.catalogue.rules), len(payload["recognition"]))
        self.assertEqual(len(captures.DETAILS_CAPTURES), len(payload["detailsCaptures"]))

    def test_dotnet_emits_one_constant_per_code_and_one_rule_per_row(self) -> None:
        source = self.files[emitters.DOTNET_PATH]
        self.assertEqual(len(self.catalogue.codes), source.count("public const string "))
        self.assertEqual(len(self.catalogue.rules), source.count("new(RecognitionKind."))
        self.assertIn("public const string RateNamespaceRateQuotaExceeded", source)

    def test_rust_and_python_emit_the_same_row_counts(self) -> None:
        rust = self.files[emitters.RUST_PATH]
        python = self.files[emitters.PYTHON_PATH]
        self.assertEqual(len(self.catalogue.codes), rust.count("pub const "))
        self.assertEqual(len(self.catalogue.codes), python.count(": Final[str] = "))

    def test_no_emitted_python_line_exceeds_the_ruff_line_length(self) -> None:
        for line in self.files[emitters.PYTHON_PATH].splitlines():
            self.assertLessEqual(len(line), 110, line)

    def test_one_fixture_per_rule_alternative_except_the_rows_already_on_disk(self) -> None:
        """One fixture per *alternative*, not per rule (D-M8-3).

        A rule with a qualifier group needs one fixture per alternative: a single
        fixture carrying every alternative at once passes under the conjunctive
        reading too, so it would certify R-23 rather than catch it.
        """
        fixtures = [name for name in self.files if name.startswith(emitters.FIXTURE_DIR)]
        expected = sum(
            max(len(rule.contains_any), 1)
            for rule in self.catalogue.rules
            if (rule.kind, rule.text) not in captures.ALREADY_FIXTURED
        )
        self.assertEqual(expected, len(fixtures))

    def test_every_qualifier_alternative_gets_a_fixture_of_its_own(self) -> None:
        """D-M8-3: and no generated fixture carries two alternatives of one group."""
        documents = dict(emitters.fixture_documents(self.catalogue))
        for rule in self.catalogue.rules:
            if len(rule.contains_any) < 2:
                continue
            messages = [
                document["exchanges"][0]["respond"]["body"]["error"]
                for document in documents.values()
                if document["expect"]["error"]["code"] == rule.code
            ]
            for alternative in rule.contains_any:
                hits = [message for message in messages if alternative in message]
                self.assertTrue(hits, f"{rule.code}: no fixture exercises {alternative!r}")
                for message in hits:
                    others = [other for other in rule.contains_any if other != alternative]
                    self.assertFalse(
                        [other for other in others if other in message],
                        f"{rule.code}: fixture message {message!r} carries two alternatives",
                    )

    def test_every_generated_fixture_is_a_valid_document(self) -> None:
        for name, content in self.files.items():
            if not name.startswith(emitters.FIXTURE_DIR):
                continue
            document = json.loads(content)
            self.assertEqual(Path(name).stem, document["id"])
            self.assertEqual("core", document["level"])
            self.assertIn(document["operation"]["name"], ("Logical.Read", "Logical.Write"))
            self.assertIn("ERR-020", document["requirements"])


if __name__ == "__main__":
    unittest.main()
