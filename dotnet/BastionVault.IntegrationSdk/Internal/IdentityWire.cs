namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The three things every SYS-080 route needs and nothing else: the <c>/v2</c> pin, the
/// <c>{mount}/{name}</c> tail, and the envelope-mismatch failure. Held once so the four identity
/// sub-surfaces cannot drift apart on any of them.
/// </summary>
internal static class IdentityWire
{
    /// <summary>
    /// SYS-080, TRN-071: the pin, not a default. A per-call
    /// <see cref="RequestOptions.ApiVersion"/> loses, because a <c>/v1/sys/identity/*</c> handler
    /// does not exist and routing there would turn a typed operation into an unregistered-path
    /// <c>404</c>.
    /// </summary>
    public static RequestOptions PinV2(RequestOptions? options)
    {
        return (options ?? new RequestOptions()) with { ApiVersion = "v2" };
    }

    /// <summary>
    /// The admin form's tail. Both segments go through <see cref="MountPaths.ToWire"/> — so an
    /// empty or all-slash one is the same <c>BV-INPUT-001</c> refusal a mount path gets (D-M7-7,
    /// D-M7-20: no third normaliser) — and are then encoded as single segments, so a
    /// <c>/</c> inside a name cannot invent a path level.
    /// </summary>
    public static string MountAndName(string root, string mount, string name)
    {
        string mountSegment = UrlBuilder.EncodePathSegment(MountPaths.ToWire(mount, "mount"));
        string nameSegment = UrlBuilder.EncodePathSegment(MountPaths.ToWire(name, "name"));
        return $"{root}/{mountSegment}/{nameSegment}";
    }

    /// <summary>ERR-020's <c>BV-PROTOCOL-002</c> for a response that carried no object where the specification names one.</summary>
    public static BastionVaultException EnvelopeMismatch(string path, string field)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ProtocolUnexpectedResponse);
        return BastionVaultException.Request(
            ErrorCodes.ProtocolUnexpectedResponse,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: path,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["expectedField"] = field });
    }
}
