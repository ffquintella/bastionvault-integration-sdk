namespace BastionVault.IntegrationSdk;

/// <summary>
/// Stable error code constants (ERR-005). Only the <c>BV-CONFIG-*</c> codes are populated at
/// milestone M1a (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-1); the literal string form is
/// always reachable directly (e.g. <c>"BV-CONFIG-001"</c>) so logs from different language SDKs
/// correlate without referencing this type.
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
}
