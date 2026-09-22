"""Synthetic tests for ``resolve``'s HTTP verb/path resolver.

Covers, per the brief's acceptance criteria: an operation whose verb/path resolve
cleanly, one that cannot be resolved (asserting a reason is reported, never a guess),
and the call-graph/ternary/const machinery in between.
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
import resolve  # noqa: E402


class ResolveTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="resolve-tests-"))

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def write(self, relative: str, content: str) -> Path:
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        return path

    def build(self, source: str) -> dict[str, list[cs_model.ClassInfo]]:
        self.write("Ops.cs", source)
        return cs_model.build_index(self.root, ["."])

    def operation(self, index: dict[str, list[cs_model.ClassInfo]], class_name: str, method_name: str):
        for class_info in index[class_name]:
            if method_name in class_info.methods:
                return class_info.methods[method_name][0]
        raise AssertionError(f"{class_name}.{method_name} not found")

    # -- clean resolution ------------------------------------------------

    def test_literal_verb_and_interpolated_path_resolve_cleanly(self) -> None:
        index = self.build(
            """\
public sealed class KeysOperations
{
    public async Task<string> ReadKeyAsync(string name, string mount = "transit")
    {
        return await logical.ExecuteShapedAsync(
            "GET", $"{mount}/keys/{name}", null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "KeysOperations", "ReadKeyAsync"), index)
        self.assertEqual("GET", route.verb)
        self.assertEqual("{mount}/keys/{name}", route.path_template)
        self.assertEqual(1, route.call_site_count)
        self.assertIsNone(route.path_reason)

    def test_resolves_through_a_private_helper_and_a_shared_wire_class(self) -> None:
        index = self.build(
            """\
public sealed class KvOperations
{
    private const string DataGroup = "data";

    public async Task<string> ReadAsync(string path, string mount = "secret")
    {
        return await logical.ExecuteShapedAsync(
            "GET", KvWire.EncodedRoute(mount, DataGroup, path), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }
}

internal static class KvWire
{
    public static string EncodedRoute(string mount, string group, string path)
    {
        return group.Length == 0 ? $"{mount}/{path}" : $"{mount}/{group}/{path}";
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "KvOperations", "ReadAsync"), index)
        self.assertEqual("GET", route.verb)
        self.assertEqual("{mount}/data/{path}", route.path_template)

    # -- honest non-answers ------------------------------------------------

    def test_dynamic_stringbuilder_path_is_unresolved_not_guessed(self) -> None:
        index = self.build(
            """\
public sealed class QueryOperations
{
    public async Task<string> SearchAsync(string mount, int? limit)
    {
        return await logical.ExecuteShapedAsync(
            "GET", BuildRoute(mount, limit), null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildRoute(string mount, int? limit)
    {
        StringBuilder route = new(mount);
        if (limit is { } value)
        {
            route.Append("?limit=").Append(value);
        }

        return route.ToString();
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "QueryOperations", "SearchAsync"), index)
        self.assertIsNone(route.path_template)
        self.assertIsNotNone(route.path_reason)
        self.assertIn("return", route.path_reason)

    def test_no_execute_call_is_reported_as_client_side_helper_not_a_guess(self) -> None:
        index = self.build(
            """\
public sealed class PathOperations
{
    public static string DataPath(string path, string mount)
    {
        return $"{mount}/data/{path}";
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "PathOperations", "DataPath"), index)
        self.assertIsNone(route.verb)
        self.assertIsNone(route.path_template)
        self.assertEqual(0, route.call_site_count)
        self.assertIn("no HTTP call", route.path_reason)

    def test_caller_supplied_verb_is_reported_not_treated_as_unresolved(self) -> None:
        index = self.build(
            """\
public sealed class RawOperations
{
    public async Task<string> RawAsync(string method, string absolutePath)
    {
        return await logical.ExecuteShapedAsync(
            method, absolutePath, null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "RawOperations", "RawAsync"), index)
        self.assertIsNone(route.verb)
        self.assertIn("caller-supplied", route.verb_reason)
        self.assertEqual("{absolutePath}", route.path_template)

    def test_ternary_folds_when_condition_resolves_to_a_literal(self) -> None:
        index = self.build(
            """\
public sealed class GroupOperations
{
    private const string Group = "data";

    public async Task<string> ReadAsync(string mount, string path)
    {
        return await logical.ExecuteShapedAsync(
            "GET", Route(mount, path), null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }

    private static string Route(string mount, string path)
    {
        return Group.Length == 0 ? $"{mount}/{path}" : $"{mount}/{Group}/{path}";
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "GroupOperations", "ReadAsync"), index)
        self.assertEqual("{mount}/data/{path}", route.path_template)

    def test_ternary_on_a_runtime_dependent_condition_is_left_unresolved(self) -> None:
        index = self.build(
            """\
public sealed class PrefixOperations
{
    public async Task<string> ListAsync(string prefix, string mount)
    {
        return await logical.ExecuteShapedAsync(
            "LIST", Route(mount, prefix), null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }

    private static string Route(string mount, string prefix)
    {
        string trimmed = prefix.Trim('/');
        return trimmed.Length == 0 ? mount : $"{mount}/{trimmed}/";
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "PrefixOperations", "ListAsync"), index)
        self.assertEqual("LIST", route.verb)
        self.assertIsNone(route.path_template)
        self.assertIn("not statically decidable", route.path_reason)

    def test_multiple_distinct_call_sites_are_reported_as_ambiguous(self) -> None:
        index = self.build(
            """\
public sealed class MultiOperations
{
    public async Task<string> ReadAsync(string mount, string path)
    {
        string existing = await CheckAsync(mount, path).ConfigureAwait(false);
        return await logical.ExecuteShapedAsync(
            "GET", $"{mount}/{path}", null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> CheckAsync(string mount, string path)
    {
        return await logical.ExecuteShapedAsync(
            "GET", $"{mount}/exists/{path}", null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "MultiOperations", "ReadAsync"), index)
        self.assertIsNone(route.verb)
        self.assertEqual("multiple distinct HTTP calls", route.reason)
        self.assertEqual(2, route.call_site_count)

    def test_catch_block_enrichment_call_does_not_count_as_ambiguous(self) -> None:
        index = self.build(
            """\
public sealed class ReadOperations
{
    public async Task<string> ReadAsync(string mount, string path)
    {
        try
        {
            return await logical.ExecuteShapedAsync(
                "GET", $"{mount}/{path}", null, options, defaultIdempotent: true,
                treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (failure.StatusCode == 404)
        {
            throw await EnrichAsync(mount, path).ConfigureAwait(false);
        }
    }

    private async Task<Exception> EnrichAsync(string mount, string path)
    {
        _ = await logical.ExecuteShapedAsync(
            "GET", "sys/mounts", null, options, defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return new Exception();
    }
}
"""
        )
        route = resolve.resolve_route(self.operation(index, "ReadOperations", "ReadAsync"), index)
        self.assertEqual("GET", route.verb)
        self.assertEqual("{mount}/{path}", route.path_template)
        self.assertEqual(1, route.call_site_count)


if __name__ == "__main__":
    unittest.main()
