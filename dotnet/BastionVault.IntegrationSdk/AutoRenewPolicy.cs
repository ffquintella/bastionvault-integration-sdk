namespace BastionVault.IntegrationSdk;

/// <summary>
/// Background token renewal configuration
/// (<c>specifications/05-authentication.md#automatic-renewal</c>, AUT-090…AUT-095).
/// </summary>
/// <remarks>
/// <para>
/// Materialised as a disabled value at M1a (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-13);
/// M2c fills in the rest of the shape D-M2-6 pinned and lands the loop behind
/// <see cref="Enabled"/>. No setting here resolves from an environment variable (D-M1a-13), so a
/// renewal loop is only ever started by an application that asked for one in code.
/// </para>
/// </remarks>
public sealed record AutoRenewPolicy
{
    /// <summary>Default <see langword="false"/>. No environment variable resolves this setting.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// AUT-090: the fraction of the lease at which renewal is scheduled —
    /// <c>IssuedAt + LeaseDuration × RenewAtFraction</c>.
    /// </summary>
    public double RenewAtFraction { get; init; } = 0.66;

    /// <summary>AUT-090: renewal is never scheduled sooner than this after the previous renewal.</summary>
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The <c>increment</c> AUT-080's renew request asks for, or <see langword="null"/> for the
    /// server's own default.
    /// </summary>
    public TimeSpan? Increment { get; init; }

    /// <summary>AUT-092: how many consecutive renewal failures are absorbed before the loop stops.</summary>
    public int MaxConsecutiveFailures { get; init; } = 5;

    /// <summary>AUT-091: raised after each successful renewal, carrying the renewed credential.</summary>
    public Action<RenewalEvent>? OnRenewed { get; init; }

    /// <summary>AUT-092: raised on each renewal failure, including the ones the loop then absorbs.</summary>
    public Action<RenewalEvent>? OnFailed { get; init; }

    /// <summary>
    /// Raised exactly once, when the loop stops for good (AUT-092, AUT-093, AUT-094). A loop that
    /// stops emits one reason and never runs again.
    /// </summary>
    public Action<RenewalStoppedReason>? OnStopped { get; init; }
}

/// <summary>Why a renewal loop stopped (AUT-092, AUT-093, AUT-094).</summary>
public enum RenewalStoppedReason
{
    /// <summary>
    /// AUT-092: renewal failed and the loop gave up — either
    /// <see cref="AutoRenewPolicy.MaxConsecutiveFailures"/> consecutive failures, or one of the
    /// failures the requirement stops immediately on (<c>BV-AUTHZ-001</c>, <c>BV-AUTH-015</c>,
    /// <c>BV-SERVER-001</c>).
    /// </summary>
    RenewalFailed,

    /// <summary>
    /// AUT-093: renewal stopped, the client's source was a <see cref="TokenSourceKind.Login"/> one,
    /// and the single fresh login that would have resumed the schedule failed too.
    /// </summary>
    ReloginFailed,

    /// <summary>AUT-092: the token was revoked or cleared (<c>RevokeSelf</c>, <c>ClearToken</c>).</summary>
    TokenRevoked,

    /// <summary>
    /// AUT-090's precondition was never met: the credential is not renewable, or carries no
    /// positive <c>lease_duration</c> (AUT-095's batch and non-renewable tokens), or the client
    /// holds a token it never issued and therefore knows no lease for.
    /// </summary>
    NotRenewable,

    /// <summary>AUT-094: the client was disposed, or the loop's cancellation token fired.</summary>
    Disposed,
}

/// <summary>
/// One renewal attempt, as reported to <see cref="AutoRenewPolicy.OnRenewed"/> and
/// <see cref="AutoRenewPolicy.OnFailed"/> (AUT-091, AUT-092).
/// </summary>
/// <remarks>
/// A sealed class rather than a record, matching <see cref="AuthInfo"/> and <see cref="TokenInfo"/>:
/// the generated record members would enlarge the CNF-027 surface with equality and
/// <c>ToString</c> operators no requirement asks for, and a generated <c>ToString</c> over a type
/// that carries a credential is exactly what CNF-032 exists to prevent.
/// </remarks>
public sealed class RenewalEvent
{
    /// <summary>When the attempt finished, read from the injected clock (never the wall clock).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>
    /// The renewed credential on success (AUT-091's new <c>lease_duration</c> is
    /// <see cref="AuthInfo.LeaseDuration"/>), and <see langword="null"/> on failure.
    /// </summary>
    public AuthInfo? Auth { get; init; }

    /// <summary>The coded failure on failure, and <see langword="null"/> on success.</summary>
    public BastionVaultException? Error { get; init; }

    /// <summary>
    /// AUT-092's counter as it stands after this attempt: <c>0</c> after a success, and the number
    /// of consecutive failures so far after a failure.
    /// </summary>
    public required int ConsecutiveFailures { get; init; }
}
