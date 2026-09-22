namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// The whole-run state ITG-021 and ITG-023 need: which secrets to watch for, and which mount
/// belongs to which specification section. Shared by every scope the harness checks - the
/// orphan sweep (<see cref="IntegrationHarness"/>) and each scenario (<see cref="IntegrationTest"/>)
/// - each of which keeps its own <see cref="LogCapture"/>/<see cref="RequestCapture"/> so a
/// violation is attributed to the call that caused it (see <see cref="RunAssertions.Enforce"/>).
/// </summary>
internal sealed class RunCapture
{
    public SecretWatch Secrets { get; } = new();

    public SectionTally Sections { get; } = new();
}
