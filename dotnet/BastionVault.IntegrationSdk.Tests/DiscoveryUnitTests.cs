using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Text;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M5a's unit coverage for section 13: address classification (DSC-001, DSC-002), SRV discovery
/// (DSC-010…014), health probing (DSC-020…022), ranking and picking (DSC-030…036), TLS under
/// discovery (RES-010, RES-011, CFG-043) and diagnostics (RES-020, RES-021).
/// </summary>
public sealed class DiscoveryUnitTests
{
    private const string LeaderBody = """{"initialized":true,"sealed":false,"standby":false,"cluster_healthy":true}""";
    private const string FollowerBody = """{"initialized":true,"sealed":false,"standby":true,"cluster_healthy":true}""";
    private const string SealedBody = """{"initialized":true,"sealed":true,"standby":false,"cluster_healthy":true}""";

    // ---- DSC-001 / DSC-002: classification ------------------------------------------------

    [Theory]
    [Requirement("DSC-001")]
    [Requirement("DSC-012")]
    [Requirement("DSC-013")]
    [Trait("Requirement", "DSC-001")]
    [InlineData("https://bv-1.corp.example:8200", false, "https://bv-1.corp.example:8200")]
    [InlineData("http://bv-1.corp.example:8200", false, "http://bv-1.corp.example:8200")]
    [InlineData("bv-1.corp.example:8200", false, "https://bv-1.corp.example:8200")]
    [InlineData("10.0.0.5", false, "https://10.0.0.5:8200")]
    [InlineData("10.0.0.5:9000", false, "https://10.0.0.5:9000")]
    [InlineData("[::1]:8200", false, "https://[::1]:8200")]
    [InlineData("[::1]", false, "https://[::1]:8200")]
    [InlineData("https://vault.corp.example", false, "https://vault.corp.example:443")]
    // D-M5-21: row 2 is decided by the written address. `:80` is an explicit port, so these stay
    // literal even though Uri.IsDefaultPort would call the first of them portless.
    [InlineData("http://vault.corp.example:80", false, "http://vault.corp.example:80")]
    [InlineData("http://vault.corp.example:8200", false, "http://vault.corp.example:8200")]
    [InlineData("http://[::1]:80", false, "http://[::1]:80")]
    [InlineData("http://vault.corp.example/some/path", true, null)]
    [InlineData("http://localhost", true, null)]
    [InlineData("vault.corp.example", true, null)]
    [InlineData("_bvault._tcp.vault.corp.example", true, null)]
    [InlineData("http://vault.corp.example", true, null)]
    public void Address_classification_implements_the_dsc_001_table(string address, bool isDiscovery, string? candidateUrl)
    {
        AddressClassifier.Classification classification =
            AddressClassifier.Classify(address, new DiscoveryConfig(), clusterDiscovery: true);

        Assert.Equal(isDiscovery, classification.IsDiscovery);
        if (isDiscovery)
        {
            Assert.Null(classification.Literal);
            Assert.Null(classification.Uri);
            Assert.Null(classification.Endpoint);
            return;
        }

        Assert.Equal(candidateUrl, classification.Literal!.Url);
        Assert.NotNull(classification.Uri);
        // The endpoint is the raw address verbatim when it already carries a scheme, so literal
        // request URIs stay byte-identical to M1b's (D-M5-11).
        Assert.Equal(address.Contains("://", StringComparison.Ordinal) ? address : candidateUrl, classification.Endpoint);
    }

    [Fact]
    [Requirement("DSC-001")]
    [Trait("Requirement", "DSC-001")]
    public void ClusterDiscovery_false_forces_a_bare_name_literal_at_the_default_scheme_and_port()
    {
        AddressClassifier.Classification bare =
            AddressClassifier.Classify("vault.corp.example", new DiscoveryConfig(), clusterDiscovery: false);
        Assert.False(bare.IsDiscovery);
        Assert.Equal("https://vault.corp.example:8200", bare.Literal!.Url);

        // The `http://` cluster-name row is also gated on discovery being enabled.
        AddressClassifier.Classification insecure =
            AddressClassifier.Classify("http://vault.corp.example", new DiscoveryConfig(), clusterDiscovery: false);
        Assert.False(insecure.IsDiscovery);
        Assert.Equal("http://vault.corp.example:80", insecure.Literal!.Url);

        // And DiscoveryConfig's own defaults are honoured, not hard-coded.
        AddressClassifier.Classification custom = AddressClassifier.Classify(
            "vault.corp.example",
            new DiscoveryConfig { DefaultScheme = "http", DefaultPort = 9000 },
            clusterDiscovery: false);
        Assert.Equal("http://vault.corp.example:9000", custom.Literal!.Url);
    }

    [Fact]
    [Requirement("DSC-001")]
    [Trait("Requirement", "DSC-001")]
    public void Client_config_reports_the_dsc_001_classification_on_its_public_pair()
    {
        // D-M5-18: these two already-public values deliberately change for a host:port and a bare-IP
        // address, which the pre-M5 resolver reported as cluster names.
        using BastionVaultClient literal = new(
            new BastionVaultClientOptions { Address = "bv-1.corp.example:8200" }, EnvironmentSource.None);
        Assert.False(literal.Config.AddressIsClusterName);
        Assert.Equal(new Uri("https://bv-1.corp.example:8200"), literal.Config.AddressUri);

        using BastionVaultClient discovery = new(
            new BastionVaultClientOptions { Address = "vault.corp.example" }, EnvironmentSource.None);
        Assert.True(discovery.Config.AddressIsClusterName);
        Assert.Null(discovery.Config.AddressUri);
        Assert.Equal("vault.corp.example", discovery.InputLabel);
    }

