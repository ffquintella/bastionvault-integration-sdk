"""Synthetic tests for ``apisurface``'s ``PublicApiSurface.txt`` reader."""

from __future__ import annotations

import shutil
import sys
import tempfile
import unittest
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import apisurface  # noqa: E402

REPO_ROOT = TOOL_DIR.parents[1]


class ApiSurfaceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="apisurface-tests-"))

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def write(self, content: str) -> Path:
        path = self.root / "PublicApiSurface.txt"
        path.write_text(content, encoding="utf-8")
        return path

    def test_canonical_name_follows_the_navigation_chain_from_the_client(self) -> None:
        path = self.write(
            "BastionVault.IntegrationSdk.BastionVaultClient : property Kv : BastionVault.IntegrationSdk.KvOperations {get}\n"
            "BastionVault.IntegrationSdk.KvOperations : property V2 : BastionVault.IntegrationSdk.KvV2Operations {get}\n"
            "BastionVault.IntegrationSdk.KvV2Operations : method ReadSecretAsync(System.String path) -> System.Threading.Tasks.Task\n"
        )
        operations = apisurface.build_public_operations(path)
        self.assertEqual(1, len(operations))
        operation = operations[0]
        self.assertEqual(("Kv.V2.ReadSecret",), operation.canonical_names)

    def test_async_suffix_is_stripped_but_only_the_suffix(self) -> None:
        path = self.write(
            "BastionVault.IntegrationSdk.BastionVaultClient : property Logical : BastionVault.IntegrationSdk.LogicalOperations {get}\n"
            "BastionVault.IntegrationSdk.LogicalOperations : method ReadAsync(System.String path) -> System.Threading.Tasks.Task\n"
            "BastionVault.IntegrationSdk.LogicalOperations : method RawAsync(System.String method) -> System.Threading.Tasks.Task\n"
        )
        operations = apisurface.build_public_operations(path)
        names = {(o.method_name, o.canonical_names) for o in operations}
        self.assertIn(("ReadAsync", ("Logical.Read",)), names)
        self.assertIn(("RawAsync", ("Logical.Raw",)), names)

    def test_a_type_reachable_by_two_paths_reports_both_names(self) -> None:
        path = self.write(
            "BastionVault.IntegrationSdk.BastionVaultClient : property Auth : BastionVault.IntegrationSdk.AuthOperations {get}\n"
            "BastionVault.IntegrationSdk.AuthOperations : property Oidc : BastionVault.IntegrationSdk.OidcOperations {get}\n"
            "BastionVault.IntegrationSdk.AuthOperations : property Saml : BastionVault.IntegrationSdk.SamlOperations {get}\n"
            "BastionVault.IntegrationSdk.OidcOperations : property Admin : BastionVault.IntegrationSdk.AuthRoleAdminOperations {get}\n"
            "BastionVault.IntegrationSdk.SamlOperations : property Admin : BastionVault.IntegrationSdk.AuthRoleAdminOperations {get}\n"
            "BastionVault.IntegrationSdk.AuthRoleAdminOperations : method ReadRoleAsync(System.String name) -> System.Threading.Tasks.Task\n"
        )
        operations = apisurface.build_public_operations(path)
        self.assertEqual(1, len(operations))
        self.assertEqual(
            ("Auth.Oidc.Admin.ReadRole", "Auth.Saml.Admin.ReadRole"), operations[0].canonical_names
        )

    def test_non_operations_types_are_ignored(self) -> None:
        path = self.write(
            "BastionVault.IntegrationSdk.BastionVaultClientOptions : property Timeout : System.TimeSpan {get, set}\n"
        )
        operations = apisurface.build_public_operations(path)
        self.assertEqual([], operations)

    def test_matches_the_real_repository_public_api_surface_count(self) -> None:
        real_surface = REPO_ROOT / "dotnet" / "BastionVault.IntegrationSdk" / "PublicApiSurface.txt"
        if not real_surface.is_file():
            self.skipTest("PublicApiSurface.txt not present in this checkout")
        operations = apisurface.build_public_operations(real_surface)
        # Every operation this generator finds must be reachable from BastionVaultClient;
        # an operation with no canonical name at all means the navigation graph missed a
        # facade, which is a bug in this module, not an acceptable gap.
        unreachable = [op for op in operations if not op.canonical_names]
        self.assertEqual([], unreachable)
        self.assertEqual(481, len(operations))

    def test_client_root_methods_are_in_the_corpus_as_Client_dot_name(self) -> None:
        real_surface = REPO_ROOT / "dotnet" / "BastionVault.IntegrationSdk" / "PublicApiSurface.txt"
        if not real_surface.is_file():
            self.skipTest("PublicApiSurface.txt not present in this checkout")
        operations = apisurface.build_public_operations(real_surface)
        by_name = {
            op.method_name: op.canonical_names
            for op in operations
            if op.declaring_type == "BastionVaultClient"
        }
        self.assertEqual(
            {
                "ClearToken": ("Client.ClearToken",),
                "ConnectAsync": ("Client.Connect",),
                "DiscoverAsync": ("Client.Discover",),
                "Dispose": ("Client.Dispose",),
                "ReconnectAsync": ("Client.Reconnect",),
                "ServerVersionAsync": ("Client.ServerVersion",),
                "SetToken": ("Client.SetToken",),
                "WithNamespace": ("Client.WithNamespace",),
            },
            by_name,
        )

    def test_root_method_reachable_directly_not_through_navigation(self) -> None:
        path = self.write(
            "BastionVault.IntegrationSdk.BastionVaultClient : method ConnectAsync([opt] System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.Task\n"
        )
        operations = apisurface.build_public_operations(path)
        self.assertEqual(1, len(operations))
        self.assertEqual("BastionVaultClient", operations[0].declaring_type)
        self.assertEqual(("Client.Connect",), operations[0].canonical_names)

    def test_unenumerated_facade_navigation_property_raises(self) -> None:
        path = self.write(
            "BastionVault.IntegrationSdk.BastionVaultClient : property Kv : BastionVault.IntegrationSdk.KvOperations {get}\n"
            "BastionVault.IntegrationSdk.KvOperations : property Widget : BastionVault.IntegrationSdk.WidgetHolder {get}\n"
            "BastionVault.IntegrationSdk.KvOperations : method ReadSecretAsync(System.String path) -> System.Threading.Tasks.Task\n"
        )
        with self.assertRaises(ValueError) as excinfo:
            apisurface.build_public_operations(path)
        self.assertIn("KvOperations.Widget", str(excinfo.exception))

    def test_enumerated_non_navigation_property_does_not_raise(self) -> None:
        path = self.write(
            "BastionVault.IntegrationSdk.BastionVaultClient : property Auth : BastionVault.IntegrationSdk.AuthOperations {get}\n"
            "BastionVault.IntegrationSdk.BastionVaultClient : property Config : BastionVault.IntegrationSdk.ClientConfig {get}\n"
            "BastionVault.IntegrationSdk.AuthOperations : property CurrentToken : BastionVault.IntegrationSdk.SecretString {get}\n"
            "BastionVault.IntegrationSdk.AuthOperations : method LoginAsync(System.String user) -> System.Threading.Tasks.Task\n"
        )
        operations = apisurface.build_public_operations(path)
        self.assertEqual(1, len(operations))
        self.assertEqual(("Auth.Login",), operations[0].canonical_names)


if __name__ == "__main__":
    unittest.main()
