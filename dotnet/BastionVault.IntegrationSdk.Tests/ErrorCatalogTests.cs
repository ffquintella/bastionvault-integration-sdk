using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// The M1c error-model tests: Appendix B §3's catalogue invariants, the §2 matcher and its
/// D-M1c-4 captures, ERR-002/ERR-003's string form, and the seven ERR-040 enrichment rows that
/// landed. Everything is driven through the public surface and the real
/// <see cref="BastionVaultClient"/> — the project has no <c>InternalsVisibleTo</c>, and the
/// recognition and enrichment rules are deliberately not public (D-M1c-7), so they are tested
/// where a caller meets them.
/// </summary>
public sealed class ErrorCatalogTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    [Requirement("ERR-036")]
    [Requirement("ERR-037")]
    [Trait("Requirement", "ERR-036")]
    public void Catalogue_is_exhaustive_and_every_message_is_unique()
    {
        Assert.NotEmpty(ErrorCatalog.All);
        Assert.All(ErrorCatalog.All, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Message));
            Assert.False(string.IsNullOrWhiteSpace(entry.Hint));
            Assert.StartsWith("BV-", entry.Code, StringComparison.Ordinal);
        });

        Assert.Equal(
            ErrorCatalog.All.Count,
            ErrorCatalog.All.Select(entry => entry.Message).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ErrorCatalog.All.Count,
            ErrorCatalog.All.Select(entry => entry.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Requirement("ERR-036")]
    [Requirement("ERR-010")]
    [Trait("Requirement", "ERR-036")]
    public void Catalogue_matches_the_appendix_row_for_row_and_in_appendix_order()
    {
        // Compared against tools/error-catalogue/catalogue.json — the generator's checked-in
        // intermediate — not against a second markdown parser written in C#. The repo-gates
        // regeneration job ties that file to Appendix B; a second parser here would be a third
        // chance to read the appendix differently, which is exactly what D-M1c-1 removes.
        using JsonDocument intermediate = LoadIntermediate();
        JsonElement[] rows = intermediate.RootElement.GetProperty("codes").EnumerateArray().ToArray();

        Assert.Equal(rows.Length, ErrorCatalog.All.Count);
        for (int index = 0; index < rows.Length; index++)
        {
            ErrorCatalogEntry entry = ErrorCatalog.All[index];
            Assert.Equal(rows[index].GetProperty("code").GetString(), entry.Code);
            Assert.Equal(rows[index].GetProperty("name").GetString(), entry.Name);
            Assert.Equal(rows[index].GetProperty("message").GetString(), entry.Message);
            Assert.Equal(rows[index].GetProperty("hint").GetString(), entry.Hint);
            Assert.Equal(rows[index].GetProperty("retryable").GetBoolean(), entry.Retryable);
            Assert.Matches(@"^BV-[A-Z]+-\d{3}$", entry.Code);
        }
    }

    [Fact]
    [Requirement("ERR-006")]
    [Trait("Requirement", "ERR-006")]
    public void Retryable_is_true_for_exactly_the_ERR_006_set()
    {
        string[] expected =
        [
            ErrorCodes.TransportConnectionFailed,
            ErrorCodes.TransportTimeout,
            ErrorCodes.TransportTlsError,
            ErrorCodes.ServerUnavailable,
            ErrorCodes.ServerStandby,
            ErrorCodes.RateNamespaceRateQuotaExceeded,
            ErrorCodes.DiscoveryNodeUnavailable,
        ];

        Assert.Equal(
            expected.OrderBy(code => code, StringComparer.Ordinal),
            ErrorCatalog.All.Where(entry => entry.Retryable).Select(entry => entry.Code).OrderBy(code => code, StringComparer.Ordinal));
    }

    [Fact]
    [Requirement("ERR-004")]
    [Trait("Requirement", "ERR-004")]
    public void Every_category_matches_its_code_prefix()
    {
        Assert.Equal(ErrorCategory.Configuration, ErrorCatalog.Get(ErrorCodes.ConfigInvalidAddress)!.Category);
        Assert.Equal(ErrorCategory.Authorization, ErrorCatalog.Get(ErrorCodes.AuthzPermissionDenied)!.Category);
        Assert.Equal(ErrorCategory.Authentication, ErrorCatalog.Get(ErrorCodes.AuthNoToken)!.Category);
        Assert.Equal(ErrorCategory.Engine, ErrorCatalog.Get(ErrorCodes.KvCasMismatch)!.Category);
        Assert.Equal(ErrorCategory.Engine, ErrorCatalog.Get(ErrorCodes.RustionEnvelopeReplay)!.Category);
        Assert.Equal(ErrorCategory.Quota, ErrorCatalog.Get(ErrorCodes.QuotaNamespaceQuotaExceeded)!.Category);
        Assert.Equal(ErrorCategory.RateLimit, ErrorCatalog.Get(ErrorCodes.RateNamespaceRateQuotaExceeded)!.Category);
        Assert.Equal(ErrorCategory.NotFound, ErrorCatalog.Get(ErrorCodes.NotFoundPathNotFound)!.Category);
        Assert.Equal(ErrorCategory.Discovery, ErrorCatalog.Get(ErrorCodes.DiscoveryNodeUnavailable)!.Category);
    }

    [Fact]
    [Requirement("ERR-036")]
    [Trait("Requirement", "ERR-036")]
    public void Get_returns_null_for_an_unknown_code_and_never_throws()
    {
        Assert.Null(ErrorCatalog.Get("BV-NOPE-999"));
        Assert.Null(ErrorCatalog.Get(string.Empty));
        Assert.Null(ErrorCatalog.Get(null!));
        Assert.NotNull(ErrorCatalog.Get(ErrorCodes.ServerSealed));
    }

    [Fact]
    [Requirement("ERR-036")]
    [Trait("Requirement", "ERR-036")]
    public void Entry_is_a_value_with_a_readable_string_form()
    {
        ErrorCatalogEntry entry = ErrorCatalog.Get(ErrorCodes.ServerSealed)!;
        ErrorCatalogEntry same = new(entry.Code, entry.Name, entry.Category, entry.Message, entry.Hint, entry.Retryable);
        ErrorCatalogEntry other = entry with { Retryable = true };

        Assert.Equal(entry, same);
        Assert.True(entry == same);
        Assert.False(entry != same);
        Assert.NotEqual(entry, other);
        Assert.Equal(entry.GetHashCode(), same.GetHashCode());
        Assert.Contains("BV-SERVER-001", entry.ToString(), StringComparison.Ordinal);
        (string code, string name, _, _, _, bool retryable) = entry;
        Assert.Equal(ErrorCodes.ServerSealed, code);
        Assert.Equal("Sealed", name);
        Assert.False(retryable);
    }

    [Fact]
    [Requirement("ERR-036")]
    [Trait("Requirement", "ERR-036")]
    public void Configuration_errors_quote_the_generated_catalogue_verbatim()
    {
        // The M1a BV-CONFIG-* construction path predates the generator and still carries its own
        // copy of the strings. This is what stops the two drifting now that Appendix B is the
        // single source (D-M1c-1).
        AssertConfigError(ErrorCodes.ConfigInvalidAddress, new BastionVaultClientOptions { Address = "not a url" });
        AssertConfigError(ErrorCodes.ConfigInsecureHttpNotAllowed, new BastionVaultClientOptions { Address = "http://vault.example.com:8200" });
        AssertConfigError(ErrorCodes.ConfigInvalidNamespace, new BastionVaultClientOptions { Address = Address, Namespace = "/bad namespace" });
        AssertConfigError(
            ErrorCodes.ConfigReservedHeader,
            new BastionVaultClientOptions
            {
                Address = Address,
                Headers = new Dictionary<string, string> { ["X-BastionVault-Token"] = "x" },
            });
    }

    private static void AssertConfigError(string code, BastionVaultClientOptions options)
    {
        ErrorCatalogEntry expected = ErrorCatalog.Get(code)!;
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(options, EnvironmentSource.None));

        Assert.Equal(code, exception.Code);
        Assert.Equal(expected.Message, exception.Message);
        Assert.Equal(expected.Hint, exception.Hint);
        Assert.Equal(expected.Category, exception.Category);
    }

    [Theory]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    [InlineData("  Permission denied.  ", ErrorCodes.AuthzPermissionDenied)]
    [InlineData("PERMISSION DENIED", ErrorCodes.AuthzPermissionDenied)]
    [InlineData("Account temporarily locked (retry after 300s)", ErrorCodes.AuthAccountLocked)]
    [InlineData("Account temporarily locked (retry after 300s).", ErrorCodes.AuthAccountLocked)]
    [InlineData("Account is disabled", ErrorCodes.AuthAccountDisabled)]
    public async Task Normalisation_trims_strips_the_stop_the_retry_suffix_and_the_case(string message, string expected)
    {
        Assert.Equal(expected, (await FailAsync(400, message)).Code);
    }

    [Theory]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("something the catalogue has never heard of")]
    public async Task Recognition_falls_through_to_the_status_table_when_no_rule_matches(string message)
    {
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, (await FailAsync(400, message)).Code);
    }

    [Fact]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    public async Task Unrecognised_messages_reach_each_D_M1b_4_status_branch()
    {
        Assert.Equal(ErrorCodes.AuthUnauthenticated, (await FailAsync(401, "authentication required by this listener")).Code);
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, (await FailAsync(403, "forbidden by the listener")).Code);
        Assert.Equal(ErrorCodes.NotFoundPathNotFound, (await FailAsync(404, "nothing here")).Code);
        Assert.Equal(ErrorCodes.ProtocolMethodNotAllowed, (await FailAsync(405, "method rejected")).Code);
        // D-M1c-12: 400 and every other unmapped 4xx, not just 400.
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, (await FailAsync(400, "unexplained")).Code);
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, (await FailAsync(418, "unexplained teapot")).Code);
        Assert.Equal(ErrorCodes.ServerInternalError, (await FailAsync(500, "unexplained")).Code);
        Assert.Equal(ErrorCodes.QuotaNamespaceQuotaExceeded, (await FailAsync(507, "out of capacity")).Code);
    }

    [Fact]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    public async Task Status_guards_keep_a_5xx_only_rule_off_a_4xx()
    {
        // `exact bastionvault is sealed / contains (5xx) is sealed`.
        Assert.Equal(ErrorCodes.ServerSealed, (await FailAsync(503, "The vault is sealed right now.")).Code);
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, (await FailAsync(400, "The vault is sealed right now.")).Code);
    }

    [Fact]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    public async Task The_409_recordings_rule_is_scoped_to_a_recordings_path()
    {
        // `contains (409, recordings) sha256 / digest` answers at step 4, and only inside a
        // recordings path. The exact `brokered_resource_no_static_credential` row also answers
        // at step 4. Nothing here comes from a body-sniffing heuristic any more (D-M1c-19).
        Assert.Equal(
            ErrorCodes.ConflictRecordingDigestMismatch,
            (await FailAsync(409, "sha256 mismatch", path: "rustion/recordings/a/chunk/0")).Code);
        Assert.Equal(
            ErrorCodes.ConflictRecordingDigestMismatch,
            (await FailAsync(409, "digest mismatch", path: "rustion/recordings/a/chunk/0")).Code);
        Assert.Equal(
            ErrorCodes.ConflictBrokeredResourceStaticCredential,
            (await FailAsync(409, "brokered_resource_no_static_credential", path: "ssh/resources/a")).Code);

        // The same words outside the recordings scope are no longer guessed at: the 409 status
        // row answers, as 04-error-model.md step 5 says it must.
        Assert.Equal(ErrorCodes.Conflict, (await FailAsync(409, "digest mismatch on upload", path: "rustion/blobs/a")).Code);
        Assert.Equal(ErrorCodes.Conflict, (await FailAsync(409, "brokered resource cannot take a static credential", path: "ssh/resources/a")).Code);
    }

    [Fact]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    public async Task An_unrecognised_409_maps_to_BV_CONFLICT_001()
    {
        // D-M1c-19. M1b's Resolve409 returned BV-CONFLICT-002 for every unrecognised 409,
        // including an empty body, and no fixture reached it. 04-error-model.md step 5 names
        // BV-CONFLICT-001; the heuristic is deleted, not adjusted.
        BastionVaultException wordy = await FailAsync(409, "the object changed under you", path: "secret/data/x");
        BastionVaultException empty = await FailEmptyAsync(409, path: "ssh/resources/a");

        Assert.Equal(ErrorCodes.Conflict, wordy.Code);
        Assert.Equal(ErrorCodes.Conflict, empty.Code);
        Assert.Equal(ErrorCategory.Conflict, empty.Category);
        Assert.False(empty.Retryable);
    }

    [Fact]
    [Requirement("ERR-020")]
    [Trait("Requirement", "ERR-020")]
    public async Task Prefix_rules_keep_the_appendix_trailing_space_and_compound_rules_need_both_parts()
    {
        // `prefix key ` + contains `already exists with type`. The trailing space in Appendix B's
        // literal is load-bearing: it stops this rule swallowing `key_name`.
        Assert.Equal(ErrorCodes.TransitKeyTypeConflict, (await FailAsync(400, "Key app already exists with type aes256-gcm96")).Code);
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, (await FailAsync(400, "key_name already exists with type aes256-gcm96")).Code);
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, (await FailAsync(400, "Key app is fine")).Code);
    }

    [Fact]
    [Requirement("ERR-035")]
    [Trait("Requirement", "ERR-035")]
    public async Task Every_details_capture_shape_extracts_what_D_M1c_4_pins()
    {
        BastionVaultException policy = await FailAsync(403, "Cannot assign policy app-admin: not granted.");
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, policy.Code);
        Assert.Equal("app-admin", policy.Details["policy"]);

        BastionVaultException locked = await FailAsync(400, "Account temporarily locked (retry after 300s).");
        Assert.Equal(ErrorCodes.AuthAccountLocked, locked.Code);
        Assert.Equal(300, locked.Details["retry_after_secs"]);

        BastionVaultException source = await FailAsync(403, "Source address 203.0.113.17 is unauthorized.");
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, source.Code);
        Assert.Equal("203.0.113.17", source.Details["source_ip"]);

        BastionVaultException batch = await FailAsync(400, "Batch has 200 operations, exceeds max 128.");
        Assert.Equal(ErrorCodes.InputBatchTooLarge, batch.Code);
        Assert.Equal(200, batch.Details["count"]);
        Assert.Equal(128, batch.Details["max"]);

        BastionVaultException meta = await FailAsync(400, "Meta key(s) username, spiffe_id are reserved.");
        Assert.Equal(ErrorCodes.InputReservedTokenMetaKey, meta.Code);
        Assert.Equal(["username", "spiffe_id"], Assert.IsAssignableFrom<IReadOnlyList<string>>(meta.Details["keys"]));

        BastionVaultException missing = await FailAsync(404, "No such namespace dti/esi.");
        Assert.Equal(ErrorCodes.NotFoundNamespaceNotFound, missing.Code);
        Assert.Equal("dti/esi", missing.Details["namespace"]);

        BastionVaultException named = await FailAsync(404, "No policy named app-read.");
        Assert.Equal(ErrorCodes.NotFoundPolicyNotFound, named.Code);
        Assert.Equal("app-read", named.Details["policy"]);
    }

    [Fact]
    [Requirement("ERR-035")]
    [Trait("Requirement", "ERR-035")]
    public async Task A_capture_that_cannot_match_is_not_an_error_the_code_still_lands()
    {
        // D-M1c-4: recognition must never be more fragile than the code it produces.
        await AssertCodeWithoutKey(400, "Account temporarily locked", ErrorCodes.AuthAccountLocked, "retry_after_secs");
        await AssertCodeWithoutKey(400, "Batch has many operations, exceeds max quota", ErrorCodes.InputBatchTooLarge, "count");
        await AssertCodeWithoutKey(400, "Meta key(s)  are reserved", ErrorCodes.InputReservedTokenMetaKey, "keys");
        await AssertCodeWithoutKey(404, "No policy named ", ErrorCodes.NotFoundPolicyNotFound, "policy");
        await AssertCodeWithoutKey(403, "Unrelated wording; cannot assign policy is not in it", ErrorCodes.AuthzPermissionDenied, "policy");
    }

    private static async Task AssertCodeWithoutKey(int status, string message, string code, string key)
    {
        BastionVaultException exception = await FailAsync(status, message);

        Assert.Equal(code, exception.Code);
        Assert.DoesNotContain(key, exception.Details.Keys, StringComparer.Ordinal);
    }

    [Fact]
    [Requirement("ERR-001")]
    [Requirement("ERR-003")]
    [Trait("Requirement", "ERR-001")]
    public async Task Path_carries_the_namespace_display_prefix_and_stays_redacted()
    {
        // 04-error-model.md's Error field table defines Path as the logical path *with* the
        // namespace prefix for display (`[ns=dti/esi] secret/data/x`). .NET recorded the raw
        // path through M1a and M1b and no fixture caught it; rust/.../logical.rs display_path
        // and python/.../logical.py _display_path are the forms matched here, byte for byte.
        BastionVaultException namespaced = await FailAsync(
            403, "Permission denied.", path: "secret/data/x", configure: o => o.Namespace = "dti/esi");
        Assert.Equal("[ns=dti/esi] secret/data/x", namespaced.Path);
        Assert.Equal("[ns=dti/esi] secret/data/x", namespaced.Details["path"]);

        // No prefix, and no leading slash, when the namespace is empty.
        Assert.Equal("secret/data/x", (await FailAsync(403, "Permission denied.", path: "secret/data/x")).Path);

        // ERR-003 survives the prefixing: redaction runs over the display form and still finds
        // the token segment, whichever way round the two are applied.
        BastionVaultException token = await FailAsync(
            403,
            "Permission denied.",
            path: "auth/token/lookup/" + FakeTokens.Client,
            configure: o => o.Namespace = "dti/esi");
        Assert.Equal("[ns=dti/esi] auth/token/lookup/<redacted>", token.Path);
        Assert.DoesNotContain(FakeTokens.Client, token.Path!, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeTokens.Client, token.ToString(), StringComparison.Ordinal);
        Assert.Contains("[HTTP 403 GET [ns=dti/esi] auth/token/lookup/<redacted>]", token.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("ERR-034")]
    [Trait("Requirement", "ERR-034")]
    public async Task The_interpolated_path_is_the_redacted_display_path()
    {
        // Deliberate, and matched to rust/.../logical.rs finish_error: Details["path"], the
        // ERR-034 note and the ERR-040 context all read the redacted *display* path, so the
        // path a developer reads in the hint is the path the error reports.
        BastionVaultException notFound = await FailAsync(
            404, "unmapped", path: "secret/app", configure: o => o.Namespace = "dti");

        Assert.Equal(ErrorCodes.NotFoundPathNotFound, notFound.Code);
        Assert.Contains("The path as sent was `[ns=dti] secret/app`.", notFound.Hint, StringComparison.Ordinal);
    }

    [Theory]
    [Requirement("ERR-003")]
    [Trait("Requirement", "ERR-003")]
    [InlineData("/auth/token/lookup/s.secret", "/auth/token/lookup/<redacted>")]
    [InlineData("/auth/token/renew/s.secret", "/auth/token/renew/<redacted>")]
    [InlineData("/auth/token/revoke/s.secret", "/auth/token/revoke/<redacted>")]
    [InlineData("/auth/token/revoke-orphan/s.secret", "/auth/token/revoke-orphan/<redacted>")]
    [InlineData("/secret/data/app", "/secret/data/app")]
    [InlineData("lookup", "lookup")]
    [InlineData("/auth/token/lookup/", "/auth/token/lookup/")]
    [InlineData("", "")]
    public void Path_redaction_replaces_only_the_token_segments(string input, string expected)
    {
        Assert.Equal(expected, Build(path: input).Path);
    }

    [Fact]
    [Requirement("ERR-003")]
    [Trait("Requirement", "ERR-003")]
    public void A_path_free_error_stays_path_free()
    {
        Assert.Null(Build(path: null).Path);
    }

    [Fact]
    [Requirement("ERR-002")]
    [Requirement("ERR-003")]
    [Trait("Requirement", "ERR-002")]
    public void One_line_form_has_no_newline_even_when_the_server_message_carries_one()
    {
        BastionVaultException exception = Build(
            path: "/auth/token/lookup/" + FakeTokens.Client,
            serverMessage: "line one\nline two\r\nline three");

        string line = exception.ToString();

        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", line, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeTokens.Client, line, StringComparison.Ordinal);
        Assert.StartsWith("BV-SERVER-005: ", line, StringComparison.Ordinal);
        Assert.Contains(" — ", line, StringComparison.Ordinal);
        Assert.Contains("[HTTP 500 GET /auth/token/lookup/<redacted>]", line, StringComparison.Ordinal);
        Assert.Contains("(server: \"line one line two line three\")", line, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("ERR-002")]
    [Trait("Requirement", "ERR-002")]
    public void One_line_form_omits_the_bracket_and_the_server_clause_when_there_is_nothing_to_show()
    {
        ErrorCatalogEntry entry = ErrorCatalog.Get(ErrorCodes.ConfigInvalidAddress)!;
        BastionVaultException exception = new(
            entry.Code, entry.Category, entry.Message, entry.Hint, retryable: false);

        Assert.Equal($"{entry.Code}: {entry.Message} — {entry.Hint}", exception.ToString());
    }

    [Fact]
    [Requirement("ERR-034")]
    [Trait("Requirement", "ERR-034")]
    public async Task A_hint_that_points_at_details_path_names_the_path_the_sdk_sent()
    {
        BastionVaultException notFound = await FailAsync(404, "unmapped", path: "secret/app");

        Assert.Equal(ErrorCodes.NotFoundPathNotFound, notFound.Code);
        Assert.Contains("Details.path", notFound.Hint, StringComparison.Ordinal);
        Assert.Contains("The path as sent was `secret/app`.", notFound.Hint, StringComparison.Ordinal);
        Assert.Equal("secret/app", notFound.Details["path"]);

        // A hint that does not mention Details.path is left alone.
        BastionVaultException sealedVault = await FailAsync(503, "BastionVault is sealed.", path: "secret/app");
        Assert.Equal(ErrorCodes.ServerSealed, sealedVault.Code);
        Assert.DoesNotContain("The path as sent was", sealedVault.Hint, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("ERR-021")]
    [Requirement("ERR-040")]
    [Trait("Requirement", "ERR-040")]
    public async Task Enrichment_row_403_no_namespace_fires_only_under_auth_or_secret_with_no_namespace()
    {
        const string Note = "No namespace is set";

        Assert.Contains(Note, (await FailAsync(403, "Permission denied.", path: "secret/data/x")).Hint, StringComparison.Ordinal);
        Assert.Contains(Note, (await FailAsync(403, "Permission denied.", path: "auth/userpass/users/bob")).Hint, StringComparison.Ordinal);
        Assert.DoesNotContain(Note, (await FailAsync(403, "Permission denied.", path: "sys/health")).Hint, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Note,
            (await FailAsync(403, "Permission denied.", path: "secret/data/x", configure: o => o.Namespace = "dti")).Hint,
            StringComparison.Ordinal);
        // ERR-021: a 403 is authorization, never authentication.
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, (await FailAsync(403, "Permission denied.", path: "secret/data/x")).Code);
    }

    [Fact]
    [Requirement("ERR-040")]
    [Trait("Requirement", "ERR-040")]
    public async Task Enrichment_rows_for_api_version_rate_gate_sealed_and_unsupported_path()
    {
        Assert.Contains(
            "Pin this call to `/v2` (RequestOptions.ApiVersion = 2).",
            (await FailAsync(400, "API version mismatch: not on this version.")).Hint,
            StringComparison.Ordinal);

        BastionVaultException throttled = await FailAsync(
            429, "request temporarily blocked by DoS protection", headers: new Dictionary<string, string> { ["Retry-After"] = "17" });
        Assert.Equal(ErrorCodes.RateLimitedByDosGuard, throttled.Code);
        Assert.Contains("paused for `17`s", throttled.Hint, StringComparison.Ordinal);

        // A 429 without Retry-After gets no rate-gate note.
        Assert.DoesNotContain("paused for", (await FailAsync(429, "too many requests")).Hint, StringComparison.Ordinal);

        Assert.Contains(
            "Run `bvault operator unseal` on the node or wait for auto-unseal; the SDK will not retry.",
            (await FailAsync(503, "BastionVault is sealed.")).Hint,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "bvault operator unseal",
            (await FailAsync(503, "cluster node is unhealthy")).Hint,
            StringComparison.Ordinal);

        // CNF-043 / D-M1c-6: one recognition row, no bespoke code path.
        BastionVaultException unsupported = await FailAsync(500, "Logical backend path not supported.");
        Assert.Equal(ErrorCodes.ServerUnsupportedByServer, unsupported.Code);
        Assert.Contains("does not have this endpoint", unsupported.Hint, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("ERR-040")]
    [Trait("Requirement", "ERR-040")]
    public async Task Enrichment_rows_for_tls_without_a_ca_and_connection_refused_to_the_default_address()
    {
        const string TlsNote = "Provide the server's CA bundle via `CaCertPath` or `BASTIONVAULT_CACERT`.";
        const string AddressNote = "No `Address` was configured; the default is `https://127.0.0.1:8200`.";

        Assert.Contains(TlsNote, (await FailTransportAsync(Internal.TransportFailureKind.TlsVerify)).Hint, StringComparison.Ordinal);
        Assert.DoesNotContain(
            TlsNote,
            (await FailTransportAsync(Internal.TransportFailureKind.TlsVerify, o => o.CaCertPem = SelfSignedCaPem())).Hint,
            StringComparison.Ordinal);

        Assert.Contains(
            AddressNote,
            (await FailTransportAsync(Internal.TransportFailureKind.ConnectionRefused, o => o.Address = "https://127.0.0.1:8200")).Hint,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            AddressNote,
            (await FailTransportAsync(Internal.TransportFailureKind.ConnectionRefused)).Hint,
            StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("ERR-040")]
    [Trait("Requirement", "ERR-040")]
    public async Task The_two_deferred_enrichment_rows_are_absent_not_stubbed()
    {
        // D-M1c-5: no branch exists for `Details.namespace_operable == false` (M3) or for a 404 on
        // a KV v2 mount (M4), so neither can fire early and neither is dead code the CNF-010 floor
        // would have to excuse. A 404 on a KV-v2-shaped path gets the catalogue hint, the ERR-034
        // path note, and nothing else.
        BastionVaultException notFound = await FailAsync(404, "unmapped", path: "secret/app");

        Assert.DoesNotContain("This mount is KV v2", notFound.Hint, StringComparison.Ordinal);
        Assert.DoesNotContain("child-visible", notFound.Hint, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("ERR-001")]
    [Trait("Requirement", "ERR-001")]
    public void Every_canonical_ERR_001_field_is_present_on_the_error_type()
    {
        string[] canonical =
        [
            "Code", "Category", "Message", "Hint", "ServerMessage", "ServerErrors", "StatusCode",
            "RetryAfter", "Retryable", "Method", "Path", "Address", "Attempts", "Details", "Cause",
            "Timestamp",
        ];
        HashSet<string> actual = typeof(BastionVaultException)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(canonical, name => Assert.Contains(name, actual, StringComparer.Ordinal));

        // And every one of them is actually populated on a request-scoped error.
        BastionVaultException exception = Build(path: "/secret/data/x", serverMessage: "boom");
        Assert.NotNull(exception.Code);
        Assert.NotNull(exception.Hint);
        Assert.NotNull(exception.ServerErrors);
        Assert.NotNull(exception.Details);
        Assert.NotEqual(default, exception.Timestamp);
        Assert.Equal(1, exception.Attempts);
    }

    [Fact]
    [Requirement("ERR-005")]
    [Trait("Requirement", "ERR-005")]
    public void Every_code_is_reachable_as_a_constant_and_as_its_literal_string()
    {
        Dictionary<string, string> constants = typeof(ErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => (string)field.GetRawConstantValue()!, field => field.Name, StringComparer.Ordinal);

        Assert.Equal(ErrorCatalog.All.Count, constants.Count);
        Assert.All(ErrorCatalog.All, entry => Assert.Contains(entry.Code, constants.Keys, StringComparer.Ordinal));

        // The literal form correlates logs across SDKs without referencing the constant.
        Assert.Equal("BV-AUTHZ-001", ErrorCodes.AuthzPermissionDenied);
        Assert.Equal("AuthzPermissionDenied", constants["BV-AUTHZ-001"]);
        Assert.Equal("RateNamespaceRateQuotaExceeded", constants["BV-RATE-002"]);
    }

    [Fact]
    [Requirement("ERR-030")]
    [Trait("Requirement", "ERR-030")]
    public void Every_message_is_one_sentence_in_the_present_tense()
    {
        Assert.All(ErrorCatalog.All, entry =>
        {
            Assert.EndsWith(".", entry.Message, StringComparison.Ordinal);
            Assert.Equal(1, SentenceCount(entry.Message));
            Assert.False(entry.Message.Contains(" will ", StringComparison.Ordinal), entry.Code);
            Assert.False(entry.Message.StartsWith("Please", StringComparison.Ordinal), entry.Code);
        });
    }

    [Fact]
    [Requirement("ERR-031")]
    [Trait("Requirement", "ERR-031")]
    public void Every_hint_is_at_most_two_sentences()
    {
        // D-M1c-13: Appendix B carried a three-sentence hint (BV-RATE-001) from M0 to M1c with
        // no gate on it. The generator now hard-fails on one; this is the same invariant
        // asserted against the compiled catalogue, counted the same way (backticked spans and
        // decimal points masked before splitting).
        Assert.All(ErrorCatalog.All, entry =>
            Assert.True(SentenceCount(entry.Hint) <= 2, $"{entry.Code}: {SentenceCount(entry.Hint)} sentences"));

        Assert.Equal(2, SentenceCount(ErrorCatalog.Get(ErrorCodes.RateLimitedByDosGuard)!.Hint));
        Assert.Equal(1, SentenceCount("Use `Sys.Batch` or `*-info` pages."));
        Assert.Equal(2, SentenceCount("Do this. Then do that."));
    }

    [Fact]
    [Requirement("ERR-033")]
    [Trait("Requirement", "ERR-033")]
    public void No_hint_offers_disabling_a_security_control_as_its_first_suggestion()
    {
        string[] unsafeKnobs = ["TlsSkipVerify", "AllowInsecureHttp", "InsecureSkipVerify"];

        Assert.All(ErrorCatalog.All, entry =>
        {
            foreach (string knob in unsafeKnobs)
            {
                int index = entry.Hint.IndexOf(knob, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                // It may appear, but never first, and never without an explicit warning.
                Assert.True(SentenceCount(entry.Hint[..index]) >= 1, $"{entry.Code}: {knob} is the first suggestion");
                Assert.True(
                    entry.Hint.Contains("never in production", StringComparison.OrdinalIgnoreCase)
                        || entry.Hint.Contains("only for isolated test networks", StringComparison.OrdinalIgnoreCase)
                        || entry.Hint.Contains("diagnostic", StringComparison.OrdinalIgnoreCase),
                    $"{entry.Code}: {knob} is mentioned without a warning");
            }
        });
    }

    /// <summary>Sentences, ignoring anything inside backticks and decimal points.</summary>
    private static int SentenceCount(string text)
    {
        string masked = System.Text.RegularExpressions.Regex.Replace(text, "`[^`]*`", "X");
        masked = System.Text.RegularExpressions.Regex.Replace(masked, @"(?<=\d)\.(?=\d)", string.Empty);
        return System.Text.RegularExpressions.Regex
            .Split(masked, @"[.!?](?:\s|$)")
            .Count(part => part.Trim().Length > 0);
    }

    // ----------------------------------------------------------------------- //

    private static JsonDocument LoadIntermediate()
    {
        FixtureRepository repository = new();
        string path = Path.Combine(repository.RepositoryRoot, "tools", "error-catalogue", "catalogue.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static BastionVaultException Build(string? path, string? serverMessage = null)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Get(ErrorCodes.ServerInternalError)!;
        return new BastionVaultException(
            entry.Code,
            entry.Category,
            entry.Message,
            entry.Hint,
            entry.Retryable,
            attempts: 1,
            serverMessage: serverMessage,
            statusCode: 500,
            method: "GET",
            path: path);
    }

    private static BastionVaultClient BuildClient(FakeTransport transport, Action<BastionVaultClientOptions>? configure)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = FakeTokens.Client,
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static async Task<BastionVaultException> FailAsync(
        int status,
        string serverMessage,
        string path = "secret/data/x",
        IReadOnlyDictionary<string, string>? headers = null,
        Action<BastionVaultClientOptions>? configure = null)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(
            status,
            headers,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = serverMessage })));
        BastionVaultClient client = BuildClient(transport, configure);

        return await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync(path));
    }

    private static async Task<BastionVaultException> FailEmptyAsync(int status, string path)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(status);
        BastionVaultClient client = BuildClient(transport, configure: null);

        return await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync(path));
    }

    private static async Task<BastionVaultException> FailTransportAsync(
        Internal.TransportFailureKind kind,
        Action<BastionVaultClientOptions>? configure = null)
    {
        FakeTransport transport = new();
        transport.EnqueueFailure(kind);
        BastionVaultClient client = BuildClient(transport, configure);

        return await Assert.ThrowsAsync<BastionVaultException>(() => client.Logical.ReadAsync("secret/data/x"));
    }

    private static string SelfSignedCaPem()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=bastionvault-m1c-test-ca", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return certificate.ExportCertificatePem();
    }
}