    [Fact]
    [Requirement("DSC-002")]
    [Trait("Requirement", "DSC-002")]
    public void Unbracketed_ipv6_with_a_port_raises_bv_config_001()
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => AddressClassifier.Classify("fe80::1:8200", new DiscoveryConfig(), clusterDiscovery: true));

        Assert.Equal(ErrorCodes.ConfigInvalidAddress, exception.Code);
        Assert.Contains("ambiguous", exception.Hint, StringComparison.OrdinalIgnoreCase);

        // The hint has to describe the portless shape too, which is rejected by the same rule
        // (section 13's address table: "IPv6 must be bracketed").
        BastionVaultException portless = Assert.Throws<BastionVaultException>(
            () => AddressClassifier.Classify("::1", new DiscoveryConfig(), clusterDiscovery: true));
        Assert.Equal(ErrorCodes.ConfigInvalidAddress, portless.Code);
        Assert.Contains("`[::1]`", portless.Hint, StringComparison.Ordinal);
        Assert.Contains("`[::1]:8200`", portless.Hint, StringComparison.Ordinal);

        // The rejection joins DR-0003's fixed validation order at position 1, so construction fails
        // with this and not with a later check (D-M5-18).
        BastionVaultException constructed = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Address = "::1:8200", Namespace = "/illegal" },
            EnvironmentSource.None));
        Assert.Equal(ErrorCodes.ConfigInvalidAddress, constructed.Code);
    }

    [Theory]
    [Requirement("DSC-001")]
    [Trait("Requirement", "DSC-001")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com")]
    [InlineData("not a valid address")]
    [InlineData("vault.corp.example/v1")]
    [InlineData("bv-1.corp.example:not-a-port")]
    [InlineData("bv-1.corp.example:0")]
    [InlineData("bv-1.corp.example:70000")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData(":8200")]
    public void A_malformed_address_raises_bv_config_001(string address)
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => AddressClassifier.Classify(address, new DiscoveryConfig(), clusterDiscovery: true));
        Assert.Equal(ErrorCodes.ConfigInvalidAddress, exception.Code);
    }

    [Fact]
    [Requirement("DSC-001")]
    [Trait("Requirement", "DSC-001")]
    public void Control_characters_are_rejected_like_whitespace()
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => AddressClassifier.Classify("vault\u0001corp", new DiscoveryConfig(), clusterDiscovery: true));
        Assert.Equal(ErrorCodes.ConfigInvalidAddress, exception.Code);
    }

    [Fact]
    [Requirement("DSC-001")]
    [Requirement("CNF-035")]
    [Trait("Requirement", "CNF-035")]
    public void Http_on_a_cluster_name_still_needs_AllowInsecureHttp()
    {
        // A discovery-mode address leaves AddressUri null, so the guard is keyed on the classified
        // scheme instead — otherwise `http://` plus discovery would be the one way past CNF-035.
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Address = "http://vault.corp.example" }, EnvironmentSource.None));
        Assert.Equal(ErrorCodes.ConfigInsecureHttpNotAllowed, exception.Code);

        using BastionVaultClient allowed = new(
            new BastionVaultClientOptions { Address = "http://vault.corp.example", AllowInsecureHttp = true },
            EnvironmentSource.None);
        Assert.True(allowed.Config.AddressIsClusterName);

        using BastionVaultClient loopback = new(
            new BastionVaultClientOptions { Address = "http://127.0.0.1", ClusterDiscovery = false },
            EnvironmentSource.None);
        Assert.False(loopback.Config.AddressIsClusterName);
    }

    // ---- DSC-010 … DSC-014: SRV discovery -------------------------------------------------

    [Fact]
    [Requirement("DSC-010")]
    [Requirement("DSC-014")]
    [Trait("Requirement", "DSC-010")]
    public async Task A_bare_name_is_prefixed_and_an_underscore_name_is_queried_verbatim()
    {
        RecordingResolver resolver = new();
        resolver.Answer("_bvault._tcp.vault.corp.example", new SrvRecord("bv-1.corp.example.", 8200, 10, 50));
        resolver.Answer("_svc._tcp.vault.corp.example", new SrvRecord("bv-2.corp.example", 8200, 10, 50));

        using BastionVaultClient bare = Client(new FakeTransport(), resolver, address: "vault.corp.example");
        IReadOnlyList<Candidate> candidates = await bare.Context.Discovery.ResolveCandidatesAsync(default);
        Assert.Equal(["_bvault._tcp.vault.corp.example"], resolver.Queried);
        // DSC-010's trailing dot is stripped; DSC-013's port is explicit.
        Assert.Equal("https://bv-1.corp.example:8200", candidates.Single().Url);
        Assert.Equal("bv-1.corp.example", candidates.Single().Target);
        Assert.Equal(10, candidates.Single().Priority);
        Assert.Equal(50, candidates.Single().Weight);

        resolver.Queried.Clear();
        using BastionVaultClient verbatim = Client(
            new FakeTransport(), resolver, address: "_bvault._tcp.vault.corp.example");
        _ = await verbatim.Context.Discovery.ResolveCandidatesAsync(default);
        Assert.Equal(["_bvault._tcp.vault.corp.example"], resolver.Queried);

        // A non-default SrvService is honoured rather than hard-coded.
        resolver.Queried.Clear();
        using BastionVaultClient renamed = Client(
            new FakeTransport(),
            resolver,
            address: "vault.corp.example",
            discovery: new DiscoveryConfig { SrvService = "_svc._tcp" });
        _ = await renamed.Context.Discovery.ResolveCandidatesAsync(default);
        Assert.Equal(["_svc._tcp.vault.corp.example"], resolver.Queried);
    }

    [Fact]
    [Requirement("DSC-010")]
    [Requirement("DSC-031")]
    [Trait("Requirement", "DSC-010")]
    public void Candidates_are_ordered_by_ascending_priority_and_the_floor_is_hard()
    {
        // DSC-010's ascending sort and DSC-031's floor, on the ranking the diagnostics table shows:
        // the leader at priority 20 loses to the follower at 10, and the rejected tail is still
        // ordered by priority.
        ProbeResult[] probes =
        [
            Probe("bv-c", NodeState.ActiveLeader, priority: 30),
            Probe("bv-a", NodeState.Follower, priority: 10),
            Probe("bv-b", NodeState.ActiveLeader, priority: 20),
        ];

        (IReadOnlyList<ProbeResult> ranked, NodeSelection? picked) = DiscoveryEngine.Rank(probes);

        Assert.Equal("https://bv-a:8200", picked!.Url);
        Assert.Equal(NodeState.Follower, picked.State);
        Assert.Equal(
            ["https://bv-a:8200", "https://bv-b:8200", "https://bv-c:8200"],
            ranked.Select(probe => probe.Candidate.Url));
    }

    [Fact]
    [Requirement("DSC-011")]
    [Requirement("DSC-012")]
    [Trait("Requirement", "DSC-011")]
    public async Task A_resolver_failure_is_no_records_and_a_bare_name_then_synthesises_one_candidate()
    {
        // Throwing resolver.
        using BastionVaultClient throwing = Client(new FakeTransport(), new ThrowingResolver());
        Candidate synthesised = (await throwing.Context.Discovery.ResolveCandidatesAsync(default)).Single();
        Assert.Equal("https://vault.corp.example:8200", synthesised.Url);
        // DSC-012's candidate has no SRV record behind it, so no priority and no weight (D-M5-8).
        Assert.Null(synthesised.Priority);
        Assert.Null(synthesised.Weight);

        // No resolver configured at all takes the same path.
        using BastionVaultClient none = Client(new FakeTransport());
        Assert.Equal(
            "https://vault.corp.example:8200",
            (await none.Context.Discovery.ResolveCandidatesAsync(default)).Single().Url);

        // An empty answer is indistinguishable from a failure.
        using BastionVaultClient empty = Client(new FakeTransport(), new RecordingResolver());
        _ = Assert.Single(await empty.Context.Discovery.ResolveCandidatesAsync(default));
    }

    [Fact]
    [Requirement("DSC-011")]
    [Trait("Requirement", "DSC-011")]
    public async Task The_resolve_timeout_is_no_records_while_the_callers_cancellation_still_cancels()
    {
        using BastionVaultClient timing = Client(
            new FakeTransport(),
            new HangingResolver(),
            discovery: new DiscoveryConfig { ResolveTimeout = TimeSpan.FromMilliseconds(10) });
        _ = Assert.Single(await timing.Context.Discovery.ResolveCandidatesAsync(default));

        using BastionVaultClient cancelled = Client(new FakeTransport(), new HangingResolver());
        using CancellationTokenSource source = new();
        await source.CancelAsync();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelled.Context.Discovery.ResolveCandidatesAsync(source.Token));
    }

    [Fact]
    [Requirement("DSC-012")]
    [Trait("Requirement", "DSC-012")]
    public async Task An_srv_shaped_name_with_no_records_raises_bv_discovery_001()
    {
        using BastionVaultClient client = Client(
            new FakeTransport(), new RecordingResolver(), address: "_bvault._tcp.vault.corp.example");

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.ConnectAsync());

        Assert.Equal(ErrorCodes.DiscoveryNoCandidates, exception.Code);
        Assert.Contains("ownerName", exception.Details.Keys, StringComparer.Ordinal);
    }

    // ---- DSC-020 … DSC-022: probing -------------------------------------------------------

    [Fact]
    [Requirement("DSC-020")]
    [Trait("Requirement", "DSC-020")]
    public async Task At_most_Parallelism_probes_are_in_flight()
    {
        GatedTransport transport = new(releaseAt: 2);
        RecordingResolver resolver = new();
        resolver.Answer(
            "_bvault._tcp.vault.corp.example",
            new SrvRecord("n1", 8200, 10, 50),
            new SrvRecord("n2", 8200, 10, 50),
            new SrvRecord("n3", 8200, 10, 50),
            new SrvRecord("n4", 8200, 10, 50));

        using BastionVaultClient client = Client(transport, resolver, health: new HealthConfig { Parallelism = 2 });
        DiscoveryReport report = await client.DiscoverAsync();

        Assert.Equal(4, report.Ranked.Count);
        Assert.Equal(2, transport.MaxInFlight);
    }

    [Fact]
    [Requirement("DSC-020")]
    [Trait("Requirement", "DSC-020")]
    public async Task Probes_are_issued_in_candidate_order_even_when_the_transport_yields()
    {
        // D-M5-22: the Nth candidate's probe is issued before the (N+1)th, including when a bounded
        // slot must free first. Six shared fixtures match exchanges by sequence, so this is the
        // invariant that keeps them deterministic; a transport that yields is what would break an
        // implementation relying on SemaphoreSlim.WaitAsync completing inline.
        YieldingTransport transport = new();
        RecordingResolver resolver = new();
        resolver.Answer(
            "_bvault._tcp.vault.corp.example",
            Enumerable.Range(1, 6).Select(n => new SrvRecord($"n{n}.corp.example", 8200, 10, 50)).ToArray());

        using BastionVaultClient client = Client(transport, resolver, health: new HealthConfig { Parallelism = 4 });
        DiscoveryReport report = await client.DiscoverAsync();

        Assert.Equal(6, report.Ranked.Count);
        Assert.Equal(
            Enumerable.Range(1, 6).Select(n => $"n{n}.corp.example"),
            transport.Requests.Select(request => request.Uri.Host));
    }

    [Fact]
    [Requirement("DSC-020")]
    [Trait("Requirement", "DSC-020")]
    public async Task A_non_positive_Parallelism_still_probes_one_at_a_time()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Bytes(LeaderBody));
        using BastionVaultClient client = Client(transport, health: new HealthConfig { Parallelism = 0 });

        Assert.Equal(NodeState.ActiveLeader, (await client.DiscoverAsync()).Ranked.Single().State);
    }

    [Theory]
    [Requirement("DSC-021")]
    [Trait("Requirement", "DSC-021")]
    [InlineData("""{"initialized":false,"sealed":true,"standby":true}""", nameof(NodeState.Uninitialized))]
    [InlineData("""{"initialized":true,"sealed":true,"standby":true}""", nameof(NodeState.Sealed))]
    [InlineData("""{"initialized":true,"sealed":false,"standby":true}""", nameof(NodeState.Follower))]
    [InlineData("""{"initialized":true,"sealed":false,"standby":false,"performance_standby":true}""", nameof(NodeState.Follower))]
    [InlineData("""{"initialized":true,"sealed":false,"standby":false}""", nameof(NodeState.ActiveLeader))]
    [InlineData("{}", nameof(NodeState.ActiveLeader))]
    [InlineData("not json", nameof(NodeState.Unreachable))]
    [InlineData("[]", nameof(NodeState.Unreachable))]
    [InlineData("", nameof(NodeState.Unreachable))]
    public async Task The_probe_body_is_authoritative_for_the_state(string body, string expected)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Bytes(body));
        using BastionVaultClient client = Client(transport);

        ProbeResult probe = (await client.DiscoverAsync()).Ranked.Single();

        Assert.Equal(Enum.Parse<NodeState>(expected), probe.State);
        // An unmeasurable round trip is null, never zero (D-M1c-25: do not report what was not seen).
        Assert.Equal(probe.State == NodeState.Unreachable, probe.RttMs is null);
    }

    [Fact]
    [Requirement("DSC-021")]
    [Requirement("DSC-022")]
    [Trait("Requirement", "DSC-022")]
    public async Task Probe_records_rtt_cluster_id_and_version_and_cluster_healthy_never_changes_the_state()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(
            503,
            body: Bytes("""{"initialized":true,"sealed":false,"standby":false,"cluster_healthy":false,"cluster_id":"c-1","version":"0.42.1"}"""));
        using BastionVaultClient client = Client(transport, clock: new SteppingClock(TimeSpan.FromMilliseconds(7)));

        ProbeResult probe = (await client.DiscoverAsync()).Ranked.Single();

        // cluster_healthy == false is surfaced, not folded into the state (DSC-022); the 503 status
        // is informational and the body wins.
        Assert.Equal(NodeState.ActiveLeader, probe.State);
        Assert.False(probe.ClusterHealthy);
        Assert.Equal("c-1", probe.ClusterId);
        Assert.Equal("0.42.1", probe.Version);
        Assert.Equal(7, probe.RttMs);
    }

    [Fact]
    [Requirement("DSC-021")]
    [Trait("Requirement", "DSC-021")]
    public async Task A_transport_failure_is_an_unreachable_probe_and_not_a_caller_error()
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(TransportFailureKind.Timeout);
        CapturingRequestObserver observer = new();
        using BastionVaultClient client = Client(transport, observer: observer);

        ProbeResult probe = (await client.DiscoverAsync()).Ranked.Single();

        Assert.Equal(NodeState.Unreachable, probe.State);
        // D-M5-17: probes are reported to the observability hook with attempt number 0, carry no
        // token, and never appear in a caller's Error.Attempts.
        RequestEvent reported = Assert.Single(observer.Events);
        Assert.Equal(0, reported.Attempt);
        Assert.Equal("sys/health", reported.Path);
        Assert.Equal(ErrorCodes.TransportTimeout, reported.ErrorCode);
        Assert.DoesNotContain("X-BastionVault-Token", transport.Requests.Single().Headers.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("application/json", transport.Requests.Single().Headers["Accept"]);
    }

    [Fact]
    [Requirement("DSC-020")]
    [Trait("Requirement", "DSC-020")]
    public async Task A_client_with_no_transport_cannot_probe()
    {
        using BastionVaultClient client = new(
            new BastionVaultClientOptions { Address = "vault.corp.example" }, EnvironmentSource.None);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
    }

    // ---- DSC-030 … DSC-034: picking -------------------------------------------------------

    [Fact]
    [Requirement("DSC-030")]
    [Requirement("DSC-033")]
    [Trait("Requirement", "DSC-033")]
    public void Ranking_is_state_then_rtt_then_weight_then_lexical_url()
    {
        ProbeResult[] probes =
        [
            Probe("d", NodeState.Follower, rttMs: 10, weight: 50),
            Probe("c", NodeState.Follower, rttMs: 10, weight: 50),
            Probe("b", NodeState.Follower, rttMs: 10, weight: 90),
            Probe("a", NodeState.Follower, rttMs: 20, weight: 90),
            Probe("leader", NodeState.ActiveLeader, rttMs: 99, weight: 1),
            Probe("sealed", NodeState.Sealed, rttMs: 1, weight: 99),
        ];

        (IReadOnlyList<ProbeResult> ranked, NodeSelection? picked) = DiscoveryEngine.Rank(probes);

        Assert.Equal("https://leader:8200", picked!.Url);
        Assert.Equal(
            [
                "https://leader:8200",  // state wins over a much lower RTT
                "https://b:8200",       // rtt 10, weight 90
                "https://c:8200",       // rtt 10, weight 50, lexically before d
                "https://d:8200",
                "https://a:8200",       // rtt 20
                "https://sealed:8200",  // dropped by DSC-030, so last however fast
            ],
            ranked.Select(probe => probe.Candidate.Url));
    }

    [Fact]
    [Requirement("DSC-030")]
    [Requirement("DSC-033")]
    [Trait("Requirement", "DSC-030")]
    public void Every_probe_appears_in_the_table_and_rejected_nodes_are_ordered_by_state()
    {
        ProbeResult[] probes =
        [
            Probe("unreachable", NodeState.Unreachable),
            Probe("uninitialized", NodeState.Uninitialized),
            Probe("sealed", NodeState.Sealed),
        ];

        (IReadOnlyList<ProbeResult> ranked, NodeSelection? picked) = DiscoveryEngine.Rank(probes);

        Assert.Null(picked);
        Assert.Equal(
            ["https://sealed:8200", "https://uninitialized:8200", "https://unreachable:8200"],
            ranked.Select(probe => probe.Candidate.Url));
    }

    [Fact]
    [Requirement("DSC-032")]
    [Trait("Requirement", "DSC-032")]
    public void Candidates_outside_the_dominant_cluster_id_are_dropped_and_those_without_one_are_kept()
    {
        ProbeResult[] probes =
        [
            Probe("minority", NodeState.ActiveLeader, clusterId: "cluster-b"),
            Probe("majority-1", NodeState.Follower, clusterId: "cluster-a"),
            Probe("majority-2", NodeState.Follower, clusterId: "cluster-a"),
            Probe("unknown", NodeState.Follower),
        ];

        (IReadOnlyList<ProbeResult> ranked, NodeSelection? picked) = DiscoveryEngine.Rank(probes);

        // The ActiveLeader is in the minority cluster, so it is dropped despite the better state,
        // and the candidate with no cluster_id survives.
        Assert.Equal("https://majority-1:8200", picked!.Url);
        Assert.Equal("https://minority:8200", ranked[^1].Candidate.Url);
    }

    [Fact]
    [Requirement("DSC-032")]
    [Trait("Requirement", "DSC-032")]
    public void A_cluster_id_frequency_tie_is_broken_by_state_then_priority()
    {
        ProbeResult[] byState =
        [
            Probe("a", NodeState.Follower, clusterId: "cluster-a"),
            Probe("b", NodeState.ActiveLeader, clusterId: "cluster-b"),
        ];
        Assert.Equal("https://b:8200", DiscoveryEngine.Rank(byState).Picked!.Url);

        ProbeResult[] byPriority =
        [
            Probe("a", NodeState.Follower, clusterId: "cluster-a", priority: 10),
            Probe("b", NodeState.Follower, clusterId: "cluster-b", priority: 10),
        ];
        // Same frequency, same state, same priority: the tie falls through to the cluster id itself,
        // so the choice is deterministic rather than dictionary-order dependent.
        Assert.Equal("https://a:8200", DiscoveryEngine.Rank(byPriority).Picked!.Url);
    }

    [Fact]
    [Requirement("DSC-033")]
    [Trait("Requirement", "DSC-033")]
    public void Two_candidates_sharing_a_url_still_rank_deterministically()
    {
        ProbeResult[] probes = [Probe("a", NodeState.Follower, rttMs: 5), Probe("a", NodeState.Follower, rttMs: 5)];
        Assert.Equal(2, DiscoveryEngine.Rank(probes).Ranked.Count);
        Assert.Equal("https://a:8200", DiscoveryEngine.Rank(probes).Picked!.Url);
    }

    [Fact]
    [Requirement("DSC-034")]
    [Requirement("RES-021")]
    [Trait("Requirement", "DSC-034")]
    public async Task No_survivor_raises_bv_discovery_002_listing_target_equals_state()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(503, body: Bytes(SealedBody));
        transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        RecordingResolver resolver = new();
        resolver.Answer(
            "_bvault._tcp.vault.corp.example",
            new SrvRecord("bv-1.corp.example", 8200, 10, 50),
            new SrvRecord("bv-2.corp.example", 8200, 10, 50));
        using BastionVaultClient client = Client(transport, resolver);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.ConnectAsync());

        Assert.Equal(ErrorCodes.DiscoveryNoHealthyNode, exception.Code);
        Assert.False(exception.Retryable);
        Assert.Equal(
            ["bv-1.corp.example=Sealed", "bv-2.corp.example=Unreachable"],
            Assert.IsType<string[]>(exception.Details["candidates"]));
        // RES-021: the operator sees *why* nothing was picked from the hint alone.
        Assert.Contains("bv-1.corp.example=Sealed", exception.Hint, StringComparison.Ordinal);
        Assert.Null(client.SelectedNode);
    }

    // ---- DSC-035, DSC-036, D-M5-9, D-M5-10 ------------------------------------------------

    [Fact]
    [Requirement("DSC-035")]
    [Trait("Requirement", "DSC-035")]
    public async Task Connect_pins_once_and_is_idempotent_while_reconnect_re_runs_discovery()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Bytes(LeaderBody));
        transport.EnqueueResponse(200, body: Bytes(FollowerBody));
        using BastionVaultClient client = Client(transport, Single("bv-1.corp.example"));

        NodeSelection? first = await client.ConnectAsync();
        NodeSelection? again = await client.ConnectAsync();

        Assert.Same(first, again);
        _ = Assert.Single(transport.Requests);
        Assert.Equal(NodeState.ActiveLeader, client.SelectedNode!.State);
        Assert.Equal("vault.corp.example", client.InputLabel);
        // The candidate set is cached, which is the set DSC-042's replay re-probes without a second
        // SRV lookup (M5b); nothing in M5a moves off the pin.
        Assert.Equal(
            ["https://bv-1.corp.example:8200"],
            client.Context.Discovery.Candidates!.Select(candidate => candidate.Url));

        // DSC-046's member: the only way to re-run discovery.
        NodeSelection? reconnected = await client.ReconnectAsync();
        Assert.Equal(NodeState.Follower, reconnected!.State);
        Assert.Equal(NodeState.Follower, client.SelectedNode!.State);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    [Requirement("DSC-035")]
    [Trait("Requirement", "DSC-035")]
    public async Task Discovery_runs_lazily_before_the_first_operation_and_only_once()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Bytes(LeaderBody));
        transport.EnqueueResponse(200, body: Bytes("""{"initialized":true,"sealed":false,"standby":false}"""));
        transport.EnqueueResponse(200, body: Bytes("""{"initialized":true,"sealed":false,"standby":false}"""));
        using BastionVaultClient client = Client(transport, Single("bv-1.corp.example"));

        Assert.Null(client.SelectedNode);
        _ = await client.Sys.HealthAsync();
        _ = await client.Sys.HealthAsync();

        // One probe, then the two operations, all against the pinned node (D-M5-9, DSC-040's pin).
        Assert.Equal(
            [
                "https://bv-1.corp.example:8200/v1/sys/health",
                "https://bv-1.corp.example:8200/v1/sys/health",
                "https://bv-1.corp.example:8200/v1/sys/health",
            ],
            transport.Requests.Select(request => request.Uri.AbsoluteUri));
        Assert.Equal("https://bv-1.corp.example:8200", client.SelectedNode!.Url);
        // The probe carried no token; the operations did (D-M5-17).
        Assert.DoesNotContain("X-BastionVault-Token", transport.Requests[0].Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Requirement("DSC-035")]
    [Requirement("DSC-036")]
    [Trait("Requirement", "DSC-036")]
    public async Task Discover_never_touches_the_pin_and_a_literal_client_pins_nothing()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Bytes(LeaderBody));
        transport.EnqueueResponse(200, body: Bytes(FollowerBody));
        using BastionVaultClient client = Client(transport, Single("bv-1.corp.example"));

        _ = await client.ConnectAsync();
        DiscoveryReport report = await client.DiscoverAsync();

        Assert.Equal(NodeState.Follower, report.Picked!.State);
        Assert.Equal(NodeState.ActiveLeader, client.SelectedNode!.State);

        // D-M5-10: a literal client has nothing chosen, but asking for diagnostics still probes —
        // DSC-036 is the one place DSC-001's "no probing" does not bind, because no pick results.
        FakeTransport literalTransport = new();
        literalTransport.EnqueueResponse(200, body: Bytes(SealedBody));
        using BastionVaultClient literal = Client(literalTransport, address: "https://bv-9.corp.example:8200");
        Assert.Null(literal.SelectedNode);
        DiscoveryReport literalReport = await literal.DiscoverAsync();
        Assert.Equal("https://bv-9.corp.example:8200", literalReport.Ranked.Single().Candidate.Url);
        Assert.Equal("https://bv-9.corp.example:8200", literal.InputLabel);
        Assert.Null(literalReport.Picked);
        Assert.Null(literal.SelectedNode);
        _ = Assert.Single(literalTransport.Requests);
    }

    [Fact]
    [Requirement("DSC-001")]
    [Requirement("DSC-035")]
    [Trait("Requirement", "DSC-001")]
    public async Task A_literal_client_never_probes_for_Connect_or_Reconnect_and_reports_no_selection()
    {
        // D-M5-8 as amended at revision 3: DSC-001 makes literal mode "no DNS, no probing", so
        // neither entry point sends anything, and both return null rather than a health state the
        // SDK never verified (D-M5-10).
        FakeTransport transport = new();
        using BastionVaultClient literal = Client(transport, address: "https://bv-9.corp.example:8200");

        Task<NodeSelection?> connect = literal.ConnectAsync();
        Task<NodeSelection?> reconnect = literal.ReconnectAsync();

        // The declared return types are nullable, which PublicApiSurface.txt cannot record (RF-2's
        // note about the gate), so the signatures are asserted here instead.
        Assert.Null(await connect);
        Assert.Null(await reconnect);
        Assert.Empty(transport.Requests);
        Assert.Null(literal.SelectedNode);

        // Same for a bare name under ClusterDiscovery = false, which DSC-001 also makes literal.
        FakeTransport forced = new();
        using BastionVaultClient disabled = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                ClusterDiscovery = false,
                Transport = forced,
                Token = "s.FAKE-token-0000000000000000",
            },
            EnvironmentSource.None);
        Assert.Null(await disabled.ConnectAsync());
        Assert.Empty(forced.Requests);
    }

    [Fact]
    [Requirement("DSC-035")]
    [Trait("Requirement", "DSC-035")]
    public async Task Concurrent_first_operations_discover_exactly_once()
    {
        GatedTransport transport = new(releaseAt: 1);
        using BastionVaultClient client = Client(transport, Single("bv-1.corp.example"));

        _ = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.ConnectAsync()));

        _ = Assert.Single(transport.Requests);
        Assert.Equal("https://bv-1.corp.example:8200", client.SelectedNode!.Url);
    }

    // ---- RES-010, RES-011, CFG-043: TLS under discovery -----------------------------------

    [Fact]
    [Requirement("RES-010")]
    [Requirement("RES-011")]
    [Trait("Requirement", "RES-010")]
    public async Task Probes_and_requests_share_one_transport_and_address_the_srv_target()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Bytes(LeaderBody));
        transport.EnqueueResponse(200, body: Bytes("""{"initialized":true,"sealed":false,"standby":false}"""));
        using BastionVaultClient client = Client(transport, Single("bv-1.corp.example"));

        _ = await client.Sys.HealthAsync();

        // RES-011: one ITransport, so one set of TLS material for both. RES-010: the authority is
        // the SRV target, which is what SNI and hostname verification follow.
        Assert.Same(transport, client.Transport);
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.Equal("bv-1.corp.example", request.Uri.Host));
    }

    [Fact]
    [Requirement("CFG-043")]
    [Requirement("RES-010")]
    [Trait("Requirement", "CFG-043")]
    public void Sni_follows_the_candidate_host_unless_TlsServerName_overrides_it()
    {
        // TargetHost left unset is what makes SNI and verification follow the request URI's host —
        // i.e. the SRV target of the chosen candidate (CFG-043's first clause, RES-010).
        Assert.Null(TargetHostFor(null));

        // CFG-043's exception, which is CFG-042: an explicit TlsServerName overrides it for every
        // candidate.
        Assert.Equal("sni.corp.example", TargetHostFor("sni.corp.example"));
    }

    // ---- RES-020: diagnostics -------------------------------------------------------------

    [Fact]
    [Requirement("RES-020")]
    [Trait("Requirement", "RES-020")]
    public void Render_reproduces_the_table_the_specification_prints()
    {
        DiscoveryReport report = new(
            "vault.corp.example",
            [
                Probe("bv-1.corp.example", NodeState.ActiveLeader, rttMs: 12, priority: 10, weight: 50),
                Probe("bv-2.corp.example", NodeState.Follower, rttMs: 14, priority: 10, weight: 50),
                Probe("bv-3.corp.example", NodeState.Sealed, rttMs: null, priority: 10, weight: 50),
            ],
            new NodeSelection("https://bv-1.corp.example:8200", NodeState.ActiveLeader, 12));

        // Byte-for-byte the block at specifications/13-cluster-discovery-and-resilience.md:148-152.
        Assert.Equal(
            "Target                                  Pri  Wt  State         RTT(ms)  ClusterId\n"
                + "https://bv-1.corp.example:8200          10   50  ActiveLeader       12  -\n"
                + "https://bv-2.corp.example:8200          10   50  Follower           14  -\n"
                + "https://bv-3.corp.example:8200          10   50  Sealed              -  -\n"
                + "Picked: https://bv-1.corp.example:8200 (ActiveLeader, 12 ms)",
            report.Render());
    }

    [Fact]
    [Requirement("RES-020")]
    [Trait("Requirement", "RES-020")]
    public void Render_prints_a_dash_for_an_absent_priority_weight_or_cluster_id_and_names_an_empty_pick()
    {
        ProbeResult synthesised = new(
            new Candidate("https://vault.corp.example:8200", "vault.corp.example", 8200, null, null),
            NodeState.Unreachable,
            null);
        DiscoveryReport report = new("vault.corp.example", [synthesised], null);

        Assert.Equal(
            "Target                                  Pri  Wt  State         RTT(ms)  ClusterId\n"
                + "https://vault.corp.example:8200         -    -   Unreachable         -  -\n"
                + "Picked: none",
            report.Render());

        DiscoveryReport withClusterId = new(
            "vault.corp.example",
            [Probe("bv-1", NodeState.Follower, rttMs: 3.6, priority: 0, weight: 0, clusterId: "c-1")],
            new NodeSelection("https://bv-1:8200", NodeState.Follower, null));
        Assert.Contains("c-1", withClusterId.Render(), StringComparison.Ordinal);
        // A rounded RTT, and a pick whose round trip was never measured.
        Assert.Contains("        4  c-1", withClusterId.Render(), StringComparison.Ordinal);
        Assert.EndsWith("(Follower, - ms)", withClusterId.Render(), StringComparison.Ordinal);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static byte[] Bytes(string body)
    {
        return Encoding.UTF8.GetBytes(body);
    }

    private static ProbeResult Probe(
        string host,
        NodeState state,
        double? rttMs = 0,
        int? priority = 10,
        int? weight = 50,
        string? clusterId = null)
    {
        return new ProbeResult(new Candidate($"https://{host}:8200", host, 8200, priority, weight), state, rttMs)
        {
            ClusterId = clusterId,
        };
    }

    private static RecordingResolver Single(string target)
    {
        RecordingResolver resolver = new();
        resolver.Answer("_bvault._tcp.vault.corp.example", new SrvRecord(target, 8200, 10, 50));
        return resolver;
    }

    private static BastionVaultClient Client(
        ITransport transport,
        ISrvResolver? resolver = null,
        string address = "vault.corp.example",
        IClock? clock = null,
        HealthConfig? health = null,
        DiscoveryConfig? discovery = null,
        IRequestObserver? observer = null)
    {
        return new BastionVaultClient(
            new BastionVaultClientOptions
            {
                Address = address,
                Token = "s.FAKE-token-0000000000000000",
                Transport = transport,
                SrvResolver = resolver,
                Health = health,
                Discovery = discovery,
                Clock = clock,
                Observer = observer,
                RateGate = new RateGate { RatePerSecond = 0 },
            },
            EnvironmentSource.None);
    }

    /// <summary>
    /// The SNI/verification host <see cref="HttpClientTransport"/> configures, which is otherwise
    /// observable only through a real handshake. Read off the handler the transport builds, through
    /// its own private TLS builder, so the assertion is about production code and not a restatement
    /// of it.
    /// </summary>
    private static string? TargetHostFor(string? tlsServerName)
    {
        using BastionVaultClient holder = new(
            new BastionVaultClientOptions
            {
                Address = "https://bv-1.corp.example:8200",
                TlsServerName = tlsServerName,
            },
            EnvironmentSource.None);
        using SocketsHttpHandler handler = new();
        MethodInfo builder = typeof(HttpClientTransport)
            .GetMethod("BuildTlsOptions", BindingFlags.NonPublic | BindingFlags.Static)!;
        _ = builder.Invoke(null, [handler, holder.Config]);
        SslClientAuthenticationOptions options = handler.SslOptions;
        return options.TargetHost;
    }

    private sealed class RecordingResolver : ISrvResolver
    {
        private readonly Dictionary<string, IReadOnlyList<SrvRecord>> answers = new(StringComparer.Ordinal);

        public List<string> Queried { get; } = [];

        public void Answer(string ownerName, params SrvRecord[] records)
        {
            answers[ownerName] = records;
        }

        public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
        {
            Queried.Add(ownerName);
            return Task.FromResult(answers.TryGetValue(ownerName, out IReadOnlyList<SrvRecord>? records)
                ? records
                : (IReadOnlyList<SrvRecord>)Array.Empty<SrvRecord>());
        }
    }

    private sealed class ThrowingResolver : ISrvResolver
    {
        public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("SERVFAIL");
        }
    }

    private sealed class HangingResolver : ISrvResolver
    {
        public async Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return [];
        }
    }

    /// <summary>A clock that moves on every read, so a probe's own before/after pair is a known RTT.</summary>
    private sealed class SteppingClock : IClock
    {
        private readonly TimeSpan step;
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;

        public SteppingClock(TimeSpan step)
        {
            this.step = step;
        }

        public DateTimeOffset NowUtc()
        {
            DateTimeOffset current = now;
            now += step;
            return current;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A transport that yields before answering, so D-M5-22's issue order cannot be satisfied by a
    /// probe that happens to complete inline.
    /// </summary>
    private sealed class YieldingTransport : ITransport
    {
        private readonly List<TransportRequest> requests = [];
        private readonly object gate = new();

        public IReadOnlyList<TransportRequest> Requests
        {
            get
            {
                lock (gate)
                {
                    return requests.ToArray();
                }
            }
        }

        public bool SupportsCustomVerbs => true;

        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                requests.Add(request);
            }

            await Task.Yield();
            return new TransportResponse(
                200,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Bytes(LeaderBody));
        }
    }

    /// <summary>
    /// A transport that holds every request until <c>releaseAt</c> of them are in flight at once, so
    /// DSC-020's bound is observed rather than assumed.
    /// </summary>
    private sealed class GatedTransport : ITransport
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<TransportRequest> requests = [];
        private readonly int releaseAt;
        private readonly object gate = new();
        private int inFlight;

        public GatedTransport(int releaseAt)
        {
            this.releaseAt = releaseAt;
        }

        public int MaxInFlight { get; private set; }

        public IReadOnlyList<TransportRequest> Requests
        {
            get
            {
                lock (gate)
                {
                    return requests.ToArray();
                }
            }
        }

        public bool SupportsCustomVerbs => true;

        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                requests.Add(request);
                inFlight++;
                MaxInFlight = Math.Max(MaxInFlight, inFlight);
                if (inFlight >= releaseAt)
                {
                    _ = release.TrySetResult();
                }
            }

            await release.Task.ConfigureAwait(false);
            lock (gate)
            {
                inFlight--;
            }

            return new TransportResponse(
                200,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Bytes(LeaderBody));
        }
    }
}
