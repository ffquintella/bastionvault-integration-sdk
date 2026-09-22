using System.Globalization;
using System.Text;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The diagnostics <see cref="BastionVaultClient.DiscoverAsync"/> returns (DSC-036): the full ranked
/// table, without changing the pinned node.
/// </summary>
/// <param name="InputLabel">The address as configured, verbatim (DSC-035).</param>
/// <param name="Ranked">
/// Every candidate probed, in rank order. Survivors of DSC-030/031/032 come first, ordered by
/// DSC-033; the candidates those rules dropped follow, so an operator sees <i>why</i> a node was not
/// eligible rather than seeing it disappear (this is what RES-020's own example table shows, with
/// its <c>Sealed</c> row).
/// </param>
/// <param name="Picked">The node that would be pinned, or <see langword="null"/> when nothing survived.</param>
public sealed record DiscoveryReport(
    string InputLabel,
    IReadOnlyList<ProbeResult> Ranked,
    NodeSelection? Picked)
{
    /// <summary>
    /// DSC-016: the cause of a DSC-012 degradation, or <see cref="DiscoveryDegradationCause.None"/>.
    /// A property, not a <see cref="Render"/> column (D-R16-12) — <see cref="Render"/>'s output is a
    /// byte-compared shared fixture artefact and must not gain a column for this.
    /// </summary>
    public DiscoveryDegradationCause Degraded { get; init; } = DiscoveryDegradationCause.None;

    private const int TargetWidth = 40;
    private const int PriorityWidth = 5;
    private const int WeightWidth = 4;
    private const int StateWidth = 14;
    private const int RttWidth = 7;

    /// <summary>The placeholder RES-020's table prints for an absent value.</summary>
    private const string Absent = "-";

    /// <summary>
    /// RES-020: renders the table the CLI prints, column-for-column as
    /// <c>specifications/13-cluster-discovery-and-resilience.md</c> lines 143-152 print it.
    /// </summary>
    /// <remarks>
    /// Lines are joined with <c>\n</c> and not the platform separator: the rendering is a specified
    /// output shape shared by three language SDKs, so it must not differ between Windows and
    /// everything else.
    /// </remarks>
    public string Render()
    {
        StringBuilder builder = new();
        _ = builder
            .Append("Target".PadRight(TargetWidth))
            .Append("Pri".PadRight(PriorityWidth))
            .Append("Wt".PadRight(WeightWidth))
            .Append("State".PadRight(StateWidth))
            .Append("RTT(ms)".PadLeft(RttWidth))
            .Append("  ")
            .Append("ClusterId")
            .Append('\n');

        foreach (ProbeResult result in Ranked)
        {
            _ = builder
                .Append(result.Candidate.Url.PadRight(TargetWidth))
                .Append(Number(result.Candidate.Priority).PadRight(PriorityWidth))
                .Append(Number(result.Candidate.Weight).PadRight(WeightWidth))
                .Append(result.State.ToString().PadRight(StateWidth))
                .Append(Rtt(result.RttMs).PadLeft(RttWidth))
                .Append("  ")
                .Append(result.ClusterId ?? Absent)
                .Append('\n');
        }

        // D-M5-25 pins the empty case, which §13:143-152 does not show: `Picked: none`, and `-` in
        // the RTT(ms) column for a probe that returned no round trip. Pinned in the record rather
        // than left to each language because RES-020's output is a shared, asserted artefact.
        _ = builder.Append(
            Picked is { } picked
                ? $"Picked: {picked.Url} ({picked.State}, {Rtt(picked.RttMs)} ms)"
                : "Picked: none");
        return builder.ToString();
    }

    private static string Number(int? value)
    {
        return value is { } present ? present.ToString(CultureInfo.InvariantCulture) : Absent;
    }

    private static string Rtt(double? value)
    {
        return value is { } present
            ? Math.Round(present).ToString("0", CultureInfo.InvariantCulture)
            : Absent;
    }
}
