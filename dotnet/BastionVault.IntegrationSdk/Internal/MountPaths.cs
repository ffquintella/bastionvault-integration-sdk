namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// SYS-022's and SYS-030's path normalisation, written once so the mount surface, the auth surface
/// and <c>Kv.DetectVersion</c> cannot drift apart on what <c>"kv"</c>, <c>"kv/"</c> and
/// <c>"auth/kv/"</c> mean.
/// </summary>
/// <remarks>
/// Two forms, and the distinction is the whole of SYS-022: the <b>wire</b> form is the URL segment
/// (no trailing <c>/</c>, so <c>sys/mounts/kv</c> rather than <c>sys/mounts/kv/</c>), and the
/// <b>table</b> form is what a result is keyed by (always a trailing <c>/</c>). Input is accepted
/// in either form, everywhere.
/// </remarks>
internal static class MountPaths
{
    private const string AuthPrefix = "auth/";

    /// <summary>
    /// SYS-022: the URL-segment form. An empty or all-slash path is the client-side
    /// <c>BV-INPUT-001</c> refusal — the server answers a bare <c>404</c> with an empty body there,
    /// which the caller could not tell from "no such mount".
    /// </summary>
    public static string ToWire(string? path, string argument)
    {
        string trimmed = (path ?? string.Empty).Trim().Trim('/');
        return trimmed.Length == 0 ? throw InvalidPath(argument) : trimmed;
    }

    /// <summary>SYS-022: the result-key form, always ending in exactly one <c>/</c>.</summary>
    public static string ToTable(string path)
    {
        return ToWire(path, "path") + "/";
    }

    /// <summary>
    /// SYS-030: the same as <see cref="ToWire(string?, string)"/>, having first dropped a leading
    /// <c>auth/</c>. Auth mount paths are relative in results and both forms are accepted as input,
    /// so <c>auth/userpass/</c> and <c>userpass</c> are the same mount.
    /// </summary>
    public static string ToAuthWire(string? path, string argument)
    {
        string trimmed = (path ?? string.Empty).Trim().TrimStart('/');
        if (trimmed.StartsWith(AuthPrefix, StringComparison.Ordinal))
        {
            trimmed = trimmed[AuthPrefix.Length..];
        }

        return ToWire(trimmed, argument);
    }

    /// <summary>
    /// The result-key form of a key the <i>server</i> sent. Distinct from <see cref="ToTable"/>
    /// because a server key is never the caller's argument: an unusable one is a protocol problem,
    /// not an <c>BV-INPUT-001</c>, and it is passed through rather than refused.
    /// </summary>
    public static string NormaliseServerKey(string key, bool stripAuthPrefix)
    {
        string trimmed = key.Trim();
        if (stripAuthPrefix && trimmed.StartsWith(AuthPrefix, StringComparison.Ordinal))
        {
            trimmed = trimmed[AuthPrefix.Length..];
        }

        trimmed = trimmed.Trim('/');
        return trimmed.Length == 0 ? key : trimmed + "/";
    }

    private static BastionVaultException InvalidPath(string argument)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
        return BastionVaultException.Request(
            ErrorCodes.InputInvalidArgument,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: 0,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["argument"] = argument });
    }
}
