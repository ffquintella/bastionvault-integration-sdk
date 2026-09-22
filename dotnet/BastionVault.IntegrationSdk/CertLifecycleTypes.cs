namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §Cert lifecycle: <c>CertLifecycle.ReadTarget</c>/<c>WriteTarget</c>'s shared field list
/// (<c>{mount}/targets/{name}</c>). Patch-shaped (OVR-007), like <see cref="PkiRole"/>: a
/// <see langword="null"/> member is omitted on write and means "the server never returned this
/// field" on read.
/// </summary>
public sealed class Target
{
    /// <summary>The wire <c>kind</c> field. Server default <c>file</c> when omitted.</summary>
    public string? Kind { get; init; }

    /// <summary>The wire <c>address</c> field.</summary>
    public string? Address { get; init; }

    /// <summary>The wire <c>pki_mount</c> field. Server default <c>pki</c> when omitted.</summary>
    public string? PkiMount { get; init; }

    /// <summary>The wire <c>role_ref</c> field.</summary>
    public string? RoleRef { get; init; }

    /// <summary>The wire <c>common_name</c> field.</summary>
    public string? CommonName { get; init; }

    /// <summary>The wire <c>alt_names</c> array.</summary>
    public IReadOnlyList<string>? AltNames { get; init; }

    /// <summary>The wire <c>ip_sans</c> array.</summary>
    public IReadOnlyList<string>? IpSans { get; init; }

    /// <summary>The wire <c>ttl</c> field, in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>The wire <c>key_policy</c> field: <c>rotate</c>, <c>reuse</c>, or <c>agent-generates</c>.</summary>
    public string? KeyPolicy { get; init; }

    /// <summary>The wire <c>key_ref</c> field.</summary>
    public string? KeyRef { get; init; }

    /// <summary>The wire <c>renew_before</c> field, in seconds. Server default 168h (604800s) when omitted.</summary>
    public TimeSpan? RenewBefore { get; init; }
}

/// <summary>12 §Cert lifecycle: <c>CertLifecycle.State</c>'s result (<c>GET {mount}/state/{name}</c>).</summary>
public sealed class TargetState
{
    /// <summary>The wire <c>current_serial</c> field, when a certificate has been issued for this target.</summary>
    public string? CurrentSerial { get; init; }

    /// <summary>The wire <c>current_not_after</c> field.</summary>
    public DateTimeOffset? CurrentNotAfter { get; init; }

    /// <summary>The wire <c>last_renewal</c> field.</summary>
    public DateTimeOffset? LastRenewal { get; init; }

    /// <summary>The wire <c>last_attempt</c> field.</summary>
    public DateTimeOffset? LastAttempt { get; init; }

    /// <summary>The wire <c>last_error</c> field, when the last attempt failed.</summary>
    public string? LastError { get; init; }

    /// <summary>The wire <c>next_attempt</c> field.</summary>
    public DateTimeOffset? NextAttempt { get; init; }

    /// <summary>The wire <c>failure_count</c> field.</summary>
    public int? FailureCount { get; init; }
}

/// <summary>
/// 12 §Cert lifecycle: <c>CertLifecycle.ReadSchedulerConfig</c>/<c>WriteSchedulerConfig</c>'s
/// shared field list (<c>{mount}/scheduler/config</c>). <see cref="ClientToken"/> is write-only;
/// a read returns <see cref="ClientTokenSet"/> instead of the token itself.
/// </summary>
public sealed class SchedulerConfig
{
    /// <summary>The wire <c>enabled</c> field.</summary>
    public bool? Enabled { get; init; }

    /// <summary>The wire <c>tick_interval_seconds</c> field. The server enforces a minimum of 30; no client-side cap is added (D-M1c-25).</summary>
    public long? TickIntervalSeconds { get; init; }

    /// <summary>The wire <c>client_token</c> field. Write-only; never returned on read.</summary>
    public SecretString? ClientToken { get; init; }

    /// <summary>The wire <c>client_token_set</c> field: whether a client token is configured. Populated only on read.</summary>
    public bool? ClientTokenSet { get; init; }

    /// <summary>The wire <c>base_backoff_seconds</c> field.</summary>
    public long? BaseBackoffSeconds { get; init; }

    /// <summary>The wire <c>max_backoff_seconds</c> field.</summary>
    public long? MaxBackoffSeconds { get; init; }
}
