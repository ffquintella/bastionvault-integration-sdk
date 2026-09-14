using System.Reflection;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// The M2a requirements whose assertions are not expressible as a single-threaded wire-shape
/// fixture: the token-source model (AUT-001, AUT-004), AUT-014's arithmetic and its
/// <c>MUST NOT expose</c> half, AUT-020, AUT-081…AUT-085's non-fixture arms, the token-helper write
/// path (CFG-031, CFG-032, BV-CONFIG-010), CFG-070's concurrency invariants (D-M2-11), and D-M2-9's
/// counter split.
/// </summary>
public sealed class AuthUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    [Requirement("AUT-001")]
    [Trait("Requirement", "AUT-001")]
    public void Client_holds_exactly_one_token_source_and_SetToken_replaces_it_with_Static()
    {
        BastionVaultClient client = BuildClient(new FakeTransport(), options => options.Token = FakeTokens.Client);

        Assert.Equal(TokenSourceKind.Static, client.Auth.TokenSource.Kind);
        Assert.Equal(FakeTokens.Client, client.Auth.CurrentToken!.Reveal());

        // A Callback source is one source, not a second one alongside the configured token.
        BastionVaultClient callbackClient = BuildClient(new FakeTransport());
        TokenSource callback = TokenSource.Callback(_ => Task.FromResult(new SecretString(FakeTokens.Child)));
        Assert.Equal(TokenSourceKind.Callback, callback.Kind);

        // SetToken replaces whatever the source was with a Static one (AUT-001).
        callbackClient.SetToken(new SecretString(FakeTokens.Rotated));
        Assert.Equal(TokenSourceKind.Static, callbackClient.Auth.TokenSource.Kind);
        Assert.Equal(FakeTokens.Rotated, callbackClient.Auth.CurrentToken!.Reveal());

        // And so does Auth.Token.Use (AUT-020's other half).
        callbackClient.Auth.Token.Use(new SecretString(FakeTokens.Explicit));
        Assert.Equal(TokenSourceKind.Static, callbackClient.Auth.TokenSource.Kind);
        Assert.Equal(FakeTokens.Explicit, callbackClient.Auth.CurrentToken!.Reveal());
    }

    [Fact]
    [Requirement("AUT-004")]
    [Trait("Requirement", "AUT-004")]
    public async Task CurrentToken_redacts_and_TokenInfo_holds_the_last_LookupSelf()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LookupBody(creationTtl: 3600)));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        Assert.Null(client.Auth.TokenInfo);
        Assert.DoesNotContain(FakeTokens.Client, client.Auth.CurrentToken!.ToString(), StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", client.Auth.CurrentToken!.ToString());

        TokenInfo info = await client.Auth.Token.LookupSelfAsync();

        Assert.Same(info, client.Auth.TokenInfo);
        Assert.Equal("alice", client.Auth.TokenInfo!.DisplayName);
        // AUT-004's redaction extends to TokenInfo.Id, which *is* the token: a plain string there
        // would be a second, non-redacting way to read what CurrentToken must redact.
        Assert.Equal("[REDACTED]", info.Id!.ToString());

        client.ClearToken();
        Assert.Null(client.Auth.CurrentToken);
    }

    [Fact]
    [Requirement("AUT-014")]
    [Trait("Requirement", "AUT-014")]
    public async Task RemainingTtl_is_computed_from_the_clock_and_is_null_when_creation_ttl_is_zero()
    {
        DateTimeOffset start = DateTimeOffset.FromUnixTimeSeconds(1789300800);
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(LookupBody(creationTtl: 3600)));
        transport.EnqueueResponse(200, body: Json(LookupBody(creationTtl: 0)));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Token = FakeTokens.Client;
            options.Clock = new FrozenClock(start + TimeSpan.FromMinutes(10));
        });

        TokenInfo withTtl = await client.Auth.Token.LookupSelfAsync();
        TokenInfo withoutTtl = await client.Auth.Token.LookupSelfAsync();

        // creation_time + creation_ttl − NowUtc = 1789300800 + 3600 − (start + 10m).
        Assert.Equal(TimeSpan.FromMinutes(50), withTtl.RemainingTtl);
        Assert.Null(withoutTtl.RemainingTtl);
        Assert.Equal(TimeSpan.FromHours(1), withTtl.CreationTtl);
        Assert.Equal(start, withTtl.CreationTime);
    }

    [Fact]
    [Requirement("AUT-014")]
    [Trait("Requirement", "AUT-014")]
    public void TokenInfo_does_not_expose_the_wire_ttl_field()
    {
        // AUT-014's second half is a prohibition, and a prohibition is only verifiable negatively:
        // the wire `ttl` is always 0, so a `Ttl` member would read as "no TTL" for every token.
        string[] members = typeof(TokenInfo)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Ttl", members);
        Assert.Contains("RemainingTtl", members);
        Assert.Contains("CreationTtl", members);
        Assert.Contains("ExplicitMaxTtl", members);
    }

    [Fact]
    [Requirement("AUT-020")]
    [Trait("Requirement", "AUT-020")]
    public void Use_rejects_empty_and_whitespace_tokens_with_BV_INPUT_001_and_sends_nothing()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        foreach (string candidate in new[] { string.Empty, " ", "\t\r\n" })
        {
            BastionVaultException exception = Assert.Throws<BastionVaultException>(
                () => client.Auth.Token.Use(new SecretString(candidate)));

            Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
            Assert.Equal(0, exception.Attempts);
            Assert.False(exception.Retryable);
            Assert.Equal("token", exception.Details["argument"]);
        }

        BastionVaultException nullCase = Assert.Throws<BastionVaultException>(
            () => client.Auth.Token.Use(new SecretString(null)));
        Assert.Equal(ErrorCodes.InputInvalidArgument, nullCase.Code);

        Assert.Empty(transport.Requests);
        // The rejected call left the existing source untouched.
        Assert.Equal(FakeTokens.Client, client.Auth.CurrentToken!.Reveal());
    }

    [Fact]
    [Requirement("AUT-081")]
    [Trait("Requirement", "AUT-081")]
    public async Task Create_refuses_every_reserved_meta_key_and_the_approle_env_prefix_before_any_request()
    {
        string[] reserved =
        [
            "spiffe_id", "machine_id", "username", "entity_id", "mount_path", "role_name", "role",
            "namespace_path", "namespace_id", "child_visible", "auth_method", "groups", "subject",
            "name_id", "name_id_format", "ferrogate_kid", "session_id", "approle_machine_bypass",
            "machine_identity_exempt", "approle_env_scoped", "approle_env_secret",
            "approle_env_machine", "approle_env_anything_at_all",
        ];

        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        foreach (string key in reserved)
        {
            CreateTokenRequest request = new() { Meta = new Dictionary<string, string> { [key] = "x" } };

            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
                () => client.Auth.Token.CreateAsync(request));

            Assert.Equal(ErrorCodes.InputReservedTokenMetaKey, exception.Code);
            Assert.Equal(0, exception.Attempts);
            Assert.Null(exception.StatusCode);
            Assert.Contains(key, (string[])exception.Details["keys"]!);
            // ERR-034's principle: the hint names the keys actually sent, not only the families.
            Assert.Contains(key, exception.Hint, StringComparison.Ordinal);
        }

        // An unreserved key is not refused, and `meta` reaching the wire proves the guard is not
        // simply rejecting every `meta`.
        transport.EnqueueResponse(200, body: Json(CreateBody()));
        AuthInfo created = await client.Auth.Token.CreateAsync(
            new CreateTokenRequest { Meta = new Dictionary<string, string> { ["purpose"] = "batch" } });

        Assert.Equal(FakeTokens.Child, created.ClientToken.Reveal());
        Assert.Contains("\"purpose\":\"batch\"", BodyOf(transport.Requests[0]), StringComparison.Ordinal);
        Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("AUT-082")]
    [Trait("Requirement", "AUT-082")]
    public async Task Create_returns_AuthInfo_and_switches_the_client_token_only_when_UseResult_is_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(CreateBody()));
        transport.EnqueueResponse(200, body: Json(CreateBody()));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        AuthInfo notUsed = await client.Auth.Token.CreateAsync(new CreateTokenRequest { Policies = ["default"] });

        Assert.Equal(FakeTokens.Child, notUsed.ClientToken.Reveal());
        Assert.Equal(FakeTokens.Client, client.Auth.CurrentToken!.Reveal());
        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.EndsWith("/v1/auth/token/create", transport.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        // `renewable` defaults to true, as the server's does, and UseResult never reaches the wire.
        Assert.Contains("\"renewable\":true", BodyOf(transport.Requests[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("UseResult", BodyOf(transport.Requests[0]), StringComparison.Ordinal);

        await client.Auth.Token.CreateAsync(new CreateTokenRequest { UseResult = true });

        Assert.Equal(FakeTokens.Child, client.Auth.CurrentToken!.Reveal());
        Assert.Equal(TokenSourceKind.Static, client.Auth.TokenSource.Kind);
    }

    [Fact]
    [Requirement("AUT-082")]
    [Trait("Requirement", "AUT-082")]
    public async Task Create_serialises_every_request_field_to_its_wire_name()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(CreateBody()));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        await client.Auth.Token.CreateAsync(new CreateTokenRequest
        {
            Policies = ["default", "ops"],
            Ttl = TimeSpan.FromMinutes(30),
            Period = TimeSpan.FromHours(2),
            NumUses = 5,
            Renewable = false,
            Meta = new Dictionary<string, string> { ["purpose"] = "x" },
            DisplayName = "batch-runner",
            ExplicitMaxTtl = TimeSpan.FromHours(8),
            NoDefaultPolicy = true,
            NoParent = true,
            Id = "chosen-id",
            Type = "batch",
            ChildVisible = false,
        });

        string body = BodyOf(transport.Requests[0]);
        Assert.Contains("\"policies\":[\"default\",\"ops\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"ttl\":1800", body, StringComparison.Ordinal);
        Assert.Contains("\"period\":7200", body, StringComparison.Ordinal);
        Assert.Contains("\"num_uses\":5", body, StringComparison.Ordinal);
        Assert.Contains("\"renewable\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"display_name\":\"batch-runner\"", body, StringComparison.Ordinal);
        Assert.Contains("\"explicit_max_ttl\":28800", body, StringComparison.Ordinal);
        Assert.Contains("\"no_default_policy\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"no_parent\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"chosen-id\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"batch\"", body, StringComparison.Ordinal);
        Assert.Contains("\"child_visible\":false", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("AUT-083")]
    [Trait("Requirement", "AUT-083")]
    public async Task RevokeSelf_clears_the_local_token_on_a_root_token_and_leaves_it_on_failure()
    {
        // A root-policy token is accepted but not revoked server-side; the response is a 204
        // either way, so the SDK cannot tell them apart and clears its token in both cases.
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        await client.Auth.Token.RevokeSelfAsync();

        Assert.Null(client.Auth.CurrentToken);
        Assert.EndsWith("/v1/auth/token/revoke-self", transport.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);

        // A failed revoke must not clear it: the token is still live.
        FakeTransport failing = new();
        failing.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient stillAuthenticated = BuildClient(failing, options => options.Token = FakeTokens.Client);

        await Assert.ThrowsAsync<BastionVaultException>(() => stillAuthenticated.Auth.Token.RevokeSelfAsync());

        Assert.Equal(FakeTokens.Client, stillAuthenticated.Auth.CurrentToken!.Reveal());
    }

    [Fact]
    [Requirement("AUT-084")]
    [Requirement("AUT-085")]
    [Trait("Requirement", "AUT-085")]
    public async Task Token_store_status_refinements_are_scoped_to_their_paths_and_leave_other_paths_alone()
    {
        // AUT-084: a 404 with an empty body on a lookup is TokenNotFound, not PathNotFound.
        FakeTransport lookup = new();
        lookup.EnqueueResponse(404);
        BastionVaultClient lookupClient = BuildClient(lookup, options => options.Token = FakeTokens.Client);
        BastionVaultException notFound = await Assert.ThrowsAsync<BastionVaultException>(
            () => lookupClient.Auth.Token.LookupAsync(FakeTokens.Explicit));
        Assert.Equal(ErrorCodes.NotFoundTokenNotFound, notFound.Code);

        // AUT-085: `400 Request is invalid.` on a renew is TokenNotRenewable, even though
        // Appendix B §2 claims that exact message for BV-INPUT-100 generally.
        FakeTransport renew = new();
        renew.EnqueueResponse(400, body: Json("""{"error":"Request is invalid."}"""));
        BastionVaultClient renewClient = BuildClient(renew, options => options.Token = FakeTokens.Client);
        BastionVaultException notRenewable = await Assert.ThrowsAsync<BastionVaultException>(
            () => renewClient.Auth.Token.RenewAsync(FakeTokens.Explicit, 3600));
        Assert.Equal(ErrorCodes.AuthTokenNotRenewable, notRenewable.Code);

        // The generic rows are untouched off those paths: the same body and status on a logical
        // read still maps the way M1c decided.
        FakeTransport logical = new();
        logical.EnqueueResponse(400, body: Json("""{"error":"Request is invalid."}"""));
        BastionVaultClient logicalClient = BuildClient(logical, options => options.Token = FakeTokens.Client);
        BastionVaultException generic = await Assert.ThrowsAsync<BastionVaultException>(
            () => logicalClient.Logical.WriteAsync("secret/data/x"));
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, generic.Code);

        // The same 404-with-empty-body that AUT-084 refines on a lookup path still resolves to
        // BV-NOTFOUND-001 off it, which is what makes the refinement path-scoped rather than a
        // change to the 404 row.
        FakeTransport missingPath = new();
        missingPath.EnqueueResponse(404);
        BastionVaultClient missingPathClient = BuildClient(missingPath, options => options.Token = FakeTokens.Client);
        BastionVaultException pathNotFound = await Assert.ThrowsAsync<BastionVaultException>(
            () => missingPathClient.Logical.WriteAsync("secret/data/x"));
        Assert.Equal(ErrorCodes.NotFoundPathNotFound, pathNotFound.Code);
        Assert.NotEqual(ErrorCodes.NotFoundTokenNotFound, pathNotFound.Code);
    }

    [Fact]
    [Requirement("AUT-080")]
    [Requirement("ERR-003")]
    [Requirement("CFG-080")]
    [Requirement("TST-051")]
    [Trait("Requirement", "AUT-080")]
    public async Task RenewSelf_puts_the_token_in_the_path_and_the_observer_never_sees_it()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        List<RequestEvent> events = new();
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Token = FakeTokens.Client;
            options.Observer = new CollectingObserver(events);
        });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Token.RenewSelfAsync(3600));

        // The wire carries the live token, because AUT-080 says there is no `renew-self` path.
        Assert.Contains(FakeTokens.Client, transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        // Nothing the SDK surfaces does. This is the leak D-M2-7 named: RequestEvent.Path is the
        // second consumer of the path string after the error, and before M2a it was unredacted.
        Assert.Equal("auth/token/renew/<redacted>", exception.Path);
        Assert.DoesNotContain(FakeTokens.Client, exception.Hint, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeTokens.Client, exception.ToString(), StringComparison.Ordinal);
        Assert.NotEmpty(events);
        Assert.All(events, captured =>
        {
            Assert.Equal("auth/token/renew/<redacted>", captured.Path);
            Assert.DoesNotContain(FakeTokens.Client, captured.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Requirement("AUT-080")]
    [Trait("Requirement", "AUT-080")]
    public async Task The_remaining_token_store_operations_hit_their_specified_paths_and_verbs()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json(LookupBody(creationTtl: 60)));
        transport.EnqueueResponse(200, body: Json(CreateBody()));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        await client.Auth.Token.RevokeAsync(FakeTokens.Explicit);
        await client.Auth.Token.RevokeOrphanAsync(FakeTokens.Explicit);
        await client.Auth.Token.AuditLoginAsync();
        TokenInfo verified = await client.Auth.Token.VerifyAsync();
        AuthInfo renewed = await client.Auth.Token.RenewSelfAsync(3600);

        Assert.Equal($"/v1/auth/token/revoke/{FakeTokens.Explicit}", transport.Requests[0].Uri.AbsolutePath);
        Assert.Equal($"/v1/auth/token/revoke-orphan/{FakeTokens.Explicit}", transport.Requests[1].Uri.AbsolutePath);
        Assert.Equal("/v1/auth/token/audit-login", transport.Requests[2].Uri.AbsolutePath);
        Assert.Equal("/v1/auth/token/lookup-self", transport.Requests[3].Uri.AbsolutePath);
        Assert.Equal($"/v1/auth/token/renew/{FakeTokens.Client}", transport.Requests[4].Uri.AbsolutePath);
        Assert.All(transport.Requests.Skip(1).Take(2), request => Assert.Equal("POST", request.Method));
        Assert.Equal("GET", transport.Requests[3].Method);
        Assert.Contains("\"increment\":3600", BodyOf(transport.Requests[4]), StringComparison.Ordinal);
        Assert.Equal("alice", verified.DisplayName);
        Assert.True(renewed.Renewable);

        // Verify() is a LookupSelf, so it records TokenInfo too (AUT-004).
        Assert.NotNull(client.Auth.TokenInfo);
    }

    [Fact]
    [Requirement("AUT-080")]
    [Trait("Requirement", "AUT-080")]
    public async Task An_envelope_without_the_field_the_operation_requires_is_a_protocol_error_not_a_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        // `data` present but no `auth`: Create's response contract is an envelope `auth` object.
        BastionVaultException missingAuth = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Token.CreateAsync(new CreateTokenRequest()));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, missingAuth.Code);
        Assert.Equal("auth", missingAuth.Details["field"]);

        // No envelope at all (a 204 where an `auth` object was promised) is the same verdict.
        BastionVaultException noEnvelope = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Token.RenewSelfAsync(60));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noEnvelope.Code);
        Assert.Equal("auth", noEnvelope.Details["field"]);

        // A 204 to a lookup: no `data` at all.
        BastionVaultException missingData = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Token.LookupSelfAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, missingData.Code);
        Assert.Equal("data", missingData.Details["field"]);
    }

    [Fact]
    [Requirement("AUT-080")]
    [Trait("Requirement", "AUT-080")]
    public async Task Token_store_operations_reject_an_empty_token_argument_before_any_request()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        await Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Token.LookupAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Token.RenewAsync(string.Empty, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Token.RevokeAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Token.RevokeOrphanAsync(" "));
        Assert.Throws<ArgumentNullException>(() => client.Auth.Token.Use(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.Auth.Token.CreateAsync(null!));

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("AUT-014")]
    [Trait("Requirement", "AUT-014")]
    public async Task A_lookup_whose_data_omits_or_mistypes_every_field_yields_a_TokenInfo_of_absences()
    {
        // TRN-043's forward-compatibility posture: an unexpected shape is not an exception. Every
        // optional field is absent or of another JSON kind, and each one degrades to "not present"
        // rather than to a guess — D-M1c-25's rule applied to parsing, and the arms that carry it
        // are otherwise never exercised by a well-formed fixture.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        transport.EnqueueResponse(200, body: Json(
            """
            {"data":{"id":1,"policies":"default","path":2,"meta":"nope","display_name":3,
                     "num_uses":"many","creation_time":"yesterday","creation_ttl":"an hour",
                     "explicit_max_ttl":null,"period":"forever"}}
            """));
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"creation_ttl":3600,"policies":["a",null],"meta":{"k":null}}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        TokenInfo absent = await client.Auth.Token.LookupSelfAsync();
        TokenInfo mistyped = await client.Auth.Token.LookupSelfAsync();
        TokenInfo ttlWithoutCreationTime = await client.Auth.Token.LookupSelfAsync();

        foreach (TokenInfo info in new[] { absent, mistyped })
        {
            Assert.Null(info.Id);
            Assert.Empty(info.Policies);
            Assert.Null(info.Path);
            Assert.Null(info.Meta);
            Assert.Null(info.DisplayName);
            Assert.Equal(0, info.NumUses);
            Assert.Null(info.CreationTime);
            Assert.Equal(TimeSpan.Zero, info.CreationTtl);
            Assert.Equal(TimeSpan.Zero, info.ExplicitMaxTtl);
            Assert.Null(info.Period);
            Assert.Null(info.RemainingTtl);
        }

        // AUT-014's arithmetic needs both operands: a creation_ttl with no creation_time is not a
        // RemainingTtl of "now + ttl", it is no RemainingTtl at all.
        Assert.Equal(TimeSpan.FromHours(1), ttlWithoutCreationTime.CreationTtl);
        Assert.Null(ttlWithoutCreationTime.CreationTime);
        Assert.Null(ttlWithoutCreationTime.RemainingTtl);
        Assert.Equal(new[] { "a", string.Empty }, ttlWithoutCreationTime.Policies);
        Assert.Equal(string.Empty, ttlWithoutCreationTime.Meta!["k"]);
    }

    [Fact]
    [Requirement("AUT-001")]
    [Trait("Requirement", "AUT-001")]
    // Deliberately carries no CFG-020 marker. This asserts only CFG-020's *first* MUST (a client
    // works without a token); its second — an authenticated operation refused client-side with
    // BV-AUTH-001 before any network call — is M2b's, alongside ERR-022 and AUT-002 (D-M2-1).
    // Claiming CFG-020 here would remove it from the ratchet on one half of the requirement, which
    // is the CFG-001 false positive DR-0006 cites twice as the thing not to repeat.
    public async Task A_callback_that_yields_nothing_behaves_as_no_token_rather_than_faulting()
    {
        // A Callback is the application's own function, so the SDK must survive it yielding an
        // empty SecretString or nothing at all: that is "no token", not a fault (BV-AUTH-017 is
        // for a source that *failed*). CFG-020 then decides what happens next, and M2b landed both
        // halves of it — an unauthenticated endpoint still works with no token header, and an
        // authenticated operation is refused client-side before any request is sent.
        foreach (Func<CancellationToken, Task<SecretString>> callback in new Func<CancellationToken, Task<SecretString>>[]
        {
            _ => Task.FromResult(SecretString.Empty),
            _ => Task.FromResult<SecretString>(null!),
        })
        {
            FakeTransport transport = new();
            transport.EnqueueResponse(200, body: Json("""{"initialized":true,"sealed":false}"""));
            BastionVaultClient client = BuildClient(transport, options => options.TokenSource = TokenSource.Callback(callback));

            Assert.Null(client.Auth.CurrentToken);

            // CFG-020's first list: `sys/health` works without a token, and carries no token header.
            await client.Logical.ReadAsync("sys/health");
            Assert.DoesNotContain("X-BastionVault-Token", transport.Requests[0].Headers.Keys);

            // CFG-020's second MUST: an authenticated operation is refused before any network call,
            // so the transport sees nothing further even though the callback answered without
            // error. AUT-080's RenewSelf is refused on the same rule, which retires M2a's
            // "RenewSelf with no token sends auth/token/renew/" — that was the absence of this
            // requirement, not a decision.
            BastionVaultException read = await Assert.ThrowsAsync<BastionVaultException>(
                () => client.Logical.ReadAsync("secret/data/x"));
            BastionVaultException renew = await Assert.ThrowsAsync<BastionVaultException>(
                () => client.Auth.Token.RenewSelfAsync(60));

            Assert.Equal(ErrorCodes.AuthNoToken, read.Code);
            Assert.Equal(ErrorCodes.AuthNoToken, renew.Code);
            Assert.Single(transport.Requests);
        }
    }

    // ---- CFG-070 (D-M2-11): the two concurrency invariants, under real concurrency. ----

    [Fact]
    [Requirement("CFG-070")]
    [Trait("Requirement", "CFG-070")]
    public async Task Concurrent_first_use_resolutions_of_a_Login_source_await_one_login_not_N()
    {
        // D-M2-11(a). The failure this asserts against is thundering-herd login: eight concurrent
        // operations at startup all find no cached token, all log in, seven tokens are orphaned,
        // and the server's DoS guard on the login path answers a later one with `rate_limited: …`
        // → BV-RATE-001, which ERR-006 makes non-retryable. So the application's first request
        // fails at startup. `Lazy<Task<T>>` with ExecutionAndPublication is the pinned primitive
        // precisely because a semaphore around a null check admits the interleaving where two
        // callers both observe "no cached token" before either takes the lock.
        const int concurrency = 8;
        CountingLogin login = new(FakeTokens.Child);
        TokenSource source = TokenSource.LoginWith(login.PerformAsync);

        Task<SecretString?>[] resolutions = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => source.ResolveAsync()))
            .ToArray();

        await login.WaitUntilEntered().ConfigureAwait(false);
        login.Release();
        SecretString?[] tokens = await Task.WhenAll(resolutions).ConfigureAwait(false);

        Assert.Equal(1, login.Invocations);
        Assert.All(tokens, token => Assert.Equal(FakeTokens.Child, token!.Reveal()));

        // A later resolution is served from the cached flight: still one login.
        Assert.Equal(FakeTokens.Child, (await source.ResolveAsync().ConfigureAwait(false))!.Reveal());
        Assert.Equal(1, login.Invocations);

        // Invalidation re-arms the cell rather than clearing it, so the next N concurrent
        // resolutions again await exactly one login — two in total, not two plus a herd.
        login.Reset();
        source.Invalidate();
        Task<SecretString?>[] second = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => source.ResolveAsync()))
            .ToArray();
        await login.WaitUntilEntered().ConfigureAwait(false);
        login.Release();
        await Task.WhenAll(second).ConfigureAwait(false);
        Assert.Equal(2, login.Invocations);
    }

    [Fact]
    [Requirement("CFG-070")]
    [Trait("Requirement", "CFG-070")]
    public async Task A_Callback_source_is_deliberately_not_deduplicated_and_a_Static_source_is_a_read()
    {
        // D-M2-11(a) excludes both: a Callback is the application's own function and the SDK does
        // not get to coalesce its calls on its behalf, and a Static source has nothing to resolve.
        int callbackInvocations = 0;
        TokenSource callback = TokenSource.Callback(_ =>
        {
            Interlocked.Increment(ref callbackInvocations);
            return Task.FromResult(new SecretString(FakeTokens.Child));
        });

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => callback.ResolveAsync())).ConfigureAwait(false);

        Assert.Equal(4, callbackInvocations);

        TokenSource @static = TokenSource.Static(new SecretString(FakeTokens.Client));
        Assert.Equal(FakeTokens.Client, (await @static.ResolveAsync().ConfigureAwait(false))!.Reveal());
        @static.Invalidate(); // A no-op on a Static source.
        Assert.Equal(FakeTokens.Client, (await @static.ResolveAsync().ConfigureAwait(false))!.Reveal());
    }

    [Fact]
    [Requirement("CFG-070")]
    [Trait("Requirement", "CFG-070")]
    public async Task An_in_flight_request_keeps_the_token_it_started_with_while_SetToken_races_it()
    {
        // CFG-070's second sentence. D-M1b-9 put the snapshot above the retry loop and D-M2-11(b)
        // deliberately did not move it: only its source changed. So a SetToken that lands after
        // the resolution and before the send must not change the header of the request already in
        // flight — and must be visible to the next one.
        GatedTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        Task<Response?> inFlight = client.Logical.ReadAsync("secret/data/x");
        await transport.WaitUntilEntered().ConfigureAwait(false);

        client.SetToken(new SecretString(FakeTokens.Rotated));
        Assert.Equal(FakeTokens.Rotated, client.Auth.CurrentToken!.Reveal());

        transport.Release(200, """{"data":{"a":1}}""");
        await inFlight.ConfigureAwait(false);

        Assert.Equal(FakeTokens.Client, transport.Requests[0].Headers["X-BastionVault-Token"]);

        // The next operation picks up the rotated token, which is what makes the first assertion
        // "kept its snapshot" rather than "never read the cell".
        transport.Release(200, """{"data":{"a":1}}""");
        Task<Response?> next = client.Logical.ReadAsync("secret/data/x");
        await transport.WaitUntilEntered().ConfigureAwait(false);
        transport.Release(200, """{"data":{"a":1}}""");
        await next.ConfigureAwait(false);
        Assert.Equal(FakeTokens.Rotated, transport.Requests[1].Headers["X-BastionVault-Token"]);
    }

    [Fact]
    [Requirement("CFG-070")]
    [Trait("Requirement", "CFG-070")]
    public async Task A_cancelled_resolution_abandons_only_its_own_wait_and_surfaces_a_coded_error()
    {
        // One caller's cancellation must not fail the others awaiting the same flight, which is
        // why the shared login runs under CancellationToken.None and each awaiter uses WaitAsync.
        CountingLogin login = new(FakeTokens.Child);
        TokenSource source = TokenSource.LoginWith(login.PerformAsync);
        using CancellationTokenSource cancelled = new();

        Task<SecretString?> patient = Task.Run(() => source.ResolveAsync());
        Task<SecretString?> impatient = Task.Run(() => source.ResolveAsync(cancelled.Token));
        await login.WaitUntilEntered().ConfigureAwait(false);
        await cancelled.CancelAsync().ConfigureAwait(false);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => impatient).ConfigureAwait(false);
        login.Release();
        Assert.Equal(FakeTokens.Child, (await patient.ConfigureAwait(false))!.Reveal());
        Assert.Equal(1, login.Invocations);

        // Through the executor, a cancelled resolution is a coded BV-TRANSPORT-005 and never a
        // bare OperationCanceledException (ERR-020, TRN-054).
        using CancellationTokenSource preCancelled = new();
        await preCancelled.CancelAsync().ConfigureAwait(false);
        BastionVaultClient client = BuildClient(new FakeTransport(), options =>
            options.TokenSource = TokenSource.Callback(token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new SecretString(FakeTokens.Child));
            }));

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Logical.ReadAsync("secret/data/x", cancellationToken: preCancelled.Token));

        Assert.Equal(ErrorCodes.TransportCancelled, exception.Code);
        Assert.Equal(0, exception.Attempts);
    }

    // ---- D-M2-9: the counter split and the hoisted requestId. ----

    [Fact]
    [Requirement("CFG-055")]
    [Requirement("CFG-080")]
    [Trait("Requirement", "CFG-055")]
    public async Task A_second_pass_keeps_one_requestId_accumulates_attempts_and_restarts_retry_eligibility()
    {
        // D-M2-9 ruling 2, which is the whole reason the counters are split. AUT-003's replay is
        // M2b's; this asserts the accounting it will use. The first pass burns all three attempts
        // on a retryable transport failure; the second pass starts with AttemptsBefore = 3 and
        // must still get its own three, or CFG-051…055 would silently not apply to the replay.
        FakeTransport transport = new();
        for (int index = 0; index < 2; index++)
        {
            transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        }

        transport.EnqueueResponse(200, body: Json("""{"data":{"a":1}}"""));
        List<RequestEvent> events = new();
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Token = FakeTokens.Client;
            options.Observer = new CollectingObserver(events);
            options.RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero };
        });

        RequestExecutor.RequestExecution execution = new("fixed-request-id", AttemptsBefore: 3);
        RequestExecutor.Outcome outcome = await Execute(client, execution).ConfigureAwait(false);

        Assert.False(outcome.IsEmpty);
        // Three attempts were made on this pass, so eligibility restarted: AttemptsBefore did not
        // exhaust MaxAttempts.
        Assert.Equal(3, events.Count);
        // One id across the whole caller-visible operation, supplied from above rather than minted
        // inside the loop (D-M1b-8, as amended).
        Assert.All(events, captured => Assert.Equal("fixed-request-id", captured.RequestId));
        // The observer reports the accumulated count: 3 already made, then 1, 2, 3.
        Assert.Equal(new[] { 4, 5, 6 }, events.Select(captured => captured.Attempt).ToArray());
    }

    [Fact]
    [Requirement("CFG-055")]
    [Trait("Requirement", "CFG-055")]
    public async Task A_thrown_error_reports_the_accumulated_attempt_count_not_the_per_pass_one()
    {
        FakeTransport transport = new();
        for (int index = 0; index < 3; index++)
        {
            transport.EnqueueFailure(TransportFailureKind.ConnectionRefused);
        }

        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Token = FakeTokens.Client;
            options.RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero };
        });

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => Execute(client, new RequestExecutor.RequestExecution("fixed-request-id", AttemptsBefore: 2)));

        // 2 before + 3 on this pass. CFG-055 makes the total observable on the error, and an
        // application that opted into a replay must not be told the first pass never happened.
        Assert.Equal(5, exception.Attempts);
    }

    // ---- CFG-031, CFG-032, BV-CONFIG-010: the token-helper write path. ----

    [Fact]
    [Requirement("CFG-031")]
    [Trait("Requirement", "CFG-031")]
    public void PersistToken_writes_the_current_token_with_owner_only_permissions()
    {
        string directory = Directory.CreateTempSubdirectory("bastionvault-persist-").FullName;
        try
        {
            string tokenFile = Path.Combine(directory, "token");
            BastionVaultClient client = BuildClient(new FakeTransport(), options =>
            {
                options.Token = FakeTokens.Client;
                options.UseTokenHelper = true;
                options.TokenFile = tokenFile;
            });

            Assert.False(File.Exists(tokenFile));

            client.Auth.PersistToken();

            Assert.Equal(FakeTokens.Client, File.ReadAllText(tokenFile));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(tokenFile));
            }

            // A later token overwrites, and the mode survives the overwrite.
            client.SetToken(new SecretString(FakeTokens.Rotated));
            client.Auth.PersistToken();
            Assert.Equal(FakeTokens.Rotated, File.ReadAllText(tokenFile));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(tokenFile));
            }

            // CFG-030 reads back what CFG-031 wrote.
            BastionVaultClient reloaded = BuildClient(new FakeTransport(), options =>
            {
                options.UseTokenHelper = true;
                options.TokenFile = tokenFile;
            });
            Assert.Equal(FakeTokens.Rotated, reloaded.Auth.CurrentToken!.Reveal());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Requirement("CFG-031")]
    [Trait("Requirement", "CFG-031")]
    public void PersistToken_refuses_when_there_is_no_token_and_reports_an_unwritable_path()
    {
        BastionVaultClient noToken = BuildClient(new FakeTransport(), options => options.TokenFile = Path.GetTempFileName());
        BastionVaultException empty = Assert.Throws<BastionVaultException>(noToken.Auth.PersistToken);
        Assert.Equal(ErrorCodes.InputInvalidArgument, empty.Code);
        Assert.Equal("CurrentToken", empty.Details["argument"]);

        string unwritable = Path.Combine(Path.GetTempPath(), $"bastionvault-absent-{Guid.NewGuid():n}", "nested", "token");
        BastionVaultClient client = BuildClient(new FakeTransport(), options =>
        {
            options.Token = FakeTokens.Client;
            options.TokenFile = unwritable;
        });

        BastionVaultException failure = Assert.Throws<BastionVaultException>(client.Auth.PersistToken);

        // BV-CONFIG-011, not BV-CONFIG-005: see AuthReviewFindingTests (F4, D-M2-16).
        Assert.Equal(ErrorCodes.ConfigTokenFileNotWritable, failure.Code);
        Assert.Equal(unwritable, failure.Details["path"]);
    }

    [Fact]
    [Requirement("CFG-032")]
    [Trait("Requirement", "CFG-032")]
    public void ForgetPersistedToken_deletes_the_file_and_does_not_fail_when_it_is_absent()
    {
        string directory = Directory.CreateTempSubdirectory("bastionvault-forget-").FullName;
        try
        {
            string tokenFile = Path.Combine(directory, "token");
            BastionVaultClient client = BuildClient(new FakeTransport(), options =>
            {
                options.Token = FakeTokens.Client;
                options.TokenFile = tokenFile;
            });

            client.Auth.PersistToken();
            Assert.True(File.Exists(tokenFile));

            client.Auth.ForgetPersistedToken();
            Assert.False(File.Exists(tokenFile));

            // Absent is success, twice over: a missing file and a missing directory.
            client.Auth.ForgetPersistedToken();

            BastionVaultClient nested = BuildClient(new FakeTransport(), options =>
            {
                options.Token = FakeTokens.Client;
                options.TokenFile = Path.Combine(directory, "no-such-directory", "token");
            });
            nested.Auth.ForgetPersistedToken();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Requirement("CFG-030")]
    [Trait("Requirement", "CFG-030")]
    public void An_encrypted_BVTOK1_token_file_is_refused_with_BV_CONFIG_010_and_its_appendix_B_hint()
    {
        string directory = Directory.CreateTempSubdirectory("bastionvault-bvtok1-").FullName;
        try
        {
            string tokenFile = Path.Combine(directory, "token");
            File.WriteAllText(tokenFile, "BVTOK1:Zm9vYmFyYmF6\n");

            BastionVaultException exception = Assert.Throws<BastionVaultException>(() => BuildClient(
                new FakeTransport(),
                options =>
                {
                    options.UseTokenHelper = true;
                    options.TokenFile = tokenFile;
                }));

            Assert.Equal(ErrorCodes.ConfigEncryptedTokenFile, exception.Code);
            Assert.Equal(ErrorCategory.Configuration, exception.Category);
            Assert.Equal("The token file is in the CLI's encrypted `BVTOK1:` format.", exception.Message);
            Assert.Contains("bvault ferrogate token --field client_token", exception.Hint, StringComparison.Ordinal);
            Assert.Contains("plaintext token files only", exception.Hint, StringComparison.Ordinal);
            Assert.Equal(tokenFile, exception.Details["path"]);
            // The ciphertext itself is never quoted back (CNF-031).
            Assert.DoesNotContain("Zm9vYmFyYmF6", exception.ToString(), StringComparison.Ordinal);

            // A plaintext file at the same path is still read (CFG-030 unchanged).
            File.WriteAllText(tokenFile, $"  {FakeTokens.Client}\n");
            BastionVaultClient client = BuildClient(new FakeTransport(), options =>
            {
                options.UseTokenHelper = true;
                options.TokenFile = tokenFile;
            });
            Assert.Equal(FakeTokens.Client, client.Auth.CurrentToken!.Reveal());

            // And the check does not fire when the helper is not opted into.
            File.WriteAllText(tokenFile, "BVTOK1:Zm9vYmFyYmF6\n");
            BastionVaultClient ignoring = BuildClient(new FakeTransport(), options => options.TokenFile = tokenFile);
            Assert.Null(ignoring.Auth.CurrentToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Requirement("ERR-034")]
    [Trait("Requirement", "ERR-034")]
    public void Key_interpolation_leaves_a_hint_alone_when_it_does_not_point_at_Details_keys()
    {
        Assert.Equal("no pointer here", HintEnrichment.InterpolateKeys("no pointer here", ["spiffe_id"]));
        Assert.Equal("see Details.keys", HintEnrichment.InterpolateKeys("see Details.keys", []));
        Assert.Equal(
            "see Details.keys The reserved key(s) sent were `a`, `b`.",
            HintEnrichment.InterpolateKeys("see Details.keys", ["a", "b"]));
    }

    // ---- helpers ----

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Transport = transport,
            Clock = new FrozenClock(DateTimeOffset.UnixEpoch),
            JitterSource = new FixedJitterSource(),
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    /// <summary>
    /// Drives the executor's D-M2-9 entry point directly, with an inbound <c>requestId</c> and a
    /// starting attempt count. In production those come from the AUT-003 replay, which is M2b's;
    /// the accounting is M2a's and is asserted here rather than left until the feature arrives.
    /// </summary>
    private static Task<RequestExecutor.Outcome> Execute(BastionVaultClient client, RequestExecutor.RequestExecution execution)
    {
        RequestExecutor executor = new(client.Context, client.Namespace);
        return executor.ExecuteAsync(
            "GET",
            "secret/data/x",
            null,
            null,
            defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true,
            CancellationToken.None,
            execution);
    }

    private static ReadOnlyMemory<byte> Json(string json) => Encoding.UTF8.GetBytes(json);

    private static string BodyOf(TransportRequest request) => Encoding.UTF8.GetString(request.Body.Span);

    private static string LookupBody(int creationTtl) => JsonSerializer.Serialize(new
    {
        renewable = false,
        lease_id = string.Empty,
        lease_duration = 0,
        auth = (object?)null,
        data = new
        {
            id = FakeTokens.Client,
            policies = new[] { "default" },
            path = "auth/userpass/login/alice",
            meta = new Dictionary<string, string> { ["username"] = "alice" },
            display_name = "alice",
            num_uses = 0,
            ttl = 0,
            creation_time = 1789300800,
            creation_ttl = creationTtl,
            explicit_max_ttl = 0,
        },
    });

    private static string CreateBody() => JsonSerializer.Serialize(new
    {
        auth = new
        {
            client_token = FakeTokens.Child,
            policies = new[] { "default" },
            metadata = new Dictionary<string, string>(),
            lease_duration = 3600,
            renewable = true,
        },
        data = new { },
    });

    private sealed class FrozenClock : IClock
    {
        private readonly DateTimeOffset now;

        public FrozenClock(DateTimeOffset now) => this.now = now;

        public DateTimeOffset NowUtc() => now;

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedJitterSource : IJitterSource
    {
        public double NextDouble() => 0.5;
    }

    private sealed class CollectingObserver : IRequestObserver
    {
        private readonly List<RequestEvent> events;

        public CollectingObserver(List<RequestEvent> events) => this.events = events;

        public void OnRequestCompleted(RequestEvent requestEvent) => events.Add(requestEvent);
    }

    /// <summary>
    /// The <see cref="TokenSource"/> test double D-M2-11(a) needs: its login records how often it
    /// was invoked and blocks on a gate the test releases, so "concurrent first-use resolutions
    /// await one login" is asserted while the login is genuinely in flight rather than after it.
    /// </summary>
    private sealed class CountingLogin
    {
        private readonly string token;
        private TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int invocations;

        public CountingLogin(string token) => this.token = token;

        public int Invocations => Volatile.Read(ref invocations);

        public async Task<SecretString> PerformAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref invocations);
            entered.TrySetResult();
            await gate.Task.ConfigureAwait(false);
            return new SecretString(token);
        }

        public Task WaitUntilEntered() => entered.Task;

        public void Release() => gate.TrySetResult();

        public void Reset()
        {
            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// A transport that parks each request until the test releases it, so a <c>SetToken</c> can be
    /// raced against a request that has already resolved its token and not yet been sent.
    /// </summary>
    private sealed class GatedTransport : ITransport
    {
        private readonly List<TransportRequest> requests = new();
        private readonly SemaphoreSlim released = new(0);
        private TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int status = 200;
        private string body = "{}";

        public IReadOnlyList<TransportRequest> Requests => requests;

        public bool SupportsCustomVerbs => true;

        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            lock (requests)
            {
                requests.Add(request);
            }

            entered.TrySetResult();
            await released.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new TransportResponse(
                status,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Encoding.UTF8.GetBytes(body));
        }

        public Task WaitUntilEntered() => entered.Task;

        public void Release(int responseStatus, string responseBody)
        {
            status = responseStatus;
            body = responseBody;
            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            released.Release();
        }
    }
}
