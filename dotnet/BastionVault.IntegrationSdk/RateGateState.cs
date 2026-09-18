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
/// <c>RateGate.Burst</c> otherwise. A <b>disabled</b> gate (<see cref="RateGate.IsDisabled"/>)
/// reports <see cref="int.MaxValue"/>, meaning unbounded — it never withholds a request. The
/// sentinel is deliberate rather than the configured <c>Burst</c>: <c>Burst = 0</c> is itself one
/// of the two values that disable the gate, so reporting the burst would have said
/// <c>AvailableTokens = 0</c> — "fully throttled" — for a gate that throttles nothing. The one
/// invariant a caller may rely on is that <c>AvailableTokens &gt; 0</c> means a request may
/// proceed without waiting.
/// </param>
public sealed record RateGateState(bool Paused, DateTimeOffset? PausedUntil, int AvailableTokens);
