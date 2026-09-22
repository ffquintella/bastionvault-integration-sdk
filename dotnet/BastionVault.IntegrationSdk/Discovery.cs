using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The health state a candidate probe resolved to, per section 13's classification table. Declared
/// worst-first, so the enum's own numeric order <i>is</i> DSC-033's state rank (higher is better)
/// and no second table is needed to compare two states (D-M5-8).
/// </summary>
public enum NodeState
{
    /// <summary>No answer, a non-JSON body, or a timeout.</summary>
    Unreachable,

    /// <summary><c>initialized == false</c>.</summary>
    Uninitialized,

    /// <summary><c>sealed == true</c>.</summary>
    Sealed,

    /// <summary><c>standby == true</c> or <c>performance_standby == true</c>.</summary>
    Follower,

    /// <summary>Initialised, unsealed and not standing by.</summary>
    ActiveLeader,
}

/// <summary>
/// DSC-016's degradation cause: why discovery fell back to a DSC-012 synthesised literal candidate,
/// or <see cref="None"/> when it did not. A cause, not a boolean, because the remedy differs for
/// each (configure a resolver / fix DNS / raise the timeout), and a legitimately configured DSC-001
/// literal client always reports <see cref="None"/> — it never runs SRV resolution at all.
/// </summary>
public enum DiscoveryDegradationCause
{
    /// <summary>No degradation: either SRV resolution returned records, or this client is literal.</summary>
    None,

    /// <summary>No <see cref="ISrvResolver"/> was supplied to the client.</summary>
    NoResolverConfigured,

    /// <summary>The resolver ran and returned zero records.</summary>
    ResolverReturnedNoRecords,

    /// <summary>The resolver threw.</summary>
    ResolverFailed,

    /// <summary><see cref="DiscoveryConfig.ResolveTimeout"/> elapsed before the resolver answered.</summary>
    ResolveTimedOut,
}

/// <summary>
/// DNS SRV discovery settings (section 13, "SRV discovery"). Constructor-settable through
/// <see cref="BastionVaultClientOptions.Discovery"/> only: none of these five has a row in
/// <c>specifications/02-client-configuration.md</c>'s settings table and none has a
/// <c>BASTIONVAULT_*</c> variable, and minting one would be a specification change (D-M5-19,
/// D-R16-5 for <see cref="StrictDiscovery"/>).
/// </summary>
public sealed record DiscoveryConfig
{
    /// <summary>The SRV service prefix prepended to a bare cluster name (DSC-010).</summary>
    public string SrvService { get; init; } = "_bvault._tcp";

    /// <summary>The scheme used for a synthesised candidate URL (DSC-012, DSC-013).</summary>
    public string DefaultScheme { get; init; } = "https";

    /// <summary>The port used when an address carries none (DSC-012, DSC-013).</summary>
    public int DefaultPort { get; init; } = 8200;

    /// <summary>How long the SRV lookup may take before it counts as "no records" (DSC-011).</summary>
    public TimeSpan ResolveTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// DSC-017: when <see langword="true"/> (the default, D-R16-5), a non-SRV-shaped cluster name
    /// that yields no SRV records raises <c>BV-DISCOVERY-004</c> instead of synthesising a DSC-012
    /// literal candidate. An SRV-shaped name (<c>_…</c>) with no records already raises via
    /// DSC-012's other arm (<c>BV-DISCOVERY-001</c>) regardless of this setting.
    /// </summary>
    public bool StrictDiscovery { get; init; } = true;

    /// <summary>
    /// DSC-050: the nameservers the built-in default <see cref="ISrvResolver"/> queries.
    /// <see langword="null"/> (the default) means query the platform's configured nameservers; an
    /// explicit list, when set, overrides platform discovery entirely and is used instead — the
    /// operator remedy for a scoped resolver platform discovery cannot see (D-R16-6, D-R16-7), and
    /// what makes the default resolver testable against an in-process fake DNS server without
    /// touching the host's own resolver configuration. Has no effect when an application supplies
    /// its own <see cref="BastionVaultClientOptions.SrvResolver"/> — an injected resolver always
    /// wins and never sees this setting.
    /// </summary>
    public IReadOnlyList<IPEndPoint>? Nameservers { get; init; }
}

/// <summary>
/// Health-probe settings (section 13, "Health probing").
/// </summary>
/// <remarks>
/// <see cref="ProbeTimeout"/> is populated from the already-shipped
/// <see cref="ClientConfig.DiscoveryProbeTimeout"/> setting rather than being a second knob for one
/// value: an explicit <see cref="BastionVaultClientOptions.Health"/> wins, otherwise the resolved
/// setting wins, otherwise the literal default below (D-M5-8, ruling 4).
/// </remarks>
public sealed record HealthConfig
{
    /// <summary>Per-candidate probe timeout.</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>The maximum number of probes in flight at once (DSC-020).</summary>
    public int Parallelism { get; init; } = 4;
}

