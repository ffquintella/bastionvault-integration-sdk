using System.Diagnostics;
using System.Text.Json;
using BastionVault.IntegrationSdk.IntegrationTests.Harness;

namespace BastionVault.IntegrationSdk.IntegrationTests.Scenarios;

/// <summary>
/// ITG-S27: the client-side rate gate (EFF-001) engages against a live server's default DoS
/// config (15-testing-requirements.md:293-295).
/// </summary>
/// <remarks>
/// Runs serially so this scenario's own client is the only one drawing from the shared
/// <see cref="AbuseGuardPacer"/> bucket while it measures elapsed time (DR-0021 F9): otherwise
/// another scenario's concurrent traffic against that same shared bucket could push the elapsed
/// time past the threshold even with a broken client gate, which is the "measures the harness
/// instead of the SDK" risk. In isolation the pacer's own worst case - Margin(200) per 10 s, i.e.
/// 100 free then throttled to roughly 10/s - carries 300 requests in about 20 s, under this
/// assertion's (300-16)/8 ~= 35.5 s floor, so a disabled client gate still fails this assertion.
/// </remarks>
public sealed class Scenario27_ClientRateGate : SerialIntegrationTest
{
    [IntegrationFact]
    public async Task Three_hundred_reads_are_paced_by_the_client_gate_and_none_are_denied()
    {
        string mount = await Resources.MountAsync("kv-v2", "rate");
        string path = Unique("secret");
        _ = await Client.Kv.V2.WriteSecretAsync(
            path,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["value"] = JsonDocument.Parse("\"x\"").RootElement.Clone(),
            },
            mount);

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 300; i++)
        {
            KvV2Secret? secret = await Client.Kv.V2.ReadSecretAsync(path, mount);
            Assert.NotNull(secret);
        }

        stopwatch.Stop();

        TimeSpan floor = TimeSpan.FromSeconds((300 - 16) / 8.0);
        Assert.True(
            stopwatch.Elapsed >= floor,
            $"expected >= {floor} for 300 reads under the default 8/s, burst-16 client rate gate (EFF-001), took {stopwatch.Elapsed}");
        Assert.DoesNotContain(Requests.Events, e => string.Equals(e.ErrorCode, ErrorCodes.RateLimitedByDosGuard, StringComparison.Ordinal));
    }
}

