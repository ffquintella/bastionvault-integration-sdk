using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Section 13's discovery pipeline: SRV resolution (DSC-010…014), health probing (DSC-020…022),
/// node ranking and picking (DSC-030…034), and the pin the sticky session reads (DSC-035, DSC-036).
/// </summary>
/// <remarks>
/// <para>
/// One engine per <see cref="ClientContext"/>, so every <see cref="BastionVaultClient.WithNamespace"/>
/// view shares one pin. Discovery runs <b>lazily</b>, before the first operation's first attempt, and
/// exactly once: <see cref="ConnectAsync"/> is idempotent and
/// <see cref="ReconnectAsync"/> is the only way to re-run it (D-M5-9).
/// </para>
/// <para>
/// Probes go through the client's one <see cref="ITransport"/> and not a private HTTP path. That is
/// what makes RES-011 ("the same TLS material as the eventual requests") and RES-010/CFG-043 (SNI
/// follows the request URI's host, which is built from the SRV target) true by construction rather
/// than by a second TLS configuration that has to be kept in step (D-M5-16, D-M5-16a).
/// </para>
/// <para>
/// This slice pins a node and nothing moves off it. Failover (DSC-040…DSC-046) is M5b's; the cached
/// candidate set <see cref="Candidates"/> is kept because that is the set M5b re-probes without a
/// second SRV lookup, and keeping it costs nothing now.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The gate lives exactly as long as the ClientContext that owns it, holds no unmanaged resource (SemaphoreSlim allocates a wait handle only if AvailableWaitHandle is read, which this type never does), and making the engine disposable would put a Dispose on a WithNamespace view able to break its parent's discovery.")]
internal sealed class DiscoveryEngine
{
    /// <summary>
    /// The probe path, as section 13's probe definition spells it: <c>GET {candidate}/v1/sys/health</c>.
    /// Literally <c>v1</c> and not <see cref="ClientConfig.ApiPrefix"/> — the requirement names the
    /// version, and a client pinned to <c>v2</c> would otherwise probe a path section 13 never names.
    /// </summary>
    private const string ProbePath = "/v1/sys/health";

    private readonly ClientContext context;
    private readonly AddressClassifier.Classification classification;
    private readonly ISrvResolver? resolver;

    /// <summary>
    /// D-M5-12's one <see cref="SemaphoreSlim"/> per <see cref="ClientContext"/>. It serialises
    /// <b>every</b> mutation of the pin — lazy discovery, <see cref="ReconnectAsync"/> and
    /// <see cref="TryFailoverAsync"/> alike — rather than one lock per entry point, so a failover
    /// racing a reconnect cannot interleave two writes to <see cref="endpoint"/>. DSC-043's
    /// late-arrival rule then falls out of it: whoever acquires the lock second sees the pin the
    /// first one moved.
    /// </summary>
    private readonly SemaphoreSlim gate = new(1, 1);

    private volatile string endpoint;
    private volatile NodeSelection? selected;
    private volatile IReadOnlyList<Candidate>? candidates;

    public DiscoveryEngine(ClientContext context, AddressClassifier.Classification classification, ISrvResolver? resolver)
    {
        this.context = context;
        this.classification = classification;
        this.resolver = resolver;
        // In literal mode this is the configured address verbatim and never changes, so every
        // request URI is byte-identical to the one M1b built (D-M5-11).
        endpoint = classification.Endpoint ?? context.Config.Address;
    }

    /// <summary>The base address each attempt builds its URI from, read at the top of the attempt (D-M5-11).</summary>
    public string Endpoint => endpoint;

    /// <summary>
    /// DSC-035's <c>Client.SelectedNode</c>. <see langword="null"/> in literal mode: DSC-001 makes
    /// literal mode "no DNS, no probing", so nothing was probed and nothing was chosen, and
    /// reporting a literal URL as <c>ActiveLeader</c> would state a health claim the SDK never
    /// verified (D-M5-10).
    /// </summary>
    public NodeSelection? SelectedNode => selected;

