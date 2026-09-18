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
/// the <c>Sys.ListMounts</c> cache were <b>absent, not stubbed</b> through M1c-M6: a stub would
/// have been a branch no test can reach and the CNF-010 coverage floor allows no exclusion pragma
/// to excuse it. The owning milestone adds the branch and its fixture (D-M1c-5).
/// <para>
/// M7c lands the second of those two. Its condition needs the SYS-026 mount-type cache <i>and</i>
/// the caller's mount/name split, neither of which this function has, so the row lives at
/// <c>Kv.V1.Read</c> and reaches the shared note text and the shared separator through
/// <see cref="KvV2MountNote"/> and <see cref="AppendNote"/> (DR-0012 D-M7-32). The
/// <c>capabilities-self</c> row is still absent.
/// </para>
/// </remarks>
internal static class HintEnrichment
{
    /// <summary>The client-side facts the seven landed rows need; nothing about retry state.</summary>
    /// <param name="StatusCode">The HTTP status of the response, when a request was made.</param>
    /// <param name="RetryAfter">The parsed <c>Retry-After</c>, when present.</param>
    /// <param name="Path">The redacted display path.</param>
    /// <param name="ActiveNamespace">The namespace the client is configured with, possibly empty.</param>
    /// <param name="HasCaCertificate">Whether a CA bundle was configured.</param>
    /// <param name="Address">The resolved server address.</param>
    /// <param name="Method">
    /// The HTTP verb the SDK sent (<c>"GET"</c>, <c>"POST"</c>, …). KV2-023's condition needs it
    /// to tell a v2 <b>read</b> apart from a v2 <b>write</b> to the same <c>data/</c> route.
    /// </param>
    internal readonly record struct Context(
        int? StatusCode,
        TimeSpan? RetryAfter,
        string Path,
        string ActiveNamespace,
        bool HasCaCertificate,
        string Address,
        string Method = "GET");

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

    /// <summary>KV2-023's note, in the requirement's own words.</summary>
    private const string EnvRequiredByPolicyNote =
        "The policy may require `env`: a policy can set `required_parameters = [\"env\"]`, and this KV v2 read carried none.";

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

        // KV2-023, not one of ERR-040's seven rows — hence last, after the table, so the table's
        // order stays exactly as 04-error-model.md lists it. A policy may set
        // `required_parameters = ["env"]`, which turns a v2 data read with no `env` into a 403
        // that looks identical to a missing capability. KV2-023 says "a 403 on a v2 read", so the
        // condition is narrowed to a GET (KV2-023 narrowing): before this, a 403 on a v2 write to
        // the same route collected the same note.
        if (context.StatusCode == 403
            && string.Equals(context.Method, "GET", StringComparison.Ordinal)
            && IsKvV2DataReadWithoutEnv(context.Path))
        {
            result = Append(result, EnvRequiredByPolicyNote);
        }

        return result;
    }

    /// <summary>
    /// KV2-023's route half of the condition: a <c>{mount}/data/{path}</c> route whose query
    /// carries no <c>env</c>. Combined in <see cref="Enrich"/> with the method check (KV2-023
    /// narrowing) so that a <b>write</b> to the same route no longer collects this note — before
    /// that check existed, a <c>403</c> on <see cref="KvV2Operations.WriteSecretAsync"/> matched
    /// this route pattern too. The display path is the raw logical path including its query
    /// (<c>RequestExecutor.BuildDisplayPath</c>), which is what makes this a client-side fact
    /// rather than one needing a second call.
    /// </summary>
    /// <remarks>
    /// One ambiguity survives narrowing and is not fixable from this layer: a KV <b>v1</b> read of
    /// a secret whose <paramref name="path"/>-shaped literal name is <c>data/foo</c> under mount
    /// <c>secret</c> builds the byte-identical route <c>secret/data/foo</c> that a v2 read of
    /// <c>foo</c> builds, and the enrichment layer never learns which sub-client sent it. Guessing
    /// "v1" for that shape would be exactly the plausible guess D-M1c-25 forbids in the other
    /// direction, so the note still fires for that one pathological case.
    /// </remarks>
    private static bool IsKvV2DataReadWithoutEnv(string path)
    {
        int queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        string route = queryIndex < 0 ? path : path[..queryIndex];
        string query = queryIndex < 0 ? string.Empty : path[(queryIndex + 1)..];

        string[] segments = route.Split('/');
        int dataIndex = Array.IndexOf(segments, "data");
        // `data` must be a middle segment: a mount before it and at least one path segment after,
        // so `{mount}/data/{path}` matches and a mount literally named `data` does not.
        if (dataIndex <= 0 || dataIndex >= segments.Length - 1 || segments[dataIndex + 1].Length == 0)
        {
            return false;
        }

        foreach (string pair in query.Split('&'))
        {
            if (pair.StartsWith("env=", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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
    /// Appends one note after a single space. No guard against a repeat: each row is tested once
    /// per error and no two rows produce the <i>same</i> note, so a duplicate is unreachable and
    /// would only be dead code (D-M1c-5). Two rows may now both fire on one error — a <c>403</c> on
    /// a KV v2 data read with no namespace set matches both the ERR-040 namespace row and KV2-023's
    /// — which is why the rows are applied in a fixed order rather than as an if/else chain.
    /// </summary>
    /// <summary>
    /// ERR-040's KV-v2 row, in the table's own words, with <c>&lt;mount&gt;</c> and
    /// <c>&lt;name&gt;</c> substituted.
    /// </summary>
    /// <remarks>
    /// ⚠️ The note names <c>Kv.ReadSecret</c>, the version-agnostic façade D-M4-9 declined and
    /// D-M7-8 kept declined. The text is the specification's, character for character, and
    /// ERR-040 requires the enrichment to be deterministic and fixture-covered — so it is emitted
    /// as written rather than rephrased to match the surface this SDK actually ships. DR-0012
    /// D-M7-32 records the mismatch as an open question rather than papering over it.
    /// </remarks>
    public static string KvV2MountNote(string mount, string name)
    {
        return $"This mount is KV v2; use `{mount}/data/{name}` or `Kv.ReadSecret`.";
    }

    /// <summary>
    /// Appends a note to an existing hint with the single-space separator every ERR-040 row uses.
    /// Public so a row whose condition is only decidable at the <i>operation</i> — ERR-040's
    /// KV-v2 row, which needs the SYS-026 mount-type cache — appends it the same way
    /// <see cref="Enrich"/> appends the other seven, rather than inventing a second separator.
    /// </summary>
    public static string AppendNote(string hint, string note)
    {
        return Append(hint, note);
    }

    private static string Append(string hint, string note)
    {
        StringBuilder builder = new(hint.Length + note.Length + 1);
        _ = builder.Append(hint.TrimEnd()).Append(' ').Append(note);
        return builder.ToString();
    }
}