/// <summary>
/// ITG-S28: the server's own DoS guard bans an over-budget client, and the client gate's
/// pause-and-drain behaviour (EFF-003) rides it out (serial - 15-testing-requirements.md:296-299).
/// </summary>
public sealed class Scenario28_ServerSideDosGuardAndClientPause : SerialIntegrationTest
{
    [IntegrationFact]
    public async Task Exceeding_max_requests_bans_this_ip_and_the_client_gate_pauses_and_drains()
    {
        DosConfig original = await Client.Sys.Dos.ReadConfigAsync();
        try
        {
            // Only max_requests is specified by the requirement; window_secs and ban_secs are
            // also set, deliberately with ban_secs > window_secs, so "drains" below has a bounded
            // wait rather than the matrix default (300 s ban over a 10 s window) - a ban no longer
            // than its own counting window re-trips the instant it clears, on the burst's own
            // leftover count, which is a real, measured interaction and not a hypothetical one.
            // The window is generous (5 s) rather than tight: under the full suite's concurrency
            // (measured), 40 sequential calls can take longer than a couple of seconds, and a
            // window too close to that spread lets the burst finish without ever being "40 requests
            // in one window" at all.
            _ = await Client.Sys.Dos.WriteConfigAsync(new DosConfig { MaxRequests = 20, WindowSecs = 5, BanSecs = 8 });

            // Exclusive access (SerialGate) stops new concurrent scenario traffic from this point
            // on, but traffic already in flight from scenarios that were running up to the instant
            // this one acquired the gate can still fall inside the freshly-shortened window above;
            // this delay lets that tail roll out before this scenario's own, deliberate provocation
            // starts, so what trips the guard below is only this scenario's.
            await Task.Delay(TimeSpan.FromSeconds(5));

            string mount = await Resources.MountAsync("kv-v2", "rategate");
            string path = Unique("secret");
            _ = await Client.Kv.V2.WriteSecretAsync(
                path,
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["value"] = JsonDocument.Parse("\"x\"").RootElement.Clone(),
                },
                mount);

            // DR-0021 F9: the shared pacer's own budget (Margin(200) per 10 s) is well above this
            // provocation (max_requests = 15), so it does not itself intercept the burst -
            // verified by the assertion below actually finding BV-RATE-001.
            using BastionVaultClient uncapped = Server.CreateClient(o =>
            {
                o.RateGate = new RateGate { RatePerSecond = 0 };
                o.Logger = Logs;
                o.Observer = Requests;
            });

            List<BastionVaultException> rateLimited = [];
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    _ = await uncapped.Kv.V2.ReadSecretAsync(path, mount);
                }
                catch (BastionVaultException ex) when (string.Equals(ex.Code, ErrorCodes.RateLimitedByDosGuard, StringComparison.Ordinal))
                {
                    rateLimited.Add(ex);
                }
            }

            Assert.NotEmpty(rateLimited);
            Assert.Contains(rateLimited, ex => ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero);

            // The gate re-enabled: this scenario's own `Client` (default RateGate, EFF-001) hits
            // the same ban. BV-RATE-001 is hard-excluded from the ordinary retry loop (it is a
            // guard signal, not a transient fault), so the SDK does not retry it internally - a
            // caller does, and D-M1b-22 pauses the *client's own gate* on every 429 it sees
            // (EFF-003) regardless, so each call after a rejection queues behind
            // ClientRateGate.AcquireAsync until that call's own pause clears before reaching the
            // wire. This is what "pauses and drains" means for a caller: the client never busy-polls
            // a banned server, and a bounded number of caller-level attempts eventually succeeds.
            KvV2Secret? afterBan = null;
            int rateLimitedOnGatedClient = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int attempt = 0; attempt < 8 && afterBan is null; attempt++)
            {
                try
                {
                    afterBan = await Client.Kv.V2.ReadSecretAsync(path, mount);
                }
                catch (BastionVaultException ex) when (string.Equals(ex.Code, ErrorCodes.RateLimitedByDosGuard, StringComparison.Ordinal))
                {
                    rateLimitedOnGatedClient++;
                }
            }

            stopwatch.Stop();

            Assert.True(rateLimitedOnGatedClient > 0, "expected this client to be banned at least once before draining");
            Assert.NotNull(afterBan);
            Assert.True(
                stopwatch.Elapsed >= TimeSpan.FromSeconds(1),
                $"expected the queued reads to pause behind the server's ban before draining (EFF-003), took {stopwatch.Elapsed}");
        }
        finally
        {
            // The window above (5 s) needs to fully roll off the burst's own request count before
            // any further call is safe from re-tripping the guard on stale accounting.
            await Task.Delay(TimeSpan.FromSeconds(6));

            // Best effort: this run's own connecting address is not observable through the public
            // surface, so both loopback spellings are tried and swallowed on failure.
            foreach (string ip in new[] { "127.0.0.1", "::1" })
            {
                try
                {
                    await Client.Sys.Dos.UnbanAsync(ip);
                }
                catch (BastionVaultException)
                {
                }
            }

            _ = await Client.Sys.Dos.WriteConfigAsync(original);
        }
    }
}

