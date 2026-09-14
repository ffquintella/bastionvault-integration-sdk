namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The single status→code mapping function required by ERR-020 and D-M1b-4. It lands whole, in its
/// final home and with its final signature; only the branches decidable "by status alone, or by a
/// discriminator section 03 itself names" are populated at M1b. M1c adds step-by-step message
/// recognition (Appendix B §2) ahead of this function and the remaining Appendix B rows; it MUST
/// NOT reshape this function or the branches already here.
/// </summary>
internal static class StatusCodeMapper
{
    /// <summary>The request-scoped facts this function needs; nothing about retry state.</summary>
    internal readonly record struct Context(
        int StatusCode,
        string? ServerMessage,
        IReadOnlyList<string> ServerErrors,
        TimeSpan? RetryAfter,
        string Method,
        string Path,
        string Address,
        int Attempts);

    /// <summary>
    /// Maps a server response to a <see cref="BastionVaultException"/>. Only called for responses
    /// D-M1b-11/D-M1b-12 have already decided are errors (a <c>404</c> empty body on
    /// <c>Logical.Read</c>/<c>Logical.List</c> never reaches this function).
    /// </summary>
    public static BastionVaultException Map(Context context)
    {
        // Step 4 (ERR-020, D-M1c-3): the ordered Appendix B §2 rule list runs ahead of the status
        // table. No match falls through to ResolveCode unchanged.
        MessageRecognition.Recognised? recognised = MessageRecognition.Recognise(context.ServerMessage, context.StatusCode, context.Path);
        string code = recognised?.Code ?? ResolveCode(context);
        ErrorCatalogEntry entry = ErrorCatalog.Require(code);
        return BastionVaultException.Request(
            code,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: context.Attempts,
            serverMessage: context.ServerMessage,
            serverErrors: context.ServerErrors,
            statusCode: context.StatusCode,
            retryAfter: context.RetryAfter,
            method: context.Method,
            path: context.Path,
            address: context.Address,
            details: recognised?.Details);
    }

    /// <summary>
    /// Builds the <c>BV-PROTOCOL-002</c> error for a non-JSON body on a JSON endpoint (TRN-053).
    /// This is a content-type discriminator, not a status branch, so it is not part of
    /// <see cref="ResolveCode"/>'s switch.
    /// </summary>
    public static BastionVaultException MapNonJson(int statusCode, string snippet, string method, string path, string address, int attempts)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ProtocolUnexpectedResponse);
        Dictionary<string, object?> details = new(StringComparer.Ordinal) { ["snippet"] = snippet };
        return BastionVaultException.Request(
            ErrorCodes.ProtocolUnexpectedResponse,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: attempts,
            statusCode: statusCode,
            method: method,
            path: path,
            address: address,
            details: details);
    }

    private static string ResolveCode(Context context) => context.StatusCode switch
    {
        401 => ErrorCodes.AuthUnauthenticated,
        403 => ErrorCodes.AuthzPermissionDenied,
        404 => ErrorCodes.NotFoundPathNotFound,
        405 => ErrorCodes.ProtocolMethodNotAllowed,
        // D-M1c-19: 04-error-model.md step 5 names BV-CONFLICT-001 for 409. M1b's Resolve409
        // guessed at BV-CONFLICT-002/003 from the body because message recognition did not exist
        // yet; Appendix B §2 now answers those at step 4 (the digest/sha256 rows and
        // `brokered_resource_no_static_credential`), so the heuristic is deleted rather than
        // adjusted — the smallest change that satisfies the requirement (CLA-007).
        409 => ErrorCodes.Conflict,
        416 => ErrorCodes.InputChunkIndexOutOfRange,
        429 => context.RetryAfter is not null ? ErrorCodes.RateLimitedByDosGuard : ErrorCodes.RateNamespaceRateQuotaExceeded,
        500 => ErrorCodes.ServerInternalError,
        502 or 504 => ErrorCodes.ServerUnavailable,
        // D-M1c-23: 04-error-model.md step 5 says 503 => BV-SERVER-002, flatly. M1b's Resolve503
        // sniffed the body for "sealed" because message recognition did not exist yet; Appendix B
        // §2 answers that at step 4 (`exact bastionvault is sealed` and `contains (5xx) is
        // sealed`), leaving the heuristic to cover only a 503 saying "sealed" without "is
        // sealed" — which no fixture covers and no specification row describes. Deleted, not
        // adjusted (CLA-007), same shape as D-M1c-19.
        503 => ErrorCodes.ServerUnavailable,
        507 => ErrorCodes.QuotaNamespaceQuotaExceeded,
        >= 300 and <= 399 => ErrorCodes.ProtocolUnexpectedRedirect, // 304 is handled before this function is ever called.
        // D-M1b-21's principle holds: an unmapped status is still a BastionVaultException,
        // never a generic exception (TRN-054, ERR-020's "callers never catch a runtime
        // exception type"). D-M1c-12 corrects which code it is — 04-error-model.md step 5 maps
        // 400 and every other unmapped 4xx to BV-INPUT-100, which D-M1b-21 could not use because
        // the hand-transcribed catalogue did not carry it. BV-INPUT-001 stays what its message
        // says it is: client-side argument validation, raised before any request.
        >= 400 and <= 499 => ErrorCodes.InputServerRejectedRequest,
        >= 500 and <= 599 => ErrorCodes.ServerInternalError,
        _ => ErrorCodes.ProtocolUnexpectedResponse,
    };
}
