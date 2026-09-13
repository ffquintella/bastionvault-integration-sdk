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