/// <summary>ITG-S29: cache version epochs and <c>If-None-Match</c> (15-testing-requirements.md:300-301).</summary>
/// <remarks>
/// Runs serially: the wire response's <c>version</c>/<c>ETag</c> is a whole-cache aggregate, not
/// scoped to the one topic this scenario asks about (CCH-001's own example shows several topics
/// under one <c>version</c>), so a concurrent write from another scenario changes it too - which
/// would make the "matching ETag stays NotModified" half fail on someone else's write, not this
/// scenario's own state (measured: it does, under the default parallel scenario run).
/// </remarks>
public sealed class Scenario29_CacheVersion : SerialIntegrationTest
{
    [IntegrationFact]
    public async Task Epoch_increases_after_a_write_and_a_matching_etag_is_not_modified()
    {
        string mount = await Resources.MountAsync("kv-v2", "cache");
        string topic = $"{mount}/";

        CacheVersion before = await Client.Sys.CacheVersionAsync([topic]);
        Assert.Equal(CacheVersionState.Current, before.State);
        int? beforeEpoch = before.Topics!.TryGetValue(topic, out int existing) ? existing : null;

        string path = Unique("secret");
        _ = await Client.Kv.V2.WriteSecretAsync(
            path,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["value"] = JsonDocument.Parse("\"x\"").RootElement.Clone(),
            },
            mount);

        CacheVersion after = await Client.Sys.CacheVersionAsync([topic]);
        Assert.Equal(CacheVersionState.Current, after.State);
        Assert.True(
            after.Topics!.TryGetValue(topic, out int afterEpoch),
            $"expected topic '{topic}' to be present after a write to it (CCH-005 - a topic the token can see is never omitted)");
        if (beforeEpoch is { } b)
        {
            Assert.True(afterEpoch > b, $"expected the epoch to increase after a write (CCH-004), was {b} then {afterEpoch}");
        }

        Assert.False(string.IsNullOrEmpty(after.ETag));
        CacheVersion notModified = await Client.Sys.CacheVersionAsync([topic], ifNoneMatch: after.ETag);
        Assert.Equal(CacheVersionState.NotModified, notModified.State);
    }
}

/// <summary>ITG-S30: bulk metadata pagination over 12 userpass users (15-testing-requirements.md:302-303).</summary>
public sealed class Scenario30_UserpassPagination : IntegrationTest
{
    [IntegrationFact]
    public async Task Listing_twelve_users_at_limit_five_walks_three_pages_and_the_iterator_yields_all_twelve()
    {
        string mount = await Resources.EnableAuthAsync("userpass", "page");
        for (int i = 0; i < 12; i++)
        {
            string username = Resources.TrackUser(mount, $"user{i:D2}");
            _ = await Client.Auth.Userpass.Admin.WriteUserAsync(
                username,
                JsonDocument.Parse("""{"password":"Sc3n30-passw0rd!"}""").RootElement,
                mount);
        }

        List<string> keysSeen = [];
        string? after = null;
        int pages = 0;
        while (true)
        {
            Page<UserSummary> page = await Client.Auth.Userpass.Admin.ListUsersInfoAsync(mount, after: after, limit: 5);
            pages++;
            keysSeen.AddRange(page.Keys);
            if (!page.Truncated)
            {
                break;
            }

            after = page.Next;
        }

        Assert.Equal(3, pages);
        Assert.Equal(12, keysSeen.Count);
        for (int i = 1; i < keysSeen.Count; i++)
        {
            Assert.True(
                string.CompareOrdinal(keysSeen[i - 1], keysSeen[i]) < 0,
                $"expected strictly increasing keys (PAG-005), got '{keysSeen[i - 1]}' then '{keysSeen[i]}'");
        }

        int iteratorCount = 0;
        await foreach (KeyValuePair<string, UserSummary> entry in Client.Auth.Userpass.Admin.ListUsersInfoAllAsync(mount))
        {
            iteratorCount++;
        }

        Assert.Equal(12, iteratorCount);
    }
}

