namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Messages and hints for the <c>BV-CONFIG-*</c> codes, transcribed verbatim from
/// <c>specifications/appendix-b-error-catalogue.md</c> rows 14-21 (D-M1a-1). Centralised here so
/// every construction-time failure quotes the catalogue exactly once.
/// </summary>
internal static class ConfigCatalogue
{
    internal const string InvalidAddressMessage = "The server address is missing or not a valid URL or cluster name.";
    internal const string InvalidAddressHint = "Set `Address` (or `BASTIONVAULT_ADDR`) to `https://host:8200`, or to a bare DNS name for cluster discovery. IPv6 literals must be bracketed.";

    internal const string InsecureHttpNotAllowedMessage = "Plain `http://` to a non-loopback host is not allowed.";
    internal const string InsecureHttpNotAllowedHint = "Use `https://`, or set `AllowInsecureHttp = true` only for isolated test networks.";

    internal const string InvalidSettingValueMessage = "A configuration value has the wrong type or range.";
    internal const string InvalidSettingValueHint = "Check `Details.setting`; booleans accept 1/0/true/false/yes/no/on/off, durations accept `30s`, `1m30s` or integer seconds; timeouts must be > 0.";

    internal const string ClientCertIncompleteMessage = "Only one of `ClientCertPath` / `ClientKeyPath` is set.";
    internal const string ClientCertIncompleteHint = "Provide both the client certificate and its private key (PEM), or neither.";

    internal const string FileNotReadableMessage = "A configured file cannot be read.";
    internal const string FileNotReadableHint = "Check `Details.path` exists and the process user can read it.";

    internal const string InvalidPemMessage = "A certificate or key is not valid PEM.";
    internal const string InvalidPemHint = "Ensure the file contains `-----BEGIN CERTIFICATE-----`/`PRIVATE KEY` blocks and is not DER or PKCS#12.";

    internal const string InvalidNamespaceMessage = "The namespace path is malformed.";
    internal const string InvalidNamespaceHint = "Use `parent/child` without a leading slash, whitespace or control characters.";

    internal const string ReservedHeaderMessage = "A custom header would override a header the SDK manages.";
    internal const string ReservedHeaderHint = "Remove `X-BastionVault-Token`, `X-Vault-Token`, `Authorization`, `Cookie`, `X-BastionVault-Namespace`, `Host`, `Content-Length` from `Headers`; use `Token`/`Namespace` instead.";
}
