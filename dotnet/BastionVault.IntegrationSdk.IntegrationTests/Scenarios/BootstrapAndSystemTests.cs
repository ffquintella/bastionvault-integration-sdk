using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.Scenarios;

/// <summary>ITG-S01: Sys.Health/Sys.SealStatus/Sys.ServerInfo (15-testing-requirements.md:182-184).</summary>
/// <remarks>Open question: no public Client.ServerVersion() exists to exercise the caching half.</remarks>
public sealed class Scenario01_BootstrapHealth : IntegrationTest
{
    [IntegrationFact]
    public async Task Health_seal_status_and_server_info_match_the_live_node()
    {
        HealthStatus health = await Client.Sys.HealthAsync();
        Assert.Equal(HealthState.Active, health.State);
        Assert.Equal(200, health.StatusCode);

        SealStatus seal = await Client.Sys.SealStatusAsync();
        Assert.False(seal.Sealed);

        // Unauthenticated: RequestOptions.Token = SecretString.Empty sends no token header at all
        // (RequestExecutor.ResolveTokenAsync honours options.Token first).
        ServerInfo anonymous = await Client.Sys.ServerInfoAsync(new RequestOptions { Token = SecretString.Empty });
        Assert.Null(anonymous.Version);
        Assert.True(anonymous.Initialized);

        ServerInfo authenticated = await Client.Sys.ServerInfoAsync();
        Assert.False(string.IsNullOrWhiteSpace(authenticated.Version));
    }
}

/// <summary>ITG-S02: seal/unseal (serial, managed mode only - 15-testing-requirements.md:185-187).</summary>
public sealed class Scenario02_SealUnseal : SerialIntegrationTest
{
    [IntegrationFact]
    public async Task Seal_then_unseal_round_trips_and_a_second_unseal_is_a_no_op()
    {
        Skip.If(Server.Mode != TestServerMode.Managed, "ITG-S02 is managed-mode only: it seals the server, and only a managed run owns unseal keys to reopen it.");
        Skip.If(Server.UnsealKeys.Count == 0, "no unseal keys available for this run");

        string mount = await Resources.MountAsync("kv-v2", "scratch");

        await Client.Sys.SealAsync();
        HealthStatus sealedHealth = await Client.Sys.HealthAsync();
        Assert.True(sealedHealth.Sealed);
        Assert.Equal(HealthState.Sealed, sealedHealth.State);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Kv.V2.ReadSecretAsync("any", mount));
        Assert.Equal(ErrorCodes.ServerSealed, failure.Code);

        SealStatus reopened = await Client.Sys.UnsealAsync(Server.UnsealKeys[0].Reveal()!);
        Assert.False(reopened.Sealed);

        HealthStatus activeHealth = await Client.Sys.HealthAsync();
        Assert.Equal(HealthState.Active, activeHealth.State);

        // Unseal on an already-unsealed vault is a no-op 200, not an error.
        SealStatus noOp = await Client.Sys.UnsealAsync(Server.UnsealKeys[0].Reveal()!);
        Assert.False(noOp.Sealed);
    }
}