    /// <summary>The cached candidate set discovery produced, or <see langword="null"/> before it ran.</summary>
    public IReadOnlyList<Candidate>? Candidates => candidates;

    /// <summary>
    /// RES-030's candidate set: <b>every</b> discovered candidate, unprobed and unfiltered,
    /// including the sealed and the unreachable.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Rank"/>'s output and deliberately not
    /// <see cref="ConnectAsync"/>'s pick: DSC-030…033 exist to choose <i>one</i> node to talk to,
    /// and RES-030 exists to reach <i>all</i> of them. Resolution is reused when it has already
    /// run, so a <c>*ClusterWide</c> call on a connected client costs no extra DNS. A literal-mode
    /// client has exactly one candidate — the configured address — because DSC-001 makes literal
    /// mode "no DNS, no probing", and "all discovered candidates" is then a set of one rather than
    /// an error.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ClusterWideEndpointsAsync(CancellationToken cancellationToken)
    {
        if (!IsDiscoveryMode)
        {
            return [endpoint];
        }

        IReadOnlyList<Candidate> resolved = candidates
            ?? await ResolveCandidatesAsync(cancellationToken).ConfigureAwait(false);
        return [.. resolved.Select(candidate => candidate.Url)];
    }

    /// <summary>Whether this client's address triggers SRV discovery (DSC-001).</summary>
    public bool IsDiscoveryMode => classification.IsDiscovery;

    /// <summary>
    /// DSC-042's arming condition: discovery produced two or more candidates. A literal client and a
    /// single-candidate cluster are both unarmed, so a node failure on either is terminal
    /// (DSC-044).
    /// </summary>
    public bool IsFailoverArmed => IsDiscoveryMode && candidates is { Count: >= 2 };

    /// <summary>DSC-035's <c>Client.InputLabel</c>: the raw configured address, in both modes (D-M5-10).</summary>
    public string InputLabel => context.Config.Address;

