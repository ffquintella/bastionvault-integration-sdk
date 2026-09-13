namespace BastionVault.IntegrationSdk;

/// <summary>
/// Stable error code constants (ERR-005). The literal string form is always reachable directly
/// (e.g. <c>"BV-CONFIG-001"</c>) so logs from different language SDKs correlate without referencing
/// this type. At M1b the <c>BV-CONFIG-*</c> codes (M1a) and the D-M1b-4 populated set are backed by
/// constructible errors; the remaining constants exist so <see cref="RetryPolicy.RetryOn"/> and
/// <see cref="ErrorCategory"/> consumers have a stable name to reference ahead of M1c.
/// </summary>
public static class ErrorCodes
{
    /// <summary>The server address is missing or not a valid URL or cluster name.</summary>
    public const string ConfigInvalidAddress = "BV-CONFIG-001";

    /// <summary>Plain <c>http://</c> to a non-loopback host is not allowed.</summary>
    public const string ConfigInsecureHttpNotAllowed = "BV-CONFIG-002";

    /// <summary>A configuration value has the wrong type or range.</summary>
    public const string ConfigInvalidSettingValue = "BV-CONFIG-003";

    /// <summary>Only one of <c>ClientCertPath</c> / <c>ClientKeyPath</c> is set.</summary>
    public const string ConfigClientCertIncomplete = "BV-CONFIG-004";

    /// <summary>A configured file cannot be read.</summary>
    public const string ConfigFileNotReadable = "BV-CONFIG-005";

    /// <summary>A certificate or key is not valid PEM.</summary>
    public const string ConfigInvalidPem = "BV-CONFIG-006";

    /// <summary>The namespace path is malformed.</summary>
    public const string ConfigInvalidNamespace = "BV-CONFIG-007";

    /// <summary>A custom header would override a header the SDK manages.</summary>
    public const string ConfigReservedHeader = "BV-CONFIG-008";

    /// <summary>The HTTP stack cannot send the custom <c>LIST</c> method (D-M1b-14).</summary>
    public const string ConfigListVerbUnsupported = "BV-CONFIG-009";

    /// <summary>An argument is missing or invalid; also the D-M1b-21 status-class fallback for an unmapped 4xx.</summary>
    public const string InputInvalidArgument = "BV-INPUT-001";

    /// <summary>The option is not supported by BastionVault (e.g. <c>WrapTtl</c>, TRN-017).</summary>
    public const string InputUnsupportedOption = "BV-INPUT-006";

    /// <summary>The request body exceeds the server's 32 MiB limit (TRN-032).</summary>
    public const string InputBodyTooLarge = "BV-INPUT-007";

    /// <summary>The recording chunk index is past the end (416).</summary>
    public const string InputChunkIndexOutOfRange = "BV-INPUT-008";

    /// <summary>Could not connect to the server.</summary>
    public const string TransportConnectionFailed = "BV-TRANSPORT-001";

    /// <summary>The request timed out.</summary>
    public const string TransportTimeout = "BV-TRANSPORT-002";

    /// <summary>TLS handshake or certificate verification failed.</summary>
    public const string TransportTlsError = "BV-TRANSPORT-003";

    /// <summary>The response exceeded <c>MaxResponseBytes</c>.</summary>
    public const string TransportResponseTooLarge = "BV-TRANSPORT-004";

    /// <summary>The operation was cancelled.</summary>
    public const string TransportCancelled = "BV-TRANSPORT-005";

    /// <summary>The server does not accept this HTTP method on this path.</summary>
    public const string ProtocolMethodNotAllowed = "BV-PROTOCOL-001";

    /// <summary>The server response could not be interpreted.</summary>
    public const string ProtocolUnexpectedResponse = "BV-PROTOCOL-002";

    /// <summary>The server answered with a redirect.</summary>
    public const string ProtocolUnexpectedRedirect = "BV-PROTOCOL-003";

    /// <summary>No token is configured for an authenticated request.</summary>
    public const string AuthNoToken = "BV-AUTH-001";

    /// <summary>The server requires authentication for this call.</summary>
    public const string AuthUnauthenticated = "BV-AUTH-002";

    /// <summary>The token does not have permission for this path.</summary>
    public const string AuthzPermissionDenied = "BV-AUTHZ-001";

    /// <summary>Nothing exists at this path.</summary>
    public const string NotFoundPathNotFound = "BV-NOTFOUND-001";

    /// <summary>The recording bytes do not match the recorded digest (409).</summary>
    public const string ConflictRecordingDigestMismatch = "BV-CONFLICT-002";

    /// <summary>A static SSH credential cannot be attached to a brokered resource (409).</summary>
    public const string ConflictBrokeredResourceStaticCredential = "BV-CONFLICT-003";

    /// <summary>The server's abuse guard temporarily blocked this client IP.</summary>
    public const string RateLimitedByDosGuard = "BV-RATE-001";

    /// <summary>The namespace request-rate quota was exceeded.</summary>
    public const string RateNamespaceQuotaExceeded = "BV-RATE-002";

    /// <summary>A namespace capacity quota was reached.</summary>
    public const string QuotaNamespaceQuotaExceeded = "BV-QUOTA-001";

    /// <summary>The vault is sealed.</summary>
    public const string ServerSealed = "BV-SERVER-001";

    /// <summary>The server is temporarily unavailable.</summary>
    public const string ServerUnavailable = "BV-SERVER-002";

    /// <summary>The node is a standby and cannot serve this request.</summary>
    public const string ServerStandby = "BV-SERVER-003";

    /// <summary>The server reported an internal error.</summary>
    public const string ServerInternalError = "BV-SERVER-005";

    /// <summary>The pinned node became unavailable.</summary>
    public const string DiscoveryNodeUnavailable = "BV-DISCOVERY-003";
}
