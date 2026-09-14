using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The canonical result of a successful <see cref="LogicalOperations"/> call (TRN-040..043).
/// A <c>304</c> is represented as <see cref="StatusCode"/> <c>== 304</c> with <see cref="Data"/>
/// absent (D-M1b-10); no separate field is added for it.
/// </summary>
public sealed class Response
{
    /// <summary>
    /// Shape A: the wire envelope's <c>data</c> object. Shape B: the whole parsed body. Absent when
    /// neither is present (e.g. a login response with an empty <c>data</c> object still yields an
    /// empty, non-null map).
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Data { get; init; }

    /// <summary>Present on login / token-create responses (TRN-040).</summary>
    public AuthInfo? Auth { get; init; }

    /// <summary>Absent when the wire value is <c>""</c> (TRN-041).</summary>
    public string? LeaseId { get; init; }

    /// <summary>Absent when not present on the wire (TRN-042).</summary>
    public bool? Renewable { get; init; }

    /// <summary>Seconds on the wire; absent when not present (TRN-042).</summary>
    public TimeSpan? LeaseDuration { get; init; }

    /// <summary>Always empty against current servers (ERR-050); never fabricated.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>The HTTP status of the attempt that produced this response.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Response headers (<c>ETag</c>, <c>Retry-After</c>, ...).</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>The exact parsed body, for diagnostics and forward-compatible field access (TRN-043).</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>The <c>auth</c> object on a login / token-create <see cref="Response"/> (03 §Auth object).</summary>
/// <remarks>
/// AUT-013's five recorded facts are <see cref="ClientToken"/>, <see cref="IssuedAt"/>,
/// <see cref="LeaseDuration"/>, <see cref="Renewable"/>, <see cref="Policies"/> and
/// <see cref="Metadata"/>. AUT-014's optional <c>Accessor</c>, <c>EntityId</c>, <c>TokenType</c>,
/// <c>Orphan</c> and <c>NumUses</c> are deliberately <b>absent</b> rather than present-and-always-null:
/// the wire <c>auth</c> object has only five fields, those five are "populated only after
/// <c>LookupSelf</c>", and no M2b requirement merges a <c>LookupSelf</c> result back into an
/// <see cref="AuthInfo"/>. A member with nothing that can ever fill it is the stub D-M1c-25
/// forbids; <see cref="TokenInfo"/> is where a lookup's fields live today. Recorded as an open
/// question against D-M2-6's pin rather than shipped as five null properties.
/// </remarks>
public sealed class AuthInfo
{
    /// <summary>The issued token. Held as a <see cref="SecretString"/> so it is never logged by accident.</summary>
    public required SecretString ClientToken { get; init; }

    /// <summary>Policies attached to the token.</summary>
    public IReadOnlyList<string> Policies { get; init; } = Array.Empty<string>();

    /// <summary>Free-form metadata the server attached to the token.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>Seconds on the wire.</summary>
    public TimeSpan? LeaseDuration { get; init; }

    /// <summary>Whether the token can be renewed.</summary>
    public bool Renewable { get; init; }

    /// <summary>
    /// AUT-013: when the SDK received this credential, read from the injected clock (D-M1b-7) and
    /// never from the local wall clock, so AUT-090's renewal schedule and AUT-003's
    /// <c>MinReloginInterval</c> are both drivable deterministically in tests.
    /// </summary>
    /// <remarks>
    /// The wire carries no issue time — only <c>lease_duration</c> — so this is the SDK's own
    /// observation of "now", which is exactly what AUT-090's
    /// <c>IssuedAt + LeaseDuration × RenewAtFraction</c> needs.
    /// </remarks>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// AUT-044: the environment scope derived from <see cref="Metadata"/>'s <c>approle_env_*</c>
    /// keys. Computed rather than stored, so it can never disagree with the metadata it comes from.
    /// </summary>
    public EnvironmentScope EnvironmentScope => EnvironmentScope.From(Metadata);
}

/// <summary>The result of <see cref="LogicalOperations.RawAsync"/>: no envelope is parsed (D-M1b-12).</summary>
public sealed class RawResponse
{
    /// <summary>The HTTP status of the attempt.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Response headers.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>The unparsed response body.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }
}