    /// <summary>
    /// Runs discovery if it has not run, and returns the pin. Idempotent: a second call on a pinned
    /// client returns the existing pick without re-probing (D-M5-9).
    /// </summary>
    /// <returns>
    /// The pinned node, or <see langword="null"/> on a literal-mode client. DSC-001 makes literal
    /// mode "no DNS, no probing", so there is nothing to run and nothing was chosen; returning a
    /// <see cref="NodeSelection"/> for the configured endpoint would state a health state the SDK
    /// never verified, which D-M5-10 forbids (D-M5-8 as amended at revision 3).
    /// </returns>
    public async Task<NodeSelection?> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!IsDiscoveryMode)
        {
            return null;
        }

        if (selected is { } already)
        {
            return already;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-checked under the lock: two first operations racing must produce one discovery.
            return selected ?? await RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>
    /// Re-runs full discovery (SRV plus probe) and re-pins. Safe to call concurrently, because it
    /// holds the same lock <see cref="ConnectAsync"/> does. <see langword="null"/> on a literal-mode
    /// client, for the reason <see cref="ConnectAsync"/> gives.
    /// </summary>
    public async Task<NodeSelection?> ReconnectAsync(CancellationToken cancellationToken)
    {
        if (!IsDiscoveryMode)
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>
    /// DSC-036: the full ranked table, without changing the pinned node and without arming or
    /// disarming anything. On a literal client it is a one-row report whose single candidate
    /// <b>is</b> probed — an operator asking for diagnostics has asked for the probe, and no pick
    /// results from it (D-M5-10).
    /// </summary>
    public async Task<DiscoveryReport> DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Candidate> probeSet = IsDiscoveryMode
            ? await ResolveCandidatesAsync(cancellationToken).ConfigureAwait(false)
            : [classification.Literal!];
        IReadOnlyList<ProbeResult> probes = await ProbeAllAsync(probeSet, cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<ProbeResult> ranked, NodeSelection? picked) = Rank(probes);
        return new DiscoveryReport(InputLabel, ranked, picked);
    }

    /// <summary>
    /// D-M5-9's lazy hook, called by <see cref="RequestExecutor"/> before an operation's first
    /// attempt. A no-op for a literal client, which is every client in the landed corpus.
    /// </summary>
    public Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        return !IsDiscoveryMode || selected is not null
            ? Task.CompletedTask
            : ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// DSC-030…034: drops ineligible candidates, applies the priority floor and the dominant
    /// cluster id, and orders every probe for the diagnostics table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned list carries <b>every</b> candidate probed, not only the survivors: RES-020's own
    /// example table prints a <c>Sealed</c> row, which DSC-030 had dropped, and an operator who
    /// cannot see the rejected nodes cannot see why nothing was picked (RES-021 is the same idea for
    /// the error).
    /// </para>
    /// <para>
    /// One comparator, total and deterministic, never RFC 2782 weighted-random (DSC-033). Priority
    /// sorts ahead of state, which is DSC-010's ascending sort and does not disturb DSC-033's own
    /// order: DSC-031 has already reduced the survivors to one priority, so priority can only
    /// discriminate among the <i>rejected</i> tail.
    /// </para>
    /// </remarks>
    public static (IReadOnlyList<ProbeResult> Ranked, NodeSelection? Picked) Rank(IReadOnlyList<ProbeResult> probes)
    {
        bool[] survives = new bool[probes.Count];
        for (int index = 0; index < probes.Count; index++)
        {
            // DSC-030.
            survives[index] = probes[index].State is NodeState.ActiveLeader or NodeState.Follower;
        }

        ApplyPriorityFloor(probes, survives);
        ApplyDominantClusterId(probes, survives);

        int[] order = Enumerable.Range(0, probes.Count).ToArray();
        Array.Sort(order, (left, right) => Compare(probes, survives, left, right));
        ProbeResult[] ranked = Array.ConvertAll(order, index => probes[index]);

        foreach (int index in order)
        {
            if (survives[index])
            {
                ProbeResult best = probes[index];
                return (ranked, new NodeSelection(best.Candidate.Url, best.State, best.RttMs)
                {
                    ClusterId = best.ClusterId,
                    Version = best.Version,
                });
            }
        }

        // DSC-034: no survivor. The caller decides whether that is an error (a pick) or a row in a
        // diagnostics table (Client.Discover).
        return (ranked, null);
    }

    /// <summary>DSC-034 / RES-021: <c>BV-DISCOVERY-002</c>, carrying <c>target=state</c> for every probe.</summary>
    public static BastionVaultException NoHealthyNode(IReadOnlyList<ProbeResult> probes)
    {
        string[] states = probes
            .Select(probe => $"{probe.Candidate.Target}={probe.State}")
            .ToArray();
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.DiscoveryNoHealthyNode);
        return BastionVaultException.Request(
            ErrorCodes.DiscoveryNoHealthyNode,
            entry.Category,
            entry.Message,
            // RES-021: the operator must see *why* nothing was picked, so the list is in the hint
            // and not only in Details.
            $"{entry.Hint} Candidates: {string.Join(", ", states)}.",
            retryable: entry.Retryable,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["candidates"] = states });
    }

    /// <summary>DSC-012: an SRV-shaped name that resolved to nothing has no candidate to synthesise.</summary>
    public static BastionVaultException NoCandidates(string ownerName)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.DiscoveryNoCandidates);
        return BastionVaultException.Request(
            ErrorCodes.DiscoveryNoCandidates,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["ownerName"] = ownerName });
    }

    /// <summary>
    /// DSC-010…013: the SRV answers for this client's cluster name, as candidates in the order the
    /// resolver returned them, with trailing dots stripped.
    /// </summary>
    /// <remarks>
    /// The <i>probe</i> order is the resolver's order, which is what
    /// <c>resilience.pick.priority-floor</c> pins on the wire; DSC-010's ascending-priority sort is
    /// applied in <see cref="Rank"/>, where priority is what it decides.
    /// </remarks>
    public async Task<IReadOnlyList<Candidate>> ResolveCandidatesAsync(CancellationToken cancellationToken)
    {
        DiscoveryConfig discovery = context.Config.Discovery;
        string ownerName = AddressClassifier.SrvOwnerName(classification.OwnerName!, discovery);
        IReadOnlyList<SrvRecord> records = await ResolveAsync(ownerName, discovery, cancellationToken).ConfigureAwait(false);
        if (records.Count > 0)
        {
            return records
                .Select(record =>
                {
                    string target = record.Target.TrimEnd('.');
                    return new Candidate(
                        AddressClassifier.CandidateUrl(classification.Scheme, target, record.Port),
                        target,
                        record.Port,
                        record.Priority,
                        record.Weight);
                })
                .ToArray();
        }

        // DSC-012's two arms: an SRV owner name that answered nothing is a hard failure, because
        // there is no host name to fall back to; a plain cluster name synthesises exactly one
        // literal candidate.
        if (classification.OwnerName!.StartsWith('_'))
        {
            throw NoCandidates(ownerName);
        }

        string fallback = classification.OwnerName!;
        return
        [
            new Candidate(
                AddressClassifier.CandidateUrl(classification.Scheme, fallback, discovery.DefaultPort),
                fallback,
                discovery.DefaultPort,
                null,
                null),
        ];
    }

    /// <summary>
    /// DSC-020: probes every candidate, at most <see cref="HealthConfig.Parallelism"/> in flight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>D-M5-22's invariant: the Nth candidate's probe is issued before the (N+1)th</b>, including
    /// when a bounded slot has to free up first. Completions may interleave freely — DSC-020 bounds
    /// concurrency and specifies no wire order — but every scripted fixture matches exchanges by
    /// <i>sequence</i>, so six shared fixtures would become order-nondeterministic if issue order
    /// were left to the runtime.
    /// </para>
    /// <para>
    /// The mechanism, which is why this is a sequential loop and not a fan-out: the slot is acquired
    /// by <b>this</b> loop, so the loop body for candidate N+1 cannot start until a slot is free,
    /// and invoking <c>ProbeAndReleaseAsync(N)</c> runs synchronously into
    /// <see cref="ProbeAsync"/> up to its <c>SendAsync</c> call — so the Nth request has been
    /// handed to the transport before the (N+1)th is launched, whether or not the transport then
    /// yields. The previous shape (launch every probe, let each await the semaphore) produced the
    /// same order only because <c>WaitAsync</c> happened to return an already-completed task, which
    /// is a runtime habit and not a contract.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ProbeResult>> ProbeAllAsync(
        IReadOnlyList<Candidate> probeSet,
        CancellationToken cancellationToken)
    {
        ProbeResult[] results = new ProbeResult[probeSet.Count];
        using SemaphoreSlim slots = new(Math.Max(1, context.Config.Health.Parallelism));
        List<Task> probes = new(probeSet.Count);
        try
        {
            for (int index = 0; index < probeSet.Count; index++)
            {
                await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                probes.Add(ProbeAndReleaseAsync(index));
            }
        }
        finally
        {
            // In the `finally` so a cancellation between two launches still waits for the probes
            // already in flight: they hold slots on a semaphore this scope is about to dispose.
            await Task.WhenAll(probes).ConfigureAwait(false);
        }

        return results;

        async Task ProbeAndReleaseAsync(int index)
        {
            try
            {
                results[index] = await ProbeAsync(probeSet[index], cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _ = slots.Release();
            }
        }
    }

    /// <summary>
    /// One probe: <c>GET {candidate}/v1/sys/health</c> with <c>Accept: application/json</c>, no
    /// token, and no rate gate (D-M5-17). Any HTTP status is accepted and the body is authoritative.
    /// </summary>
    public async Task<ProbeResult> ProbeAsync(Candidate candidate, CancellationToken cancellationToken)
    {
        ClientConfig config = context.Config;
        TransportRequest request = new(
            "GET",
            new Uri(candidate.Url + ProbePath, UriKind.Absolute),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Accept"] = "application/json" },
            ReadOnlyMemory<byte>.Empty)
        {
            Timeout = config.Health.ProbeTimeout,
            ConnectTimeout = config.ConnectTimeout,
            MaxResponseBytes = config.MaxResponseBytes,
        };

        if (context.Transport is null)
        {
            throw new InvalidOperationException("No transport is configured on this client (OVR-001).");
        }

        // EFF-005, stated rather than implied: a probe is exempt from the client rate gate
        // ("they are exempt server-side too"), and this call is how that exemption is expressed.
        // The alternative — not calling the gate at all, which is what this method did before
        // M8d — is indistinguishable from an oversight to anyone auditing EFF-001's "every
        // outgoing request", and D-M8-26 rules that "never gated" must be legible as such.
        await context.RateGate.AcquireAsync(EgressKind.DiscoveryProbe, cancellationToken).ConfigureAwait(false);

        DateTimeOffset started = context.Clock.NowUtc();
        TransportResponse? response = null;
        string? errorCode = null;
        try
        {
            response = await context.Transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException failure)
        {
            // Any transport-level failure is "no answer", which section 13's table classifies as
            // Unreachable. It is not a caller-visible error: a probe is how the SDK finds out.
            errorCode = failure.Code;
        }

        double elapsed = (context.Clock.NowUtc() - started).TotalMilliseconds;
        // RES-002 says every attempt is reported. A probe has no attempt number, so it is reported
        // with attempt 0 (D-M5-17) — it is not a caller attempt and does not appear in Error.Attempts.
        context.Observer?.OnRequestCompleted(new RequestEvent(
            Method: "GET",
            Path: "sys/health",
            Namespace: config.Namespace,
            StatusCode: response?.StatusCode,
            Duration: TimeSpan.FromMilliseconds(elapsed),
            RequestId: Guid.NewGuid().ToString("n"),
            Attempt: 0,
            ErrorCode: errorCode));

        if (response is null || !TryParseObject(response.Body, out JsonElement body))
        {
            return new ProbeResult(candidate, NodeState.Unreachable, null);
        }

        bool? initialized = Flag(body, "initialized");
        bool? isSealed = Flag(body, "sealed");
        bool? standby = Flag(body, "standby");
        bool? performanceStandby = Flag(body, "performance_standby");
        NodeState state = initialized == false ? NodeState.Uninitialized
            : isSealed == true ? NodeState.Sealed
            : standby == true || performanceStandby == true ? NodeState.Follower
            : NodeState.ActiveLeader;

        return new ProbeResult(candidate, state, elapsed)
        {
            // DSC-022: cluster_healthy MUST NOT by itself change the state.
            ClusterHealthy = Flag(body, "cluster_healthy") ?? true,
            ClusterId = Text(body, "cluster_id"),
            Version = Text(body, "version"),
        };
    }

    /// <summary>
    /// DSC-042's single failover step: re-probe the <b>cached</b> candidate set with no new SRV
    /// lookup, exclude the URL that failed, pick, and swap the pin.
    /// </summary>
    /// <param name="failedEndpoint">The endpoint the caller's attempt actually failed on.</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <returns>
    /// The node now pinned, or <see langword="null"/> when there is nowhere to move — which is
    /// DSC-044's "failover is not armed or the replay also fails" arm and leaves the pin alone.
    /// </returns>
    /// <remarks>
    /// D-M5-12's late arrival: a task that acquires the lock after another has already moved off
    /// the dead node compares the endpoint it failed on with the endpoint now pinned, finds they
    /// differ, and <b>reuses the new pick without re-probing</b>. That is why the comparison is
    /// against <paramref name="failedEndpoint"/> and not against a snapshot taken here — by the
    /// time the lock is free, "the current pin" may already be the answer.
    /// </remarks>
    public async Task<NodeSelection?> TryFailoverAsync(string failedEndpoint, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.Equals(endpoint, failedEndpoint, StringComparison.Ordinal))
            {
                // DSC-043: another task already moved. Reuse its pick; do not probe again.
                return selected;
            }

            if (candidates is not { Count: >= 2 } cached)
            {
                return null;
            }

            // DSC-042: exclude the failed URL. No guard for an empty remainder — an empty probe
            // set ranks to no pick, which the `picked is null` arm below already answers, and a
            // second guard for the same outcome would be an unreachable branch (D-M1c-25).
            Candidate[] remaining = cached
                .Where(candidate => !string.Equals(candidate.Url, failedEndpoint, StringComparison.Ordinal))
                .ToArray();
            IReadOnlyList<ProbeResult> probes = await ProbeAllAsync(remaining, cancellationToken).ConfigureAwait(false);
            (IReadOnlyList<ProbeResult> _, NodeSelection? picked) = Rank(probes);
            if (picked is null)
            {
                return null;
            }

            endpoint = picked.Url;
            selected = picked;
            return picked;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <summary>
    /// Seeds an already-pinned node and an already-cached candidate set, which is what the fixture
    /// instruments <c>settings.__pinned</c> and <c>settings.__candidates</c> configure (D-M5-15).
    /// </summary>
    /// <remarks>
    /// Internal and reached only through the test assembly's <c>InternalsVisibleTo</c>. It writes
    /// exactly the three fields <see cref="RunAsync"/> writes and nothing else, so a seeded client
    /// is indistinguishable from one that ran discovery — which is the point: a failover fixture
    /// asserts the failover, and re-scripting the initial discovery in every one of them would be
    /// four more exchanges per fixture for no extra coverage.
    /// </remarks>
    internal void Seed(NodeSelection pinned, IReadOnlyList<Candidate> cached)
    {
        candidates = cached;
        endpoint = pinned.Url;
        selected = pinned;
    }

    private async Task<NodeSelection> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Candidate> resolved = await ResolveCandidatesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProbeResult> probes = await ProbeAllAsync(resolved, cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<ProbeResult> _, NodeSelection? picked) = Rank(probes);
        if (picked is null)
        {
            throw NoHealthyNode(probes);
        }

        candidates = resolved;
        endpoint = picked.Url;
        selected = picked;
        return picked;
    }

    /// <summary>DSC-011: a resolver failure — including no resolver at all — is "no records", never propagated.</summary>
    private async Task<IReadOnlyList<SrvRecord>> ResolveAsync(
        string ownerName,
        DiscoveryConfig discovery,
        CancellationToken cancellationToken)
    {
        if (resolver is null)
        {
            return [];
        }

        // The SRV lookup is DNS, not HTTP: it never reaches the cluster's abuse guard and is
        // therefore exempt permanently, not merely un-gated for now (D-M8-26). Claimed by name so
        // the difference is readable at the call site. DR-0014's shipped resolver will inherit
        // this without a second decision.
        await context.RateGate.AcquireAsync(EgressKind.SrvResolution, cancellationToken).ConfigureAwait(false);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(discovery.ResolveTimeout);
        try
        {
            return await resolver.ResolveAsync(ownerName, timeout.Token).ConfigureAwait(false) ?? [];
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The filter keeps the *caller's* cancellation a cancellation: only the resolver's own
            // failure, and our own ResolveTimeout, become "no records".
            return [];
        }
    }

    private static void ApplyPriorityFloor(IReadOnlyList<ProbeResult> probes, bool[] survives)
    {
        int floor = int.MaxValue;
        for (int index = 0; index < probes.Count; index++)
        {
            if (survives[index])
            {
                floor = Math.Min(floor, Priority(probes[index]));
            }
        }

        for (int index = 0; index < probes.Count; index++)
        {
            if (survives[index] && Priority(probes[index]) != floor)
            {
                // DSC-031: priority is a hard floor, so a higher-priority follower beats a
                // lower-priority leader.
                survives[index] = false;
            }
        }
    }

    /// <summary>
    /// DSC-032: drop survivors outside the dominant <c>cluster_id</c>. Candidates without one are
    /// kept. Ties on frequency are broken by best state rank, then lowest priority.
    /// </summary>
    private static void ApplyDominantClusterId(IReadOnlyList<ProbeResult> probes, bool[] survives)
    {
        Dictionary<string, (int Count, NodeState Best, int Priority)> groups = new(StringComparer.Ordinal);
        for (int index = 0; index < probes.Count; index++)
        {
            if (!survives[index] || probes[index].ClusterId is not { } clusterId)
            {
                continue;
            }

            int priority = Priority(probes[index]);
            NodeState state = probes[index].State;
            groups[clusterId] = groups.TryGetValue(clusterId, out (int Count, NodeState Best, int Priority) existing)
                ? (existing.Count + 1, existing.Best > state ? existing.Best : state, Math.Min(existing.Priority, priority))
                : (1, state, priority);
        }

        if (groups.Count == 0)
        {
            return;
        }

        string dominant = groups
            .OrderByDescending(group => group.Value.Count)
            .ThenByDescending(group => group.Value.Best)
            .ThenBy(group => group.Value.Priority)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .First()
            .Key;

        for (int index = 0; index < probes.Count; index++)
        {
            if (survives[index] && probes[index].ClusterId is { } clusterId && !string.Equals(clusterId, dominant, StringComparison.Ordinal))
            {
                survives[index] = false;
            }
        }
    }

    private static int Compare(IReadOnlyList<ProbeResult> probes, bool[] survives, int left, int right)
    {
        if (survives[left] != survives[right])
        {
            return survives[left] ? -1 : 1;
        }

        int byPriority = Priority(probes[left]).CompareTo(Priority(probes[right]));
        if (byPriority != 0)
        {
            return byPriority;
        }

        // DSC-033: ActiveLeader before Follower, which the enum's own order already encodes.
        int byState = probes[right].State.CompareTo(probes[left].State);
        if (byState != 0)
        {
            return byState;
        }

        int byRtt = Rtt(probes[left]).CompareTo(Rtt(probes[right]));
        if (byRtt != 0)
        {
            return byRtt;
        }

        int byWeight = Weight(probes[right]).CompareTo(Weight(probes[left]));
        if (byWeight != 0)
        {
            return byWeight;
        }

        int byUrl = string.CompareOrdinal(probes[left].Candidate.Url, probes[right].Candidate.Url);
        // The index tiebreak makes the order total even for two candidates sharing a URL, so the
        // unstable Array.Sort cannot make the result depend on the input order.
        return byUrl != 0 ? byUrl : left.CompareTo(right);
    }

    /// <summary>
    /// A synthesised DSC-012 candidate has no SRV record, so no priority and no weight. Zero is the
    /// comparison stand-in — never the reported value, which stays <see langword="null"/> (D-M5-8).
    /// </summary>
    private static int Priority(ProbeResult probe)
    {
        return probe.Candidate.Priority ?? 0;
    }

    private static int Weight(ProbeResult probe)
    {
        return probe.Candidate.Weight ?? 0;
    }

    /// <summary>An unmeasured round trip sorts last, never first.</summary>
    private static double Rtt(ProbeResult probe)
    {
        return probe.RttMs ?? double.MaxValue;
    }

    private static bool TryParseObject(ReadOnlyMemory<byte> body, out JsonElement element)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            element = document.RootElement.Clone();
            return element.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    private static bool? Flag(JsonElement body, string name)
    {
        return body.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static string? Text(JsonElement body, string name)
    {
        return body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