/// <summary>ITG-S03: mount lifecycle (15-testing-requirements.md:188-190).</summary>
public sealed class Scenario03_MountLifecycle : IntegrationTest
{
    [IntegrationFact]
    public async Task Mount_appears_remounts_and_unmounts()
    {
        // Mounted and tracked by hand rather than through Resources.MountAsync: bvault 0.44.5
        // answers an unmount of an already-gone path with a 500 ("Mount not match"), not a 404
        // (observed against the live server), so ResourceLedger's 404-is-clean rule does not cover
        // it. Remounting invalidates a Track closed over the original path, and this scenario
        // remounts by design, so `live` is the one mutable indirection the teardown closure reads.
        string path = Unique("scratch");
        string? live = path;
        Resources.Track("mount", "lifecycle-scratch", async ct =>
        {
            if (live is { } stillMounted)
            {
                await Client.Sys.UnmountAsync(stillMounted, cancellationToken: ct).ConfigureAwait(false);
            }
        });

        await Client.Sys.MountAsync(path, new MountRequest { Type = "kv-v2", Description = "scenario03 scratch" });
        Context.Capture.Sections.RegisterMount(path, "kv-v2");

        IReadOnlyDictionary<string, MountInfo> mounts = await Client.Sys.ListMountsAsync();
        Assert.True(mounts.TryGetValue(path + "/", out MountInfo? info) || mounts.TryGetValue(path, out info));
        Assert.NotNull(info);
        Assert.Equal("kv-v2", info!.Type);
        Assert.False(string.IsNullOrEmpty(info.Description));

        string newPath = Unique("scratch-remounted");
        await Client.Sys.RemountAsync(path, newPath);
        live = newPath;
        Context.Capture.Sections.RegisterMount(newPath, "kv-v2");

        IReadOnlyDictionary<string, MountInfo> afterRemount = await Client.Sys.ListMountsAsync();
        Assert.DoesNotContain(afterRemount, m => m.Key.TrimEnd('/') == path);
        Assert.Contains(afterRemount, m => m.Key.TrimEnd('/') == newPath);

        string collidingPath = await Resources.MountAsync("kv-v2", "colliding");
        BastionVaultException conflict = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Sys.RemountAsync(newPath, collidingPath));
        Assert.Equal(ErrorCodes.ConflictMountPathInUse, conflict.Code);

        await Client.Sys.UnmountAsync(newPath);
        live = null;
        IReadOnlyDictionary<string, MountInfo> afterUnmount = await Client.Sys.ListMountsAsync();
        Assert.DoesNotContain(afterUnmount, m => m.Key.TrimEnd('/') == newPath);
    }
}

/// <summary>ITG-S04: policy lifecycle (15-testing-requirements.md:191-193).</summary>
public sealed class Scenario04_PolicyLifecycle : IntegrationTest
{
    [IntegrationFact]
    public async Task Write_read_list_history_and_delete_round_trip_and_legacy_path_matches()
    {
        string name = await Resources.WritePolicyAsync("scratch", """
            path "secret/data/scratch/*" {
              capabilities = ["read", "list"]
            }
            """);

        Policy? read = await Client.Sys.ReadPolicyAsync(name);
        Assert.NotNull(read);
        Assert.Contains("scratch", read!.Hcl, StringComparison.Ordinal);

        IReadOnlyList<string> names = await Client.Sys.ListPoliciesAsync();
        Assert.Contains(name, names);

        // bvault 0.44.5 reports the first history entry's op as "create", not "write" -
        // observed against the live server, not assumed.
        IReadOnlyList<PolicyHistoryEntry> history = await Client.Sys.PolicyHistoryAsync(name);
        PolicyHistoryEntry entry = Assert.Single(history);
        Assert.True(
            string.Equals(entry.Op, "create", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Op, "write", StringComparison.OrdinalIgnoreCase),
            $"expected a create/write history entry, got '{entry.Op}'");

        Policy? legacy = await Client.Sys.Legacy.ReadPolicyAsync(name);
        Assert.NotNull(legacy);
        Assert.Equal(read.Hcl, legacy!.Hcl);

        await Client.Sys.DeletePolicyAsync(name);

        // ReadPolicyAsync's own doc comment records the design: BV-NOTFOUND-005 is turned into a
        // null result here rather than raised, because "not there" is an answer for a reader.
        Policy? afterDelete = await Client.Sys.ReadPolicyAsync(name);
        Assert.Null(afterDelete);

        // Deleted; nothing left for teardown to clean up.
        Resources.Track("policy-already-deleted", name, _ => Task.CompletedTask);
    }
}

