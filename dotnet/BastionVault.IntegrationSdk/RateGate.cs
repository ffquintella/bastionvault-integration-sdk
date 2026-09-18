namespace BastionVault.IntegrationSdk;

/// <summary>
/// Client-side rate gate configuration
/// (<c>specifications/14-batch-and-request-efficiency.md#client-rate-gate</c>). M1a resolved and
/// validated these values (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-13); M8d builds the
/// token bucket itself (EFF-001, <see cref="Internal.ClientRateGate"/>).
/// </summary>
public sealed record RateGate
{
    /// <summary>Default 8. <c>0</c> disables the rate gate.</summary>
    public int RatePerSecond { get; init; } = 8;

    /// <summary>Default 16. <c>0</c> disables the rate gate.</summary>
    public int Burst { get; init; } = 16;

    /// <summary>
    /// EFF-001: <see langword="true"/> when <b>either</b> <see cref="RatePerSecond"/> or
    /// <see cref="Burst"/> is <c>0</c> — "setting either to <c>0</c> disables the gate", read as
    /// written.
    /// </summary>
    /// <remarks>
    /// The <see cref="Burst"/> limb is M8d's correction. Before the token bucket existed this
    /// property had one reader (none on the request path), so the missing limb was invisible;
    /// with the bucket in place a <c>Burst</c> of <c>0</c> and a non-zero <c>RatePerSecond</c>
    /// would have meant "no request may ever proceed without waiting" rather than "off", which is
    /// the opposite of what EFF-001 says. <c>rust/.../rate.rs</c> has read both limbs since M1a
    /// (<c>either_field_at_zero_disables_the_gate</c>), so this closes a parity gap rather than
    /// opening one.
    /// </remarks>
    public bool IsDisabled => RatePerSecond == 0 || Burst == 0;
}
