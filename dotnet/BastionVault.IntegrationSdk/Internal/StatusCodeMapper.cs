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
        string code = ResolveCode(context);
        ErrorCatalogue.Entry entry = ErrorCatalogue.Get(code);
        return BastionVaultException.Request(
            code,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: ErrorCatalogue.IsRetryable(code),
            attempts: context.Attempts,
            serverMessage: context.ServerMessage,
            serverErrors: context.ServerErrors,
            statusCode: context.StatusCode,
            retryAfter: context.RetryAfter,
            method: context.Method,
            path: context.Path,
            address: context.Address);
    }

    /// <summary>
    /// Builds the <c>BV-PROTOCOL-002</c> error for a non-JSON body on a JSON endpoint (TRN-053).
    /// This is a content-type discriminator, not a status branch, so it is not part of
    /// <see cref="ResolveCode"/>'s switch.
    /// </summary>
    public static BastionVaultException MapNonJson(int statusCode, string snippet, string method, string path, string address, int attempts)
    {
        ErrorCatalogue.Entry entry = ErrorCatalogue.Get(ErrorCodes.ProtocolUnexpectedResponse);
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
        409 => Resolve409(context.ServerMessage),
        416 => ErrorCodes.InputChunkIndexOutOfRange,
        429 => context.RetryAfter is not null ? ErrorCodes.RateLimitedByDosGuard : ErrorCodes.RateNamespaceQuotaExceeded,
        500 => ErrorCodes.ServerInternalError,
        502 or 504 => ErrorCodes.ServerUnavailable,
        503 => Resolve503(context.ServerMessage),
        507 => ErrorCodes.QuotaNamespaceQuotaExceeded,
        >= 300 and <= 399 => ErrorCodes.ProtocolUnexpectedRedirect, // 304 is handled before this function is ever called.
        // D-M1b-21: an unmapped status is still a BastionVaultException, never a generic
        // exception (TRN-054, ERR-020's "callers never catch a runtime exception type").
        // Falls back by status class; ServerMessage passes through unchanged so M1c's message
        // recognition refines this into a precise code rather than replacing a crash.
        >= 400 and <= 499 => ErrorCodes.InputInvalidArgument,
        >= 500 and <= 599 => ErrorCodes.ServerInternalError,
        _ => ErrorCodes.ProtocolUnexpectedResponse,
    };

    // 503's only M1b discriminator is the "sealed" body, named explicitly by D-M1b-4.
    private static string Resolve503(string? serverMessage)
        => serverMessage is not null && serverMessage.Contains("sealed", StringComparison.OrdinalIgnoreCase)
            ? ErrorCodes.ServerSealed
            : ErrorCodes.ServerUnavailable;

    // 409 has no fixture at M1b; this best-effort discrimination is not exercised by any
    // conformance fixture and is revisited at M1c alongside full message recognition.
    private static string Resolve409(string? serverMessage)
    {
        if (serverMessage is not null)
        {
            if (serverMessage.Contains("digest", StringComparison.OrdinalIgnoreCase)
                || serverMessage.Contains("sha256", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCodes.ConflictRecordingDigestMismatch;
            }

            if (serverMessage.Contains("brokered", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCodes.ConflictBrokeredResourceStaticCredential;
            }
        }

        return ErrorCodes.ConflictRecordingDigestMismatch;
    }
}
