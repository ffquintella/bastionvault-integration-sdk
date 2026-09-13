namespace BastionVault.IntegrationSdk;

/// <summary>
/// Per-operation options (CFG-060, CFG-061). Every operation accepts one of these, defaulted when
/// omitted. Being an immutable record, an instance can never mutate the
/// <see cref="BastionVaultClient"/> it is passed alongside (CFG-061) — there is no reference from a
/// <see cref="RequestOptions"/> back to a client at all.
/// </summary>
public sealed record RequestOptions
{
    /// <summary>Override the client namespace for this call only.</summary>
    public string? Namespace { get; init; }

    /// <summary>Additional headers for this call (same reserved-header rule as <c>Headers</c>, CFG-017).</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Override the client's <c>Timeout</c> (per attempt) for this call.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Tri-state (D-M1b-6): unset defers to the idempotency table; when set, wins in both
    /// directions — a caller may mark a write retryable and may mark a read not-retryable.
    /// </summary>
    public bool? Idempotent { get; init; }

    /// <summary>Request response wrapping (<c>X-Vault-Wrap-TTL</c>). Unimplemented by the server; raises <c>BV-INPUT-006</c> (TRN-017).</summary>
    public string? WrapTtl { get; init; }

    /// <summary>Use a different token for this call only (e.g. a machine token).</summary>
    public SecretString? Token { get; init; }

    /// <summary>Override <c>ApiPrefix</c> (<c>v1</c>/<c>v2</c>) for this call only (TRN-002, D-M1b-13).</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Bounds attempts <em>and</em> backoff together, where <see cref="Timeout"/> bounds each attempt (RES-004, D-M1b-13).</summary>
    public TimeSpan? TotalTimeout { get; init; }

    /// <summary>Runtime cancellation primitive for this call.</summary>
    public CancellationToken CancellationToken { get; init; }
}
