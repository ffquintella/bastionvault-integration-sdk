namespace BastionVault.IntegrationSdk;

/// <summary>
/// Per-operation options (CFG-060, CFG-061). Every operation MUST accept one of these, defaulted
/// when omitted. No operations exist yet at milestone M1a; this type lands so the shape is fixed
/// before any operation is built on top of it. Being an immutable record, an instance can never
/// mutate the <see cref="BastionVaultClient"/> it is passed alongside (CFG-061) — there is no
/// reference from a <see cref="RequestOptions"/> back to a client at all.
/// </summary>
public sealed record RequestOptions
{
    /// <summary>Override the client namespace for this call only.</summary>
    public string? Namespace { get; init; }

    /// <summary>Additional headers for this call (same reserved-header rule as <c>Headers</c>, CFG-017).</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Override the client's <c>Timeout</c> for this call.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Mark a write as safe to retry.</summary>
    public bool Idempotent { get; init; }

    /// <summary>Request response wrapping (<c>X-Vault-Wrap-TTL</c>) where the server supports it.</summary>
    public string? WrapTtl { get; init; }

    /// <summary>Use a different token for this call only (e.g. a machine token).</summary>
    public SecretString? Token { get; init; }

    /// <summary>Runtime cancellation primitive for this call.</summary>
    public CancellationToken CancellationToken { get; init; }
}
