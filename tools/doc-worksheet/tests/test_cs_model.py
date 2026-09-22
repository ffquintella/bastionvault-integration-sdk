"""Synthetic tests for ``cs_model``'s C# scanner.

Runnable directly as ``python tools/doc-worksheet/tests/test_cs_model.py``: the tool's
own directory is put on ``sys.path`` (the directory has a hyphen, so it is not an
importable package name), matching ``tools/error-catalogue/tests``' convention.
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

import cs_model  # noqa: E402


class CsModelTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="cs-model-tests-"))

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def write(self, relative: str, content: str) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def test_multi_line_signature_is_parsed(self) -> None:
        path = self.write(
            "Foo.cs",
            """\
namespace N;

public sealed class FooOperations
{
    /// <summary>Reads something.</summary>
    /// <spec>Foo.Read — ABC-001</spec>
    public async Task<string?> ReadAsync(
        string path,
        string mount = "default",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await logical.ExecuteShapedAsync(
            "GET", path, null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }
}
""",
        )
        classes = cs_model.parse_file(path, self.root)
        self.assertEqual(1, len(classes))
        methods = classes[0].methods["ReadAsync"]
        self.assertEqual(1, len(methods))
        method = methods[0]
        self.assertTrue(method.is_public)
        self.assertEqual(["path", "mount", "options", "cancellationToken"], method.params)
        self.assertIn("ExecuteShapedAsync", method.body)
        self.assertIn("Foo.Read — ABC-001", method.doc_comment)

    def test_public_modifier_detected_regardless_of_other_modifiers(self) -> None:
        path = self.write(
            "Bar.cs",
            """\
public sealed class BarOperations
{
    public static string Helper(string mount)
    {
        return mount;
    }

    private static string Internal(string mount)
    {
        return mount;
    }
}
""",
        )
        classes = cs_model.parse_file(path, self.root)
        methods = classes[0].methods
        self.assertTrue(methods["Helper"][0].is_public)
        self.assertFalse(methods["Internal"][0].is_public)

    def test_const_captures_raw_expression_not_just_literals(self) -> None:
        path = self.write(
            "Consts.cs",
            """\
public sealed class ConstOperations
{
    private const string Root = "identity/group";
    private const string SelfPath = Root + "/self";
}
""",
        )
        classes = cs_model.parse_file(path, self.root)
        consts = classes[0].consts
        self.assertEqual('"identity/group"', consts["Root"])
        self.assertEqual('Root + "/self"', consts["SelfPath"])

    def test_readonly_field_type_is_captured(self) -> None:
        path = self.write(
            "Fields.cs",
            """\
public sealed class FieldOperations
{
    private readonly LoginRunner runner;
    private readonly ClientContext context;
}
""",
        )
        classes = cs_model.parse_file(path, self.root)
        self.assertEqual({"runner": "LoginRunner", "context": "ClientContext"}, classes[0].fields)

    def test_expression_bodied_property_is_a_navigation_edge_candidate(self) -> None:
        path = self.write(
            "Nav.cs",
            """\
public sealed class RootOperations
{
    public ChildOperations Child => new(context, activeNamespace);
}
""",
        )
        classes = cs_model.parse_file(path, self.root)
        self.assertIn("Child", classes[0].methods)
        self.assertEqual("property", classes[0].methods["Child"][0].kind)

    def test_build_index_covers_multiple_files(self) -> None:
        self.write("A.cs", "public sealed class AOperations\n{\n    public void Noop() {}\n}\n")
        self.write(
            "Sub/B.cs", "public sealed class BOperations\n{\n    public void Noop() {}\n}\n"
        )
        index = cs_model.build_index(self.root, ["."])
        self.assertIn("AOperations", index)
        self.assertIn("BOperations", index)


if __name__ == "__main__":
    unittest.main()
