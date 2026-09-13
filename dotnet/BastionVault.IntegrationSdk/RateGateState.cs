namespace BastionVault.IntegrationSdk;

/// <summary>
/// The observable state of the client-side rate gate (D-M1b-16). Only the pause half of
/// EFF-003/EFF-004/EFF-006 ships at M1b; the token bucket, <c>AvailableTokens</c> and FIFO queueing
/// are M8, and no <c>EFF-*</c> requirement leaves the traceability baseline for this type.
/// </summary>
public sealed record RateGateState(bool Paused, DateTimeOffset? PausedUntil);
