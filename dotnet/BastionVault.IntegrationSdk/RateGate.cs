namespace BastionVault.IntegrationSdk;

/// <summary>
/// Client-side rate gate configuration
/// (<c>specifications/14-batch-and-request-efficiency.md#client-rate-gate</c>). At milestone M1a
/// these values are resolved and validated only (<c>decisions/0003-m1a-configuration.md</c>,
/// D-M1a-13); the token bucket itself is M8.
/// </summary>
public sealed record RateGate
{
    /// <summary>Default 8. <c>0</c> disables the rate gate.</summary>
    public int RatePerSecond { get; init; } = 8;

    /// <summary>Default 16.</summary>
    public int Burst { get; init; } = 16;

    /// <summary>True when <see cref="RatePerSecond"/> is <c>0</c>.</summary>
    public bool IsDisabled => RatePerSecond == 0;
}
