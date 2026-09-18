namespace BastionVault.IntegrationSdk;

/// <summary>
/// EFF-006: the observable state of the client-side rate gate, for diagnostics (D-M1b-16 landed
/// the pause half; M8d adds <see cref="AvailableTokens"/> with the token bucket).
/// </summary>
/// <param name="Paused">
/// Whether the gate is paused <b>right now</b> — that is, whether <paramref name="PausedUntil"/>
/// is in the future by the client's injected clock. A pause that has elapsed reports
/// <see langword="false"/> while <paramref name="PausedUntil"/> keeps its value, so a caller can
/// still see when the last pause ended.
/// </param>
/// <param name="PausedUntil">
/// When the current (or most recent) EFF-003/EFF-004 pause ends, or <see langword="null"/> when
/// the gate has never been paused.
/// </param>
/// <param name="AvailableTokens">
/// How many requests may proceed immediately without waiting: <c>0</c> while paused, and at most
/// <c>RateGate.Burst</c> otherwise. When the gate is <b>disabled</b>
/// (<see cref="RateGate.IsDisabled"/>) this reports the configured <c>Burst</c> and is not a
/// meaningful measurement — a disabled gate never withholds a token whatever this says. Read
/// <c>Client.Config.RateGate.IsDisabled</c> to tell a disabled gate from a full one.
/// </param>
public sealed record RateGateState(bool Paused, DateTimeOffset? PausedUntil, int AvailableTokens);
