using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>Rustion.Session.Open</c>'s request (12 §Rustion). Only <c>credential_material</c> is named
/// and typed (v1, <see cref="SecretString"/>); every other field is an opaque bag merged into the
/// body verbatim, the same idiom <see cref="SyncTarget"/> established at M10 slice b.
/// </summary>
public sealed class RustionSessionRequest
{
    /// <summary>The raw v1 credential material. Never logged, never redacted away from the wire.</summary>
    public required SecretString CredentialMaterial { get; init; }

    /// <summary>Every other field, merged into the request body verbatim. Must not carry a <c>credential_material</c> key.</summary>
    public JsonElement? Fields { get; init; }
}

/// <summary>
/// <c>Rustion.Session.OpenConnectOnly</c>'s request (12 §Rustion), fully documented and
/// <c>/v2</c>-pinned. <c>connect_ticket</c> is the same single-use secret shape as
/// <c>Resources.Connect</c>'s ticket (M10 slice b).
/// </summary>
public sealed class RustionSessionOpenConnectOnlyRequest
{
    /// <summary>The resource being connected to.</summary>
    public required string ResourceName { get; init; }

    /// <summary>The <c>credential_source.secret_id</c> field. <c>kind</c> is always <c>secret</c> and is not settable.</summary>
    public required string SecretId { get; init; }

    /// <summary>The bastion-facing target host.</summary>
    public required string TargetHost { get; init; }

    /// <summary>The bastion-facing target port.</summary>
    public required int TargetPort { get; init; }

    /// <summary>The bastion-facing target protocol.</summary>
    public required string TargetProtocol { get; init; }

    /// <summary>Optional connect profile.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Single-use, redacting. Never travels in a query string (R-33).</summary>
    public SecretString? ConnectTicket { get; init; }
}

/// <summary><c>Rustion.Session.Renew</c>'s request (12 §Rustion), fully documented.</summary>
public sealed class RustionSessionRenewRequest
{
    /// <summary>The bastion identifier.</summary>
    public required string BastionId { get; init; }

    /// <summary>The session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>The caller-assigned correlation identifier.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Default 1800 seconds.</summary>
    public TimeSpan ExtendSecs { get; init; } = TimeSpan.FromSeconds(1800);
}

/// <summary>
/// <c>Rustion.Session.Kill</c>'s request. 12 §Rustion names no fields; this is <see cref="RustionSessionRenewRequest"/>'s
/// three identifying fields minus <c>extend_secs</c> (see <c>RustionWire.SerialiseKill</c>'s remarks).
/// </summary>
public sealed class RustionSessionKillRequest
{
    /// <summary>The bastion identifier.</summary>
    public required string BastionId { get; init; }

    /// <summary>The session identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>The caller-assigned correlation identifier.</summary>
    public required string CorrelationId { get; init; }
}

/// <summary>One chunk of <c>Rustion.Recordings.Chunk</c>/<c>Download</c> (RUS-001).</summary>
public sealed class RustionRecordingChunk
{
    /// <summary>The decoded <c>bytes_b64</c> payload.</summary>
    public required ReadOnlyMemory<byte> Bytes { get; init; }

    /// <summary>Whether this is the last chunk of the recording.</summary>
    public required bool Eof { get; init; }

    /// <summary>The server's own digest-verification claim, when reported.</summary>
    public bool? DigestVerified { get; init; }

    /// <summary>The recording's SHA-256, when reported.</summary>
    public string? Sha256 { get; init; }
}
