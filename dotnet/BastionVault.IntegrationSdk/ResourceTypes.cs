namespace BastionVault.IntegrationSdk;

/// <summary>12 §Resources: <c>Resources.Search</c>'s request, sent as a JSON body, never a query string.</summary>
public sealed class ResourceSearchQuery
{
    /// <summary>The wire <c>q</c> field; omitted from the body when unset.</summary>
    public string? Q { get; init; }

    /// <summary>The wire <c>type</c> field; omitted from the body when unset.</summary>
    public string? Type { get; init; }

    /// <summary>The wire <c>offset</c> field; omitted from the body when unset.</summary>
    public int? Offset { get; init; }

    /// <summary>The wire <c>limit</c> field; omitted from the body when unset.</summary>
    public int? Limit { get; init; }
}

/// <summary>
/// RSC-002: <c>Resources.Secrets.Read</c>/<c>ReadVersion</c>'s result. No field set is named
/// beyond "the data", so this is <see cref="Response.Data"/> (TRN-040's Shape A/B resolution),
/// never <see cref="Response.Raw"/> — the envelope (<c>request_id</c>, <c>lease_id</c>, …) is not
/// secret material and does not belong redacted; only each field's own value does. The opposite
/// of <see cref="ResourcesOperations.ReadAsync"/>'s plain, unredacted record.
/// </summary>
public sealed class ResourceSecret
{
    /// <summary>The server's own field names; each value held redacting, never the server's own field.</summary>
    public required IReadOnlyDictionary<string, SecretString> Data { get; init; }
}

/// <summary>12 §Resources: <c>Resources.Connect.MfaBegin</c>'s request, <c>{resource, profile_id}</c>.</summary>
public sealed class ConnectMfaBeginRequest
{
    /// <summary>The wire <c>resource</c> field. RSC-001: empty raises <c>BV-INPUT-001</c> client-side.</summary>
    public required string Resource { get; init; }

    /// <summary>The wire <c>profile_id</c> field.</summary>
    public required string ProfileId { get; init; }
}

/// <summary>
/// 12 §Resources: <c>Resources.Connect.MfaVerify</c>'s request. <c>method</c> is a plain string
/// (<c>totp</c> | <c>fido2</c>), following <c>Identity.Groups</c>' <c>kind</c> convention rather
/// than an enum for a domain this small.
/// </summary>
public sealed class ConnectMfaVerifyRequest
{
    /// <summary>The wire <c>resource</c> field. RSC-001: empty raises <c>BV-INPUT-001</c> client-side.</summary>
    public required string Resource { get; init; }

    /// <summary>The wire <c>profile_id</c> field.</summary>
    public required string ProfileId { get; init; }

    /// <summary>The wire <c>method</c> field: <c>totp</c> or <c>fido2</c>.</summary>
    public required string Method { get; init; }

    /// <summary>The wire <c>totp_code</c> field; omitted from the body when unset.</summary>
    public string? TotpCode { get; init; }

    /// <summary>The wire <c>credential</c> field (a FIDO2 assertion); omitted from the body when unset.</summary>
    public string? Credential { get; init; }
}

/// <summary>
/// 12 §Resources: <c>Resources.Connect.MfaVerify</c>'s result. RSC-001: the ticket is single-use
/// and redacting, held as a <see cref="SecretString"/> from the moment it is parsed off the wire.
/// </summary>
public sealed class ConnectMfaVerifyResult
{
    /// <summary>The wire <c>connect_ticket</c> field, consumed by <see cref="ConnectAuthorizeRequest.ConnectTicket"/>.</summary>
    public required SecretString ConnectTicket { get; init; }
}

/// <summary>
/// 12 §Resources: <c>Resources.Connect.Authorize</c>'s request. R-33 guard:
/// <see cref="ConnectTicket"/> travels in the POST body only (D-M9-20's rule).
/// </summary>
public sealed class ConnectAuthorizeRequest
{
    /// <summary>The wire <c>resource</c> field. RSC-001: empty raises <c>BV-INPUT-001</c> client-side.</summary>
    public required string Resource { get; init; }

    /// <summary>The wire <c>profile_id</c> field.</summary>
    public required string ProfileId { get; init; }

    /// <summary>The wire <c>connect_ticket</c> field; omitted from the body when unset.</summary>
    public SecretString? ConnectTicket { get; init; }
}