/// <summary>One DNS SRV answer, as the resolver returned it (DSC-010).</summary>
/// <param name="Target">The target host name. A trailing dot is stripped by the SDK, not by the resolver.</param>
/// <param name="Port">The target port.</param>
/// <param name="Priority">The SRV priority; lower wins, and DSC-031 makes it a hard floor.</param>
/// <param name="Weight">The SRV weight; higher wins, as a deterministic tiebreak only (DSC-033).</param>
public sealed record SrvRecord(string Target, int Port, int Priority, int Weight);

/// <summary>
/// DSC-014's injectable resolver seam, so a test can supply fake SRV answers without a DNS server.
/// </summary>
public interface ISrvResolver
{
    /// <summary>
    /// Resolves SRV records for <paramref name="ownerName"/>. An empty result and a thrown failure
    /// are treated identically by the SDK (DSC-011), so an implementation may choose either.
    /// </summary>
    public Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default);
}

/// <summary>A node the SDK may probe and pin (DSC-013).</summary>
/// <param name="Url">Always <c>{scheme}://{target}:{port}</c>, with the port explicit.</param>
/// <param name="Target">The SRV target host name, which is also the SNI/verification name (RES-010, CFG-043).</param>
/// <param name="Port">The port.</param>
/// <param name="Priority">
/// The SRV priority, or <see langword="null"/> on a DSC-012 synthesised literal candidate, which has
/// no SRV record behind it. <see langword="null"/> and not <c>0</c> or <c>-1</c>: three languages
/// would otherwise each pick a different sentinel and the shared
/// <c>resilience.address.classification</c> fixture would stop being portable (D-M5-8, RF-5a).
/// </param>
/// <param name="Weight">The SRV weight, <see langword="null"/> on a synthesised candidate, for the same reason.</param>
[SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Justification = "D-M5-8 pins Url as a string, because it is the value the shared fixtures compare and the three language SDKs must agree on character-for-character; System.Uri would re-render it.")]
[SuppressMessage(
    "Design",
    "CA1056:URI-like properties should not be strings",
    Justification = "D-M5-8 pins Url as a string, because it is the value the shared fixtures compare and the three language SDKs must agree on character-for-character; System.Uri would re-render it.")]
public sealed record Candidate(string Url, string Target, int Port, int? Priority, int? Weight);

/// <summary>One candidate's probe outcome (DSC-021).</summary>
/// <param name="Candidate">The candidate probed.</param>
/// <param name="State">The classified state.</param>
/// <param name="RttMs">The round trip in milliseconds, or <see langword="null"/> when there was no answer.</param>
public sealed record ProbeResult(Candidate Candidate, NodeState State, double? RttMs)
{
    /// <summary>
    /// The body's <c>cluster_healthy</c> field. DSC-022: it MUST NOT by itself change
    /// <see cref="State"/>; it is surfaced here so ranking can prefer healthy nodes.
    /// </summary>
    public bool ClusterHealthy { get; init; } = true;

    /// <summary>The body's <c>cluster_id</c>, absent on current servers and kept for forward-compat (DSC-021, DSC-032).</summary>
    public string? ClusterId { get; init; }

    /// <summary>The body's <c>version</c>, absent on current servers and kept for forward-compat (DSC-021).</summary>
    public string? Version { get; init; }
}

/// <summary>
/// The node discovery pinned (DSC-035). The type is <c>NodeSelection</c> while the client member is
/// <c>SelectedNode</c>: a type of that name would give <c>public SelectedNode SelectedNode</c>,
/// C#'s "Color Color" hazard, for no gain (D-M5-8, ruling 1).
/// </summary>
/// <param name="Url">The pinned node's URL.</param>
/// <param name="State">The state its probe reported.</param>
/// <param name="RttMs">Its probe round trip in milliseconds.</param>
[SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Justification = "D-M5-8 pins Url as a string, because it is the value the shared fixtures compare and the three language SDKs must agree on character-for-character; System.Uri would re-render it.")]
[SuppressMessage(
    "Design",
    "CA1056:URI-like properties should not be strings",
    Justification = "D-M5-8 pins Url as a string, because it is the value the shared fixtures compare and the three language SDKs must agree on character-for-character; System.Uri would re-render it.")]
public sealed record NodeSelection(string Url, NodeState State, double? RttMs)
{
    /// <summary>The pinned node's <c>cluster_id</c>, when it reported one.</summary>
    public string? ClusterId { get; init; }

    /// <summary>The pinned node's <c>version</c>, when it reported one.</summary>
    public string? Version { get; init; }
}
