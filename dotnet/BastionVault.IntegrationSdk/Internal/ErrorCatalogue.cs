namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Messages, hints and categories transcribed from <c>specifications/appendix-b-error-catalogue.md</c>
/// for the D-M1b-4 populated set (plus the M1a <c>BV-CONFIG-*</c> rows already in
/// <see cref="ConfigCatalogue"/>). M1c extends this table with the remaining Appendix B rows and
/// message recognition; it does not reshape <see cref="StatusCodeMapper"/>.
/// </summary>
internal static class ErrorCatalogue
{
    internal sealed record Entry(ErrorCategory Category, string Message, string Hint);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ConfigListVerbUnsupported] = new(
            ErrorCategory.Configuration,
            "The HTTP stack cannot send the custom `LIST` method.",
            "Use the SDK's default transport or an HTTP client that allows non-standard methods; the server does not support `?list=true`."),

        [ErrorCodes.InputInvalidArgument] = new(
            ErrorCategory.Input,
            "An argument is missing or invalid.",
            "See `Details.argument` and `Details.reason`; required strings must be non-empty, `env` cannot contain `/`, `env` and `envs` are mutually exclusive."),

        [ErrorCodes.InputUnsupportedOption] = new(
            ErrorCategory.Input,
            "The option is not supported by BastionVault.",
            "Response wrapping (`WrapTtl`) is not implemented by the server; remove the option."),

        [ErrorCodes.InputBodyTooLarge] = new(
            ErrorCategory.Input,
            "The request body exceeds the server limit.",
            "Keep bodies under 32 MiB; for files, upload smaller versions or use sync targets."),

        [ErrorCodes.InputChunkIndexOutOfRange] = new(
            ErrorCategory.Input,
            "The recording chunk index is past the end.",
            "Read chunk 0 first and stop at `eof`; `Details.chunk_count` is the real count."),

        [ErrorCodes.TransportConnectionFailed] = new(
            ErrorCategory.Transport,
            "Could not connect to the server.",
            "Check `Address`, DNS, firewall and that the server is listening (default `https://127.0.0.1:8200`)."),

        [ErrorCodes.TransportTimeout] = new(
            ErrorCategory.Transport,
            "The request timed out.",
            "Increase `Timeout`/`ConnectTimeout`, check server load; long-poll calls need ≥ 40 s."),

        [ErrorCodes.TransportTlsError] = new(
            ErrorCategory.Transport,
            "TLS handshake or certificate verification failed.",
            "Provide the server CA via `CaCertPath`; check `TlsServerName` matches a SAN; verify the clock. Only as a diagnostic step, and never in production, `TlsSkipVerify` confirms whether trust is the cause."),

        [ErrorCodes.TransportResponseTooLarge] = new(
            ErrorCategory.Transport,
            "The response exceeded `MaxResponseBytes`.",
            "Use the chunked route (`Rustion.Recordings.Download`) or paging (`*-info`), or raise `MaxResponseBytes`."),

        [ErrorCodes.TransportCancelled] = new(
            ErrorCategory.Transport,
            "The operation was cancelled.",
            "The caller cancelled; no request state is known. Retry is the caller's decision."),

        [ErrorCodes.ProtocolMethodNotAllowed] = new(
            ErrorCategory.Protocol,
            "The server does not accept this HTTP method on this path.",
            "Only GET, POST/PUT, DELETE and LIST are routed; use the matching logical operation."),

        [ErrorCodes.ProtocolUnexpectedResponse] = new(
            ErrorCategory.Protocol,
            "The server response could not be interpreted.",
            "The body was not JSON or not a known shape (`Details.snippet`); confirm `Address` points at a BastionVault API listener, not a proxy or GUI."),

        [ErrorCodes.ProtocolUnexpectedRedirect] = new(
            ErrorCategory.Protocol,
            "The server answered with a redirect.",
            "BastionVault never redirects; a proxy or load balancer in front of it does. Point `Address` at the vault or fix the proxy."),

        [ErrorCodes.AuthUnauthenticated] = new(
            ErrorCategory.Authentication,
            "The server requires authentication for this call.",
            "Provide a valid token; for connect-MFA calls the caller must be a userpass principal."),

        [ErrorCodes.AuthzPermissionDenied] = new(
            ErrorCategory.Authorization,
            "The token does not have permission for this path (or the token is invalid, expired or revoked).",
            "Check the token's policies grant the capability on `Details.path` (`Sys.CapabilitiesSelf`); verify the token with `Auth.Token.LookupSelf`; if the credential is namespace-scoped set `Namespace`; a `token_bound_cidrs` or `bound_source_ips` rule may exclude this client."),

        [ErrorCodes.NotFoundPathNotFound] = new(
            ErrorCategory.NotFound,
            "Nothing exists at this path.",
            "Check the mount and the engine's path layout (`Details.path`); KV v2 data lives under `<mount>/data/<name>`; unregistered `sys/*` routes also answer 404."),

        [ErrorCodes.ConflictRecordingDigestMismatch] = new(
            ErrorCategory.Conflict,
            "The recording bytes do not match the recorded digest.",
            "Deterministic failure: do not retry; inspect the bastion and the sidecar digest."),

        [ErrorCodes.ConflictBrokeredResourceStaticCredential] = new(
            ErrorCategory.Conflict,
            "A static SSH credential cannot be attached to a brokered resource.",
            "Remove `private_key`/`password` or change the resource's `login_class`."),

        [ErrorCodes.RateLimitedByDosGuard] = new(
            ErrorCategory.RateLimit,
            "The server's abuse guard temporarily blocked this client IP.",
            "The rate gate is paused for `RetryAfter` seconds. Reduce request fan-out: use `Sys.Batch`, `Kv.ReadMany`, `*-info` pages and a read cache. Do not add retries."),

        [ErrorCodes.RateNamespaceQuotaExceeded] = new(
            ErrorCategory.RateLimit,
            "The namespace request-rate quota was exceeded.",
            "Slow down or ask an admin to raise `request_rate` on the namespace; back off before retrying."),

        [ErrorCodes.QuotaNamespaceQuotaExceeded] = new(
            ErrorCategory.Quota,
            "A namespace capacity quota was reached.",
            "`ServerMessage` names the quota (mounts, leases, entities, storage); free capacity or raise the quota via `Sys.UpdateNamespace`."),

        [ErrorCodes.ServerSealed] = new(
            ErrorCategory.ServerState,
            "The vault is sealed.",
            "An operator must unseal it (`bvault operator unseal` or HSM auto-unseal); the SDK does not retry. Use `Sys.Health` to watch for readiness."),

        [ErrorCodes.ServerUnavailable] = new(
            ErrorCategory.ServerState,
            "The server is temporarily unavailable.",
            "Cluster has no leader/quorum, node unhealthy, or HSM unreachable; the SDK retries idempotent calls. Check `Sys.ClusterStatus` and node health."),

        [ErrorCodes.ServerInternalError] = new(
            ErrorCategory.ServerState,
            "The server reported an internal error.",
            "Read `ServerMessage`; many engine validation errors are reported as 500 — the message names the field or object. Check server logs if it is generic."),
    };

    /// <summary>Every code above, used only by <see cref="StatusCodeMapper"/> to build the exception.</summary>
    internal static Entry Get(string code) => Entries[code];

    /// <summary>
    /// <c>Error.Retryable</c> follows ERR-006 exactly and is computed independently of
    /// <see cref="RetryPolicy.RetryOn"/> — the two sets differ (D-M1b-4b): <c>BV-TRANSPORT-003</c>
    /// and <c>BV-RATE-002</c> are retryable but never in a default <c>RetryOn</c>, and
    /// <c>RetryOn</c>'s members are also gated by the CFG-051 idempotency table, which
    /// <c>Retryable</c> is not.
    /// </summary>
    internal static bool IsRetryable(string code) => code switch
    {
        ErrorCodes.TransportConnectionFailed => true,
        ErrorCodes.TransportTimeout => true,
        ErrorCodes.TransportTlsError => true,
        ErrorCodes.ServerUnavailable => true,
        ErrorCodes.ServerStandby => true,
        ErrorCodes.RateNamespaceQuotaExceeded => true,
        ErrorCodes.DiscoveryNodeUnavailable => true,
        _ => false,
    };
}
