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
    /// <summary>
    /// The request-scoped facts this function needs; nothing about retry state.
    /// </summary>
    /// <remarks>
    /// <c>BodyEmpty</c> says whether the response body was empty or whitespace. It is carried
    /// because AUT-084's shape is "<c>404</c> <b>empty body</b>", and a null <c>ServerMessage</c>
    /// does not mean the same thing: a <c>404</c> carrying <c>{}</c>, or any JSON body with no
    /// <c>error</c>/<c>errors</c> field, also yields a null message while plainly not being an
    /// empty body. Both call sites already knew the answer — <c>ExecuteRawAsync</c> was computing
    /// it and discarding it.
    /// </remarks>
    internal readonly record struct Context(
        int StatusCode,
        string? ServerMessage,
        IReadOnlyList<string> ServerErrors,
        TimeSpan? RetryAfter,
        string Method,
        string Path,
        string Address,
        int Attempts,
        bool BodyEmpty = false);

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
        string code = RefineForTokenStore(recognised, context);
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

    /// <summary>
    /// AUT-084 and AUT-085: two section-05 refinements that Appendix B §2 cannot express, because
    /// they turn on the <b>request path</b> and not on the server message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AUT-084's shape is a <c>404</c> with an <i>empty body</i> — there is no message to
    /// recognise — and AUT-085's <c>400 Request is invalid.</c> is a message Appendix B §2 already
    /// claims for <c>BV-INPUT-100</c> generally, which is correct everywhere except on the renew
    /// path. Adding either as an Appendix B row would be a specification change and would make the
    /// generic rows path-scoped for every caller.
    /// </para>
    /// <para>
    /// So both live here, in the one shared mapping function every operation already goes through,
    /// rather than as a status branch inside an <c>Auth.*</c> operation: D-M2-4 forbids an auth
    /// operation owning its own status mapping, and the shared recogniser already scopes rules by
    /// path (<c>RecognitionRule.PathContains</c>), so this is the same mechanism and not a second
    /// table.
    /// </para>
    /// <para>
    /// Each refinement reproduces <b>every</b> condition its requirement states, and is gated on
    /// the <i>recognition outcome</i> rather than on the code that survives the status fallthrough.
    /// Gating on the code was M2a's F1 defect: <c>ResolveCode</c> sends every unmapped 4xx to
    /// <c>BV-INPUT-100</c> and Appendix B §2 has three further rows that yield it
    /// (<c>request field is not found</c>, <c>request field is invalid</c>, <c>no data field is
    /// available for the request</c>), so a <c>400</c> caused by the caller's own malformed
    /// body — including the missing-<c>increment</c> shape, and <c>increment</c> is required —
    /// became <c>BV-AUTH-015 TokenNotRenewable</c>. A caller branching on that code to re-login
    /// would have re-logged-in in response to its own bug.
    /// </para>
    /// </remarks>
    private static string RefineForTokenStore(MessageRecognition.Recognised? recognised, in Context context)
    {
        string code = recognised?.Code ?? ResolveCode(context);

        // AUT-084: a `Lookup` of an unknown token, which is a 404 *with an empty body* under
        // `auth/token/lookup/{token}`. A 404 carrying a body is the server saying something else;
        // `lookup-self` is a different endpoint for which the specification names no refinement,
        // and D-M1c-25 forbids inventing one.
        if (context.StatusCode == 404
            && context.BodyEmpty
            && recognised is null
            && IsUnder(context.Path, "auth/token/lookup/"))
        {
            return ErrorCodes.NotFoundTokenNotFound;
        }

        // AUT-085: `Renew` of an unknown/expired token, which the requirement pins to a 400 whose
        // message is exactly `Request is invalid.`. Tested against that row's own literal, so the
        // three sibling rows that share BV-INPUT-100 are not swept in with it.
        if (context.StatusCode == 400
            && code == ErrorCodes.InputServerRejectedRequest
            && IsRecognisedAs(recognised, context.ServerMessage, "request is invalid")
            && IsUnder(context.Path, "auth/token/renew/"))
        {
            return ErrorCodes.AuthTokenNotRenewable;
        }

        return code;
    }

    /// <summary>
    /// Whether the recogniser matched, and matched on the row whose literal is
    /// <paramref name="ruleText"/>. <see cref="MessageRecognition.Recognised"/> does not carry the
    /// row it came from, so the row is identified by re-applying D-M1c-3's normalisation to the
    /// server message and comparing against the same literal Appendix B §2 spells — which is what
    /// the <c>exact</c> rule itself compares.
    /// </summary>
    private static bool IsRecognisedAs(MessageRecognition.Recognised? recognised, string? serverMessage, string ruleText)
    {
        return recognised is not null
                && serverMessage is not null
                && string.Equals(MessageRecognition.Normalise(serverMessage), ruleText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the request path is <paramref name="prefix"/> followed by a further segment — an
    /// endpoint test, not a substring test. Substring matching was the other half of M2a's F1
    /// defect: <c>Contains("auth/token/lookup")</c> also matched <c>auth/token/lookup-self</c> and
    /// any caller path containing that text, such as <c>secret/data/auth/token/lookup/notes</c>.
    /// </summary>
    /// <remarks>
    /// The display path may carry ERR-001's <c>[ns=…] </c> prefix, which is stripped here, and is
    /// not yet ERR-003-redacted — the redaction replaces the token <i>segment</i>, never the
    /// <c>lookup</c>/<c>renew</c> segment anchoring the match, so the same test holds either side
    /// of it.
    /// </remarks>
    private static bool IsUnder(string path, string prefix)
    {
        int prefixEnd = path.IndexOf("] ", StringComparison.Ordinal);
        string logical = prefixEnd >= 0 ? path[(prefixEnd + 2)..] : path;
        logical = logical.TrimStart('/');
        return logical.StartsWith(prefix, StringComparison.Ordinal) && logical.Length > prefix.Length;
    }

    private static string ResolveCode(Context context)
    {
        return context.StatusCode switch
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
}
