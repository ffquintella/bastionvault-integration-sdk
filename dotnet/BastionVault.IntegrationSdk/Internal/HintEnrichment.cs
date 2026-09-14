using System.Globalization;
using System.Text;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// ERR-034's path interpolation and ERR-040's context-aware hint notes, in one deterministic
/// function applied once, where the error leaves <see cref="RequestExecutor"/> (D-M1c-5).
/// </summary>
/// <remarks>
/// Only the seven rows of
/// <c>specifications/04-error-model.md#hint-enrichment-from-context</c> that are decidable from
/// client-side state alone are here. The two rows that need <c>Sys.CapabilitiesSelf</c> (M3) and
/// the <c>Sys.ListMounts</c> cache (M4) are <b>absent, not stubbed</b>: a stub would be a branch no
/// test can reach and the CNF-010 coverage floor allows no exclusion pragma to excuse it. The
/// owning milestone adds the branch and its fixture (D-M1c-5).
/// </remarks>
internal static class HintEnrichment
{
    /// <summary>The client-side facts the seven landed rows need; nothing about retry state.</summary>
    internal readonly record struct Context(
        int? StatusCode,
        TimeSpan? RetryAfter,
        string Path,
        string ActiveNamespace,
        bool HasCaCertificate,
        string Address);

    /// <summary>The address <c>ConfigurationResolver</c> falls back to when none is configured.</summary>
    internal const string DefaultAddress = "https://127.0.0.1:8200";

    private const string NoNamespaceNote =
        "No namespace is set; if the credential is scoped to a namespace, set `Namespace`.";

    private const string ApiVersionNote =
        "Pin this call to `/v2` (RequestOptions.ApiVersion = 2).";

    private const string SealedNote =
        "Run `bvault operator unseal` on the node or wait for auto-unseal; the SDK will not retry.";

    private const string UnsupportedPathNote =
        "This server version does not have this endpoint; check `Client.ServerVersion()` and use the documented fallback.";

    private const string TlsNoCaNote =
        "Provide the server's CA bundle via `CaCertPath` or `BASTIONVAULT_CACERT`.";

    private const string ConnectionRefusedNote =
        "No `Address` was configured; the default is `https://127.0.0.1:8200`. Set `Address` or `BASTIONVAULT_ADDR`.";

    /// <summary>
    /// ERR-034: a hint that points at <c>Details.path</c> must name the path the SDK actually sent,
    /// because most "not found" problems are mount or prefix mistakes. Runs before the ERR-040
    /// table so the enrichment rows below stay exactly the seven D-M1c-5 lists.
    /// </summary>
    public static string InterpolatePath(string hint, string? redactedPath)
    {
        if (string.IsNullOrEmpty(redactedPath) || !hint.Contains("Details.path", StringComparison.Ordinal))
        {
            return hint;
        }

        return Append(hint, $"The path as sent was `{redactedPath}`.");
    }

    /// <summary>
    /// The same ERR-034 principle for a hint that points at <c>Details.keys</c> instead of
    /// <c>Details.path</c>: <c>BV-INPUT-009</c>'s catalogue hint names the reserved <i>families</i>
    /// (<c>username</c>, <c>spiffe_id</c>, <c>approle_env_*</c>) but not the keys the caller
    /// actually sent, and AUT-081 is refused client-side so there is no server message to fall
    /// back on. Nothing is rewritten; the concrete keys are appended (D-M1c-5).
    /// </summary>
    public static string InterpolateKeys(string hint, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0 || !hint.Contains("Details.keys", StringComparison.Ordinal))
        {
            return hint;
        }

        return Append(hint, $"The reserved key(s) sent were `{string.Join("`, `", keys)}`.");
    }

    /// <summary>
    /// Appends the ERR-040 notes whose condition holds, in the order
    /// <c>04-error-model.md</c> lists them, separated by a single space. Never rewrites the
    /// catalogue hint (D-M1c-5).
    /// </summary>
    public static string Enrich(string code, string hint, in Context context)
    {
        string result = hint;

        if (context.StatusCode == 403
            && context.ActiveNamespace.Length == 0
            && IsUnderNamespaceScopedMount(context.Path))
        {
            result = Append(result, NoNamespaceNote);
        }

        if (context.StatusCode == 400 && code == ErrorCodes.ServerApiVersionMismatch)
        {
            result = Append(result, ApiVersionNote);
        }

        if (context.StatusCode == 429 && context.RetryAfter is { } retryAfter)
        {
            int seconds = (int)Math.Round(retryAfter.TotalSeconds, MidpointRounding.AwayFromZero);
            result = Append(
                result,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The client rate gate is now paused for `{seconds}`s; reduce request fan-out (use `Sys.Batch` or `*-info` pages)."));
        }

        if (context.StatusCode == 503 && code == ErrorCodes.ServerSealed)
        {
            result = Append(result, SealedNote);
        }

        if (context.StatusCode == 500 && code == ErrorCodes.ServerUnsupportedByServer)
        {
            result = Append(result, UnsupportedPathNote);
        }

        // The SDK maps a handshake failure and a verification failure to the same
        // BV-TRANSPORT-003 (D-M1b-4a), so the trigger is the code plus "no CA configured"
        // rather than a distinction the error does not carry.
        if (code == ErrorCodes.TransportTlsError && !context.HasCaCertificate)
        {
            result = Append(result, TlsNoCaNote);
        }

        // "Connection refused to default address": the resolved address is the observable
        // client-side fact; ClientConfig does not record whether it was defaulted, and adding a
        // flag to it would be a public API change this row does not justify.
        if (code == ErrorCodes.TransportConnectionFailed
            && string.Equals(context.Address, DefaultAddress, StringComparison.Ordinal))
        {
            result = Append(result, ConnectionRefusedNote);
        }

        return result;
    }

    private static bool IsUnderNamespaceScopedMount(string path)
    {
        // The display path may carry the `[ns=…] ` prefix (ERR-001's `Path`), but this row only
        // fires when the namespace is empty, in which case there is no prefix. Matches the note
        // in rust/.../enrichment.rs.
        string trimmed = path.TrimStart('/');
        return trimmed.StartsWith("auth/", StringComparison.Ordinal)
            || trimmed.StartsWith("secret/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Appends one note after a single space. No guard against a repeat: each ERR-040 row is
    /// tested once per error and the rows are disjoint, so a duplicate is unreachable and would
    /// only be dead code (D-M1c-5).
    /// </summary>
    private static string Append(string hint, string note)
    {
        StringBuilder builder = new(hint.Length + note.Length + 1);
        builder.Append(hint.TrimEnd()).Append(' ').Append(note);
        return builder.ToString();
    }
}