/// <summary>ITG-S05: Sys.CapabilitiesSelf (15-testing-requirements.md:194-195).</summary>
public sealed class Scenario05_CapabilitiesSelf : IntegrationTest
{
    [IntegrationFact]
    public async Task Capabilities_self_reflects_the_restrictive_policy_and_deny_path()
    {
        string mount = await Resources.MountAsync("kv-v2", "scratch");
        string readPath = $"{mount}/data/allowed";
        string denyPath = $"{mount}/data/forbidden";

        string policyName = await Resources.WritePolicyAsync("restrictive", $$"""
            path "{{readPath}}" {
              capabilities = ["read"]
            }
            path "{{denyPath}}" {
              capabilities = ["deny"]
            }
            """);

        CreateTokenRequest request = new() { Policies = [policyName] };
        AuthInfo restricted = await Client.Auth.Token.CreateAsync(request);
        Resources.TrackToken("scoped", restricted.ClientToken);

        using BastionVaultClient scopedClient = Server.CreateClient(o => o.Token = restricted.ClientToken.Reveal());
        Capabilities capabilities = await scopedClient.Sys.CapabilitiesSelfAsync([readPath, denyPath]);

        Assert.True(capabilities.NamespaceOperable);
        Assert.Equal([Capability.Read], capabilities.ByPath[readPath]);
        Assert.Equal([Capability.Deny], capabilities.ByPath[denyPath]);
    }
}

/// <summary>ITG-S06: namespaces, Standard (15-testing-requirements.md:196-200).</summary>
public sealed class Scenario06_Namespaces : IntegrationTest
{
    [IntegrationFact]
    public async Task Namespace_lifecycle_pages_and_scopes_a_token()
    {
        string path = Unique("ns");

        NamespaceSpec spec = new()
        {
            ChildVisibleDefault = true,
            Quotas = new NamespaceQuotas { MaxMounts = 10 },
        };
        Namespace created = await Client.Sys.WriteNamespaceAsync(path, spec);
        _ = Resources.TrackNamespace(created.Path);

        Namespace? read = await Client.Sys.ReadNamespaceAsync(path);
        Assert.NotNull(read);
        Assert.Equal(created.Uuid, read!.Uuid);

        bool found = false;
        await foreach (KeyValuePair<string, Namespace> entry in Client.Sys.ListNamespacesInfoAllAsync())
        {
            if (entry.Key.TrimEnd('/') == path)
            {
                found = true;
            }
        }

        Assert.True(found);

        NamespacesSelf self = await Client.Sys.NamespacesSelfAsync();
        Assert.True(self.Root);
        Assert.Contains(self.Namespaces, n => n.TrimEnd('/') == path);

        await Client.Sys.DeleteNamespaceAsync(path);
        Resources.Track("namespace-already-deleted", path, _ => Task.CompletedTask);

        // The deny check needs a token bound to a namespace that is unrelated to the one it then
        // tries to operate in: observed against the live server, `namespace_operable` is true
        // whenever the active namespace is an ancestor of the token's own (root always qualifies),
        // so two sibling namespaces are what actually exercises the false case.
        string siblingA = Unique("ns-a");
        Namespace nsA = await Client.Sys.WriteNamespaceAsync(siblingA, new NamespaceSpec());
        _ = Resources.TrackNamespace(nsA.Path);

        string siblingB = Unique("ns-b");
        Namespace nsB = await Client.Sys.WriteNamespaceAsync(siblingB, new NamespaceSpec());
        _ = Resources.TrackNamespace(nsB.Path);

        CreateTokenRequest tokenRequest = new() { Policies = ["default"] };
        AuthInfo nsToken = await Client.Auth.Token.CreateAsync(
            tokenRequest, new RequestOptions { Namespace = siblingA });
        Resources.TrackToken("ns-scoped", nsToken.ClientToken);

        // The siblingA token reaches into siblingB: Client here is the root-namespace view, and
        // RequestOptions pins this one call's token and namespace without switching the view's own
        // (CFG-060).
        RequestOptions asChildTokenInSibling = new() { Token = nsToken.ClientToken, Namespace = siblingB };
        BastionVaultException denied = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Sys.ListMountsAsync(asChildTokenInSibling));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, denied.Code);

        Capabilities capabilities = await Client.Sys.CapabilitiesSelfAsync(["secret/data/x"], asChildTokenInSibling);
        Assert.False(capabilities.NamespaceOperable);
    }
}