/// <summary>ITG-S31: <c>Logical.Raw</c> against an unknown path and an unsupported verb (15-testing-requirements.md:304-305).</summary>
public sealed class Scenario31_LogicalRawErrors : IntegrationTest
{
    [IntegrationFact]
    public async Task Unknown_sys_path_is_not_found_and_patch_is_method_not_allowed()
    {
        string unknownPath = $"/v1/sys/{Unique("does-not-exist")}";
        BastionVaultException notFound = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Logical.RawAsync("GET", unknownPath));
        Assert.Equal(ErrorCodes.NotFoundPathNotFound, notFound.Code);
        Assert.Equal(404, notFound.StatusCode);

        BastionVaultException methodNotAllowed = await Assert.ThrowsAsync<BastionVaultException>(
            () => Client.Logical.RawAsync("PATCH", "/v1/sys/health"));
        Assert.Equal(ErrorCodes.ProtocolMethodNotAllowed, methodNotAllowed.Code);
        Assert.Equal(405, methodNotAllowed.StatusCode);
    }
}

// Scenario32's own doc comment stays deliberately short (D-M12-15 Ruling D is the full story):
// `storage "file"` has no replication, so the three nodes below are independent vaults; a
// fixed-id token and a mirrored mount let one client authenticate against whichever survives,
// exercising the SDK's own discovery/failover (DSC-036, DSC-040, DSC-042) against real servers.
/// <summary>ITG-S32: managed multi-node discovery and bounded failover, optional (15-testing-requirements.md:306-309).</summary>
public sealed class Scenario32_ManagedMultiNodeDiscoveryAndFailover : SerialIntegrationTest
{
    [IntegrationFact]
    public async Task Discovery_ranks_a_node_first_a_read_fails_over_once_and_a_write_during_the_outage_does_not_replay()
    {
        Skip.If(Server.Mode != TestServerMode.Managed, "ITG-S32 needs a managed bvault binary to start two extra nodes; this run is external-mode.");
        string? binaryPath = TestEnvironment.Get(TestEnvironment.Bin);
        Skip.If(binaryPath is null, $"requires {TestEnvironment.Bin} to start the extra nodes");

        List<ManagedBinaryServer> nodes = [];
        List<BastionVaultClient> nodeClients = [];
        BastionVaultClient? cluster = null;
        try
        {
            const string fixedTokenId = "s32-shared-00000000000000000000";
            const string sharedMount = "s32-shared";
            const string sharedPath = "s32-secret";

            for (int i = 0; i < 3; i++)
            {
                ManagedBinaryServer node;
                try
                {
                    node = await ManagedBinaryServer.StartAsync(
                        binaryPath!, Context.Matrix.ManagedServer, $"{Server.RunId}-s32-{i}", CancellationToken.None);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    // D-M12-15 Ruling D: bvault's global /tmp/bastion_vault work directory can
                    // collide across concurrent nodes on one machine. Sanctioned skip, not a bug.
                    Skip.If(
                        true,
                        $"could not start managed node {i + 1} of 3 on this machine (managed multi-node is one run per machine, D-M12-15 Ruling D): {ex.Message}");
                    return;
                }

                nodes.Add(node);

                BastionVaultClientOptions bootstrapOptions = new()
                {
                    Address = node.Address,
                    ClusterDiscovery = false,
                    Timeout = TimeSpan.FromSeconds(30),
                };

                using (BastionVaultClient bootstrap = new(bootstrapOptions))
                {
                    using InitResult init = await bootstrap.Sys.InitAsync(
                        Context.Matrix.ManagedServer.InitShares, Context.Matrix.ManagedServer.InitThreshold);
                    foreach (SecretString key in init.Keys)
                    {
                        SealStatus status = await bootstrap.Sys.UnsealAsync(key.Reveal()!);
                        if (!status.Sealed)
                        {
                            break;
                        }
                    }

                    bootstrapOptions.Token = init.RootToken.Reveal();
                }

                BastionVaultClient nodeClient = new(bootstrapOptions);
                nodeClients.Add(nodeClient);

                // A fixed token id, minted independently on each vault, is what lets one client
                // authenticate no matter which of the three it lands on. "root" itself is refused
                // for an explicit-id create (measured: BV-INPUT-100, "auth methods cannot create
                // root tokens"), so an equivalent full-access policy stands in for it.
                await nodeClient.Sys.WritePolicyAsync("s32-full-access", """
                    path "*" {
                      capabilities = ["create", "read", "update", "delete", "list", "sudo"]
                    }
                    """);
                _ = await nodeClient.Auth.Token.CreateAsync(new CreateTokenRequest { Id = fixedTokenId, Policies = ["s32-full-access"] });

                await nodeClient.Sys.MountAsync(sharedMount, new MountRequest { Type = "kv-v2", Description = "ITG-S32 mirrored secret" });
                _ = await nodeClient.Kv.V2.WriteSecretAsync(
                    sharedPath,
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["value"] = JsonDocument.Parse("\"s32\"").RootElement.Clone(),
                    },
                    sharedMount);
            }

            IReadOnlyList<SrvRecord> records = [.. nodes.Select(n => new SrvRecord(new Uri(n.Address).Host, new Uri(n.Address).Port, 0, 0))];
            cluster = new BastionVaultClient(new BastionVaultClientOptions
            {
                Address = "bvault-itg-s32.test",
                ClusterDiscovery = true,
                // CNF-035 does not recognise this discovery *name* as loopback even though every
                // SRV target it resolves to is; the nodes really are loopback-only managed servers.
                AllowInsecureHttp = true,
                SrvResolver = new FixedSrvResolver(records),
                Discovery = new DiscoveryConfig { DefaultScheme = "http" },
                Token = fixedTokenId,
                Timeout = TimeSpan.FromSeconds(10),
                Logger = Logs,
                Observer = Requests,
            });

            DiscoveryReport report = await cluster.DiscoverAsync();
            Assert.NotNull(report.Picked);
            Assert.Equal(NodeState.ActiveLeader, report.Picked!.State);
            Assert.Equal(report.Picked.Url, report.Ranked[0].Candidate.Url);

            KvV2Secret? initial = await cluster.Kv.V2.ReadSecretAsync(sharedPath, sharedMount);
            Assert.NotNull(initial);
            string pinnedUrl = cluster.SelectedNode!.Url;

            ManagedBinaryServer pinnedNode = nodes.Single(n => string.Equals(new Uri(n.Address).Authority, new Uri(pinnedUrl).Authority, StringComparison.OrdinalIgnoreCase));
            await pinnedNode.DisposeAsync();
            _ = nodes.Remove(pinnedNode);

            // DSC-042: a write is never replayed, so hitting the now-dead pinned node with a write
            // fails outright instead of trying a survivor - this is "during the outage".
            BastionVaultException writeFailure = await Assert.ThrowsAsync<BastionVaultException>(
                () => cluster.Kv.V2.WriteSecretAsync(
                    sharedPath,
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["value"] = JsonDocument.Parse("\"s32-outage\"").RootElement.Clone(),
                    },
                    sharedMount));
            Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, writeFailure.Code);

            // A read, in contrast, gets DSC-042's single bounded replay and succeeds against a
            // surviving node.
            KvV2Secret? afterFailover = await cluster.Kv.V2.ReadSecretAsync(sharedPath, sharedMount);
            Assert.NotNull(afterFailover);
            Assert.NotEqual(pinnedUrl, cluster.SelectedNode!.Url);
        }
        finally
        {
            cluster?.Dispose();
            foreach (BastionVaultClient nodeClient in nodeClients)
            {
                nodeClient.Dispose();
            }

            foreach (ManagedBinaryServer node in nodes)
            {
                await node.DisposeAsync();
            }
        }
    }

    private sealed class FixedSrvResolver(IReadOnlyList<SrvRecord> records) : ISrvResolver
    {
        public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(records);
        }
    }
}
