using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M3's assertions not expressible as a single-threaded wire-shape fixture (DR-0007): the
/// closed-status-set contract of <c>Sys.Health</c> outside its four documented statuses, the
/// client-side <c>BV-INPUT-002</c> refusal on an empty <c>paths</c> argument, the optional-field
/// absence on <c>ServerInfo</c>/<c>ClusterStatus</c>, <c>Capability</c>'s open-enum shape (SYS-051)
/// and <c>Capabilities.Can</c>'s evaluation rules (SYS-053).
/// </summary>
public sealed class SysUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    [Requirement("SYS-001")]
    [Trait("Requirement", "SYS-001")]
    public async Task Health_falls_through_to_the_ordinary_mapping_for_a_status_outside_the_closed_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"errors":["internal"]}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.HealthAsync());

        Assert.Equal(ErrorCodes.ServerInternalError, exception.Code);
        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    [Requirement("SYS-001")]
    [Trait("Requirement", "SYS-001")]
    public async Task Health_raises_BV_PROTOCOL_002_for_a_non_JSON_body_even_within_the_closed_status_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("not json"));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.HealthAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SYS-001")]
    [Trait("Requirement", "SYS-001")]
    public async Task Health_raises_BV_PROTOCOL_002_when_the_transport_yields_no_body_at_all()
    {
        // 304 is the one status TryHandleResponse answers with a successful, bodyless Outcome
        // (D-M1b-10); Sys.Health has no conditional-GET caller, so this is a defensive arm rather
        // than a scenario the server actually produces, exercised here rather than left
        // unreachable-and-untested.
        FakeTransport transport = new();
        transport.EnqueueResponse(304);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.HealthAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SYS-001")]
    [Trait("Requirement", "SYS-001")]
    public async Task Health_classifies_a_missing_cluster_healthy_field_as_false_and_Unhealthy()
    {
        // "cluster_healthy" is absent entirely, covering ReadBool(JsonElement,...)'s
        // missing-property arm alongside SYS-001's Unhealthy row (sealed == false, cluster
        // unhealthy, not standby).
        FakeTransport transport = new();
        transport.EnqueueResponse(503, body: Json("""{"initialized":true,"sealed":false,"standby":false}"""));
        BastionVaultClient client = BuildClient(transport);

        HealthStatus health = await client.Sys.HealthAsync();

        Assert.Equal(HealthState.Unhealthy, health.State);
        Assert.False(health.ClusterHealthy);
    }

    [Fact]
    [Requirement("SYS-001")]
    [Trait("Requirement", "SYS-001")]
    public async Task Health_standby_429_leaves_the_rate_gate_unpaused()
    {
        // sys/health is DoS-guard-exempt (06-system-api.md:15) and its 429 is a normal Standby
        // outcome under SYS-001, not a real rate-limit signal — unlike every other operation's
        // 429, this one must not leave BastionVaultClient.RateGateState reporting Paused.
        FakeTransport transport = new();
        transport.EnqueueResponse(429, body: Json("""{"initialized":true,"sealed":false,"standby":true,"cluster_healthy":true}"""));
        BastionVaultClient client = BuildClient(transport);

        HealthStatus health = await client.Sys.HealthAsync();

        Assert.Equal(HealthState.Standby, health.State);
        Assert.Equal(429, health.StatusCode);
        Assert.False(client.RateGateState.Paused);
    }

    [Fact]
    [Requirement("SYS-005")]
    [Trait("Requirement", "SYS-005")]
    public async Task SealStatus_raises_BV_PROTOCOL_002_on_an_empty_envelope()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.SealStatusAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SYS-005")]
    [Trait("Requirement", "SYS-005")]
    public async Task SealStatus_defaults_T_and_N_to_zero_when_the_server_omits_them()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"sealed":false}"""));
        BastionVaultClient client = BuildClient(transport);

        SealStatus status = await client.Sys.SealStatusAsync();

        Assert.Equal(0, status.T);
        Assert.Equal(0, status.N);
        Assert.Equal(0, status.KeyShares);
        Assert.Equal(0, status.KeyThreshold);
        Assert.Equal(0, status.Progress);
    }

    [Fact]
    [Requirement("SYS-008")]
    [Trait("Requirement", "SYS-008")]
    public async Task ServerInfo_anonymous_tier_leaves_the_authenticated_fields_null_rather_than_defaulted()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"initialized":true,"sealed":false}"""));
        BastionVaultClient client = BuildClient(transport);

        ServerInfo info = await client.Sys.ServerInfoAsync();

        Assert.True(info.Initialized);
        Assert.False(info.Sealed);
        Assert.Null(info.Version);
        Assert.Null(info.StartedAt);
        Assert.Null(info.UptimeSeconds);
        Assert.Null(info.StorageType);
    }

    [Fact]
    [Requirement("SYS-008")]
    [Trait("Requirement", "SYS-008")]
    public async Task ServerInfo_raises_BV_PROTOCOL_002_on_an_empty_envelope()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ServerInfoAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SYS-008")]
    [Trait("Requirement", "SYS-008")]
    public async Task ServerInfo_ignores_an_unparsable_started_at_rather_than_throwing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"initialized":true,"sealed":false,"started_at":"not-a-date"}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        ServerInfo info = await client.Sys.ServerInfoAsync();

        Assert.Null(info.StartedAt);
    }

    [Fact]
    [Requirement("SYS-006")]
    [Trait("Requirement", "SYS-006")]
    public async Task ClusterStatus_requires_a_live_token_client_side()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ClusterStatusAsync());

        Assert.Equal(ErrorCodes.AuthNoToken, exception.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-006")]
    [Trait("Requirement", "SYS-006")]
    public async Task ClusterStatus_raises_BV_PROTOCOL_002_on_an_empty_envelope()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ClusterStatusAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SYS-006")]
    [Trait("Requirement", "SYS-006")]
    public async Task ClusterStatus_treats_a_wrongly_typed_is_leader_as_absent_rather_than_throwing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"storage_type":"raft","cluster":"c1","is_leader":"not-a-bool"}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        ClusterStatus status = await client.Sys.ClusterStatusAsync();

        Assert.Null(status.IsLeader);
    }

    [Fact]
    [Requirement("SYS-006")]
    [Trait("Requirement", "SYS-006")]
    public async Task ClusterStatus_on_a_non_clustered_backend_leaves_every_optional_field_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"storage_type":"file","cluster":"solo"}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        ClusterStatus status = await client.Sys.ClusterStatusAsync();

        Assert.Equal("file", status.StorageType);
        Assert.Equal("solo", status.Cluster);
        Assert.Null(status.NodeId);
        Assert.Null(status.IsLeader);
        Assert.Null(status.ClusterHealthy);
        Assert.Null(status.RaftMetrics);
    }

    [Fact]
    [Requirement("SYS-006")]
    [Trait("Requirement", "SYS-006")]
    public async Task ClusterStatus_reports_a_present_but_false_is_leader_and_cluster_healthy()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"storage_type":"raft","cluster":"c1","is_leader":false,"cluster_healthy":false}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        ClusterStatus status = await client.Sys.ClusterStatusAsync();

        Assert.False(status.IsLeader);
        Assert.False(status.ClusterHealthy);
    }

    [Fact]
    [Requirement("SYS-052")]
    [Trait("Requirement", "SYS-052")]
    public async Task CapabilitiesSelf_refuses_an_empty_paths_argument_before_any_request()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.CapabilitiesSelfAsync(Array.Empty<string>()));

        Assert.Equal(ErrorCodes.InputEmptyCollection, exception.Code);
        Assert.False(exception.Retryable);
        Assert.Equal(0, exception.Attempts);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-050")]
    [Requirement("SYS-051")]
    [Trait("Requirement", "SYS-051")]
    public async Task CapabilitiesSelf_preserves_an_unrecognised_wire_capability_as_Other()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"capabilities":{"secret/data/x":["read","future-verb"]},"namespace_operable":true,"token_namespace":"","active_namespace":""}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        Capabilities capabilities = await client.Sys.CapabilitiesSelfAsync(["secret/data/x"]);

        IReadOnlyList<Capability> granted = capabilities.ByPath["secret/data/x"];
        Assert.Contains(Capability.Read, granted);
        Capability other = Assert.Single(granted, capability => capability.IsOther);
        Assert.Equal("future-verb", other.WireValue);
        Assert.Equal("future-verb", other.ToString());
    }

    [Fact]
    [Requirement("SYS-050")]
    [Trait("Requirement", "SYS-050")]
    public async Task CapabilitiesSelf_defaults_to_an_empty_map_when_the_capabilities_key_is_absent()
    {
        FakeTransport transport = new();
        // "namespace_operable" is also absent, covering ReadBool(dict,...)'s missing-key arm
        // alongside ReadCapabilitiesMap's missing-"capabilities" arm.
        transport.EnqueueResponse(200, body: Json("""{"token_namespace":"","active_namespace":""}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        Capabilities capabilities = await client.Sys.CapabilitiesSelfAsync(["secret/data/x"]);

        Assert.Empty(capabilities.ByPath);
        Assert.False(capabilities.NamespaceOperable);
    }

    [Fact]
    [Requirement("SYS-050")]
    [Trait("Requirement", "SYS-050")]
    public async Task CapabilitiesSelf_treats_a_non_array_capability_value_as_empty()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"capabilities":{"secret/data/x":"not-an-array"},"namespace_operable":true,"token_namespace":"","active_namespace":""}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        Capabilities capabilities = await client.Sys.CapabilitiesSelfAsync(["secret/data/x"]);

        Assert.Empty(capabilities.ByPath["secret/data/x"]);
    }

    [Fact]
    [Requirement("SYS-050")]
    [Trait("Requirement", "SYS-050")]
    public async Task CapabilitiesSelf_raises_BV_PROTOCOL_002_on_an_empty_envelope()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.CapabilitiesSelfAsync(["secret/data/x"]));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("SYS-053")]
    [Trait("Requirement", "SYS-053")]
    public async Task Can_fetches_capabilities_self_and_delegates_to_Capabilities_Can()
    {
        // SYS-053 requires the spec-named Client.Sys.Can convenience (":9 — all operations live
        // under Client.Sys"), a thin one-round-trip wrapper over CapabilitiesSelfAsync plus the
        // no-round-trip Capabilities.Can evaluation already covered above.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"capabilities":{"secret/data/x":["read"]},"namespace_operable":true,"token_namespace":"","active_namespace":""}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        bool canRead = await client.Sys.CanAsync("secret/data/x", Capability.Read);

        Assert.True(canRead);
        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal("POST", request.Method);
    }

    [Fact]
    [Requirement("SYS-053")]
    [Trait("Requirement", "SYS-053")]
    public void Capabilities_Can_applies_deny_root_and_the_read_implies_connect_rule()
    {
        Capabilities capabilities = new()
        {
            ByPath = new Dictionary<string, IReadOnlyList<Capability>>(StringComparer.Ordinal)
            {
                ["denied"] = [Capability.Read, Capability.Deny],
                ["root"] = [Capability.Root],
                ["read-only"] = [Capability.Read],
                ["list-only"] = [Capability.List],
                ["empty"] = [],
            },
            NamespaceOperable = true,
            TokenNamespace = string.Empty,
            ActiveNamespace = string.Empty,
        };

        // Deny overrides every other entry on the same path.
        Assert.False(capabilities.Can("denied", Capability.Read));

        // Root implies every capability, including one the path never listed.
        Assert.True(capabilities.Can("root", Capability.Read));
        Assert.True(capabilities.Can("root", Capability.Sudo));

        // Read implies Connect.
        Assert.True(capabilities.Can("read-only", Capability.Read));
        Assert.True(capabilities.Can("read-only", Capability.Connect));
        Assert.False(capabilities.Can("read-only", Capability.List));

        // List does not imply Connect, and does not imply Read.
        Assert.True(capabilities.Can("list-only", Capability.List));
        Assert.False(capabilities.Can("list-only", Capability.Connect));

        // A path with no capabilities, or one never asked about, is never operable.
        Assert.False(capabilities.Can("empty", Capability.Read));
        Assert.False(capabilities.Can("unknown/path", Capability.Read));
    }

    [Fact]
    [Requirement("SYS-051")]
    [Trait("Requirement", "SYS-051")]
    public void Capability_named_instances_round_trip_and_reject_an_empty_wire_value()
    {
        (Capability Value, string Wire)[] named =
        [
            (Capability.Root, "root"),
            (Capability.Deny, "deny"),
            (Capability.Read, "read"),
            (Capability.List, "list"),
            (Capability.Create, "create"),
            (Capability.Update, "update"),
            (Capability.Delete, "delete"),
            (Capability.Sudo, "sudo"),
            (Capability.Connect, "connect"),
        ];

        Assert.All(named, entry =>
        {
            Assert.False(entry.Value.IsOther);
            Assert.Equal(entry.Wire, entry.Value.ToString());
            Assert.Equal(entry.Wire, entry.Value.WireValue);
        });

        Capability other = Capability.Other("future-verb");
        Assert.True(other.IsOther);

        Assert.True(Capability.Root == Capability.Other("root"));
        Assert.True(Capability.Root != Capability.Deny);
        Assert.Equal(Capability.Root.GetHashCode(), Capability.Other("root").GetHashCode());

        _ = Assert.Throws<ArgumentException>(() => Capability.Other(string.Empty));
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
