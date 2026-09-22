using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M6's section-05 remainder where a single-threaded wire-shape fixture cannot express the
/// assertion: the whole AUT-043 and AUT-054 path table, AUT-035's opaque-payload rules, AUT-051's
/// cache, AUT-052's status map, AUT-060's envelope reads and AUT-070's code override.
/// </summary>
/// <remarks>
/// Everything here drives the real <see cref="BastionVaultClient"/> through
/// <see cref="FakeTransport"/> and asserts what went on the wire, never an internal call. The two
/// fixtures Appendix C names for this material live in <c>specifications/fixtures/auth/</c> and run
/// from <see cref="AuthFixturesTests"/>; these are the arms a fixture cannot reach.
/// </remarks>
public sealed class AuthM6UnitTests
{
    private const string Address = "https://vault.example.com:8200";
    private const string Prefix = "https://vault.example.com:8200/v1/";

    // ---- AUT-035: FIDO2 on both mounts. ----

    [Theory]
    [InlineData(false, "auth/userpass/fido2/login/begin")]
    [InlineData(true, "auth/fido2/login/begin")]
    [Requirement("AUT-035")]
    [Trait("Requirement", "AUT-035")]
    public async Task Fido2_begin_posts_the_username_and_returns_the_assertion_options_verbatim(bool standalone, string path)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"challenge":"Q0hBTA","allowCredentials":[{"id":"abc","type":"public-key"}],"userVerification":"preferred"}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        WebAuthnAssertionOptions assertion = standalone
            ? await client.Auth.Fido2.LoginBeginAsync("alice")
            : await client.Auth.Userpass.Fido2LoginBeginAsync("alice");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(Prefix + path, request.Uri.ToString());
        Assert.Equal("""{"username":"alice"}""", Body(request));
        // AUT-035: uninterpreted. Every member the server sent is still there, and the SDK models none.
        using JsonDocument parsed = JsonDocument.Parse(assertion.Json);
        Assert.Equal("Q0hBTA", parsed.RootElement.GetProperty("challenge").GetString());
        Assert.Equal("preferred", parsed.RootElement.GetProperty("userVerification").GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("allowCredentials").GetArrayLength());
        // CFG-020's first MUST: the begin step carries no token even though the client holds one.
        Assert.DoesNotContain("X-BastionVault-Token", request.Headers.Keys);
    }

    [Theory]
    [InlineData(false, "auth/userpass/fido2/login/complete")]
    [InlineData(true, "auth/fido2/login/complete")]
    [Requirement("AUT-035")]
    [Trait("Requirement", "AUT-035")]
    public async Task Fido2_complete_embeds_the_credential_unaltered_and_follows_the_login_contract(bool standalone, string path)
    {
        const string Credential = """{"id":"abc","response":{"signature":"c2ln","userHandle":null},"type":"public-key"}""";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"auth":{"client_token":"{{{FakeTokens.Child}}}","policies":["default"],"lease_duration":3600,"renewable":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        AuthInfo auth = standalone
            ? await client.Auth.Fido2.LoginCompleteAsync("alice", Credential)
            : await client.Auth.Userpass.Fido2LoginCompleteAsync("alice", Credential);

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(Prefix + path, request.Uri.ToString());
        Assert.Equal($$$"""{"username":"alice","credential":{{{Credential}}}}""", Body(request));
        // AUT-013 and DR-0006: the completion reuses the one login path, so the credential is recorded.
        Assert.Equal(FakeTokens.Child, auth.ClientToken.Reveal());
        Assert.Equal(FakeTokens.Child, client.Auth.CurrentToken!.Reveal());
        Assert.Equal(TimeSpan.FromHours(1), auth.LeaseDuration);
    }

    [Fact]
    [Requirement("AUT-035")]
    [Trait("Requirement", "AUT-035")]
    public async Task Fido2_complete_rejects_a_credential_that_is_not_JSON_before_any_network_call()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Fido2.LoginCompleteAsync("alice", "not json at all"));

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
        Assert.Equal("credentialJson", failure.Details["argument"]);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("AUT-035")]
    [Trait("Requirement", "AUT-035")]
    public async Task Fido2_begin_raises_BV_PROTOCOL_002_when_the_server_sends_no_data_object()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":null,"auth":null}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Fido2.LoginBeginAsync("alice"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal("data", failure.Details["field"]);
    }

    // ---- AUT-050, AUT-051, AUT-052, AUT-053: FerroGate. ----

    [Fact]
    [Requirement("AUT-050")]
    [Trait("Requirement", "AUT-050")]
    public async Task Ferrogate_login_sends_the_dpop_proof_in_both_the_header_and_the_body()
    {
        const string Proof = "eyJhbGciOiJFUzI1NiJ9.proof.signature";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"auth":{"client_token":"{{{FakeTokens.Child}}}","policies":[],"lease_duration":600,"renewable":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Auth.Ferrogate.LoginAsync(
            new SecretString(FakeTokens.Machine), Proof, new SecretString(FakeTokens.Explicit));

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(Prefix + "auth/ferrogate/login", request.Uri.ToString());
        Assert.Equal(Proof, request.Headers["DPoP"]);
        Assert.Equal(
            $$$"""{"token":"{{{FakeTokens.Machine}}}","dpop":"{{{Proof}}}","user_token":"{{{FakeTokens.Explicit}}}"}""",
            Body(request));
    }

    [Fact]
    [Requirement("AUT-050")]
    [Trait("Requirement", "AUT-050")]
    public async Task Ferrogate_login_without_a_proof_sends_no_DPoP_header_and_omits_the_optional_fields()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"auth":{"client_token":"{{{FakeTokens.Child}}}","policies":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Auth.Ferrogate.LoginAsync(new SecretString(FakeTokens.Machine));

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.DoesNotContain("DPoP", request.Headers.Keys);
        Assert.Equal($$$"""{"token":"{{{FakeTokens.Machine}}}"}""", Body(request));
    }

    [Fact]
    [Requirement("AUT-050")]
    [Trait("Requirement", "AUT-050")]
    public async Task Ferrogate_login_merges_the_DPoP_header_into_the_callers_own_headers()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"auth":{"client_token":"{{{FakeTokens.Child}}}","policies":[]}}"""));
        BastionVaultClient client = BuildClient(transport);
        RequestOptions options = new()
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Trace-Id"] = "abc123" },
        };

        _ = await client.Auth.Ferrogate.LoginAsync(new SecretString(FakeTokens.Machine), "proof", options: options);

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal("proof", request.Headers["DPoP"]);
        Assert.Equal("abc123", request.Headers["X-Trace-Id"]);
        // CFG-061: the caller's own options object is not mutated.
        _ = Assert.Single(options.Headers!);
    }

    [Fact]
    [Requirement("AUT-051")]
    [Trait("Requirement", "AUT-051")]
    public async Task Requirement_is_readable_at_a_non_default_mount_with_no_token_at_all()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"require_machine_identity":true,"expected_audience":"bv","trust_domain":"spiffe://x","mia_environment":"prod"}}"""));
        BastionVaultClient client = BuildClient(transport);

        FerrogateRequirement requirement = await client.Auth.Ferrogate.RequirementAsync("fg/eu");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(Prefix + "auth/fg/eu/requirement", request.Uri.ToString());
        Assert.True(requirement.RequireMachineIdentity);
        Assert.Equal("bv", requirement.ExpectedAudience);
        Assert.Equal("spiffe://x", requirement.TrustDomain);
        Assert.Equal("prod", requirement.MiaEnvironment);
    }

    [Fact]
    [Requirement("AUT-051")]
    [Trait("Requirement", "AUT-051")]
    public async Task Requirement_omits_absent_fields_and_reads_a_false_flag_as_false()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"require_machine_identity":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        FerrogateRequirement requirement = await client.Auth.Ferrogate.RequirementAsync();

        Assert.False(requirement.RequireMachineIdentity);
        Assert.Null(requirement.ExpectedAudience);
        Assert.Null(requirement.TrustDomain);
        Assert.Null(requirement.MiaEnvironment);
    }

    [Fact]
    [Requirement("AUT-051")]
    [Trait("Requirement", "AUT-051")]
    public async Task Requirement_raises_BV_PROTOCOL_002_when_the_envelope_carries_no_data()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":null,"auth":null}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Ferrogate.RequirementAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    [Fact]
    [Requirement("AUT-051")]
    [Trait("Requirement", "AUT-051")]
    public async Task IsMachineIdentityRequired_fetches_once_per_mount_and_answers_from_the_cache_thereafter()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"require_machine_identity":true}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"require_machine_identity":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.True(await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync());
        // Second read of `Client.Auth` builds a fresh view (CFG-071); the cache is on the client,
        // so this must not send a second request.
        Assert.True(await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync());
        _ = Assert.Single(transport.Requests);

        // A different mount is a different answer and a second fetch.
        Assert.False(await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync("fg2"));
        Assert.Equal(2, transport.Requests.Count);

        // RequirementAsync refreshes the cache as a side effect, which is the documented way back
        // to a live answer.
        transport.EnqueueResponse(200, body: Json("""{"data":{"require_machine_identity":false}}"""));
        _ = await client.Auth.Ferrogate.RequirementAsync();
        Assert.False(await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync());
        Assert.Equal(3, transport.Requests.Count);
    }

    [Theory]
    // The spellings a JSON encoder can plausibly produce for a *true* flag. All three read as
    // true, because the failure direction of a security gate is what matters (D-M6-17): read
    // strictly, a server spelling it `"true"` or `1` would be understood as not requiring a
    // machine identity, and the application would skip a FerroGate login it is subject to.
    [InlineData("""{"require_machine_identity":true}""", true)]
    [InlineData("""{"require_machine_identity":"true"}""", true)]
    [InlineData("""{"require_machine_identity":"TRUE"}""", true)]
    [InlineData("""{"require_machine_identity":1}""", true)]
    [InlineData("""{"require_machine_identity":-1}""", true)]
    // And the false ones, including absence: AUT-051's server omits a false flag.
    [InlineData("""{"require_machine_identity":false}""", false)]
    [InlineData("""{"require_machine_identity":"false"}""", false)]
    [InlineData("""{"require_machine_identity":0}""", false)]
    [InlineData("""{"expected_audience":"spiffe://corp"}""", false)]
    // It stops at the plausible spellings rather than treating everything non-false as true. A
    // word outside the set, a non-integral number, a null, an object or an array is a response
    // AUT-051 does not describe, and inventing a truth value for it would be the guess D-M1c-25
    // forbids rather than a safety measure.
    [InlineData("""{"require_machine_identity":"yes"}""", false)]
    [InlineData("""{"require_machine_identity":1.5}""", false)]
    [InlineData("""{"require_machine_identity":null}""", false)]
    [InlineData("""{"require_machine_identity":{}}""", false)]
    [InlineData("""{"require_machine_identity":[1]}""", false)]
    [Requirement("AUT-051")]
    [Trait("Requirement", "AUT-051")]
    public async Task Require_machine_identity_reads_every_plausible_spelling_of_a_true_flag(string data, bool expected)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$"""{"data":{{data}}}"""));
        BastionVaultClient client = BuildClient(transport);

        FerrogateRequirement requirement = await client.Auth.Ferrogate.RequirementAsync();

        Assert.Equal(expected, requirement.RequireMachineIdentity);
    }

    [Fact]
    [Requirement("AUT-051")]
    [Requirement("AUT-041")]
    [Trait("Requirement", "AUT-051")]
    public async Task IsMachineIdentityRequired_asks_each_namespace_for_itself_in_both_orderings()
    {
        // B1 of the R3 handback, and the fail-open ordering is the second half. `ClientContext` is
        // shared by every `WithNamespace` view of one client by design, so a cache keyed by mount
        // alone answers tenant-b out of tenant-a's entry — permanently, because AUT-051's cache has
        // no TTL and D-M6-14's refusal to invent one stands. AUT-041 makes the namespace a
        // request-scoping dimension on auth paths, so two namespaces are two questions (D-M6-16).
        //
        // Safe ordering first: the strict tenant is asked first, so a blind cache would merely
        // over-report. Then the dangerous one: the permissive tenant is asked first, and a blind
        // cache tells the strict tenant it need not present a machine identity.
        foreach ((string first, bool firstAnswer, string second, bool secondAnswer) in new[]
        {
            ("tenant-a", true, "tenant-b", false),
            ("tenant-a", false, "tenant-b", true),
        })
        {
            FakeTransport transport = new();
            transport.EnqueueResponse(200, body: Json(RequirementBody(firstAnswer)));
            transport.EnqueueResponse(200, body: Json(RequirementBody(secondAnswer)));
            BastionVaultClient client = BuildClient(transport);

            Assert.Equal(firstAnswer, await client.WithNamespace(first).Auth.Ferrogate.IsMachineIdentityRequiredAsync());
            Assert.Equal(secondAnswer, await client.WithNamespace(second).Auth.Ferrogate.IsMachineIdentityRequiredAsync());

            // Each namespace was actually consulted, and each consultation carried its own header.
            Assert.Equal(2, transport.Requests.Count);
            Assert.Equal([first, second], transport.Requests.Select(request => request.Headers["X-BastionVault-Namespace"]));

            // And the cache still caches: neither namespace asks twice.
            Assert.Equal(firstAnswer, await client.WithNamespace(first).Auth.Ferrogate.IsMachineIdentityRequiredAsync());
            Assert.Equal(secondAnswer, await client.WithNamespace(second).Auth.Ferrogate.IsMachineIdentityRequiredAsync());
            Assert.Equal(2, transport.Requests.Count);
        }

        static string RequirementBody(bool required)
        {
            return "{\"data\":{\"require_machine_identity\":" + (required ? "true" : "false") + "}}";
        }
    }

    [Fact]
    [Requirement("AUT-051")]
    [Requirement("CFG-060")]
    [Trait("Requirement", "AUT-051")]
    public async Task IsMachineIdentityRequired_keys_on_the_per_call_namespace_override_too()
    {
        // `WithNamespace` and `RequestOptions.Namespace` are two independent mechanisms reaching
        // the same wire header, so a cache key derived from only the first is still blind. The
        // effective namespace is what the request is scoped by, and it is what the key uses.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"require_machine_identity":false}}"""));
        transport.EnqueueResponse(200, body: Json("""{"data":{"require_machine_identity":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.False(await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync(
            options: new RequestOptions { Namespace = "tenant-a" }));
        Assert.True(await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync(
            options: new RequestOptions { Namespace = "tenant-b" }));
        Assert.Equal(2, transport.Requests.Count);

        // A trailing slash is the same namespace on the wire, so it must not be a second key and a
        // third fetch: the key trims exactly as `RequestExecutor.EffectiveNamespace` trims.
        Assert.True(await client.Auth.Ferrogate.IsMachineIdentityRequiredAsync(
            options: new RequestOptions { Namespace = "tenant-b/" }));
        Assert.Equal(2, transport.Requests.Count);

        // The override and the view agree with each other: a `WithNamespace("tenant-b")` view
        // reads the entry the per-call override wrote, because both name one namespace.
        Assert.True(await client.WithNamespace("tenant-b").Auth.Ferrogate.IsMachineIdentityRequiredAsync());
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData("pending", MachineIdentityStatus.Pending)]
    [InlineData("approved", MachineIdentityStatus.Approved)]
    [InlineData("rejected", MachineIdentityStatus.Rejected)]
    [InlineData("revoked", MachineIdentityStatus.Revoked)]
    [InlineData("unknown", MachineIdentityStatus.Unknown)]
    [InlineData("APPROVED", MachineIdentityStatus.Approved)]
    [InlineData("something-the-server-invented", MachineIdentityStatus.Unknown)]
    [Requirement("AUT-052")]
    [Trait("Requirement", "AUT-052")]
    public async Task Status_maps_every_member_of_the_set_and_degrades_anything_else_to_Unknown(string wire, MachineIdentityStatus expected)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"data":{"status":"{{{wire}}}","machine_id":"m-1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        MachineStatus status = await client.Auth.Ferrogate.StatusAsync(new SecretString(FakeTokens.Machine), "proof");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(Prefix + "auth/ferrogate/status", request.Uri.ToString());
        Assert.Equal("proof", request.Headers["DPoP"]);
        Assert.Equal(expected, status.Status);
        // The unmodelled fields are still reachable, so nothing the server sent is lost.
        Assert.Equal("m-1", status.Raw["machine_id"].GetString());
    }

    [Fact]
    [Requirement("AUT-052")]
    [Trait("Requirement", "AUT-052")]
    public async Task Status_with_no_data_at_all_reads_as_Unknown_with_an_empty_raw_map()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":null,"auth":null}"""));
        BastionVaultClient client = BuildClient(transport);

        MachineStatus status = await client.Auth.Ferrogate.StatusAsync(new SecretString(FakeTokens.Machine));

        Assert.Equal(MachineIdentityStatus.Unknown, status.Status);
        Assert.Empty(status.Raw);
    }

    [Fact]
    [Requirement("AUT-052")]
    [Requirement("AUT-053")]
    [Trait("Requirement", "AUT-052")]
    public async Task Enroll_posts_the_spiffe_id_and_returns_a_status_that_can_hold_no_token()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"status":"pending","machine_id":"m-9"}}"""));
        BastionVaultClient client = BuildClient(transport);

        EnrollResult result = await client.Auth.Ferrogate.EnrollAsync("spiffe://example.internal/web-1", "build agent");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(Prefix + "auth/ferrogate/enroll", request.Uri.ToString());
        Assert.Equal("""{"spiffe_id":"spiffe://example.internal/web-1","comment":"build agent"}""", Body(request));
        Assert.Equal(MachineIdentityStatus.Pending, result.Status);
        Assert.Equal("m-9", result.Raw["machine_id"].GetString());
        // AUT-052: enrolment never yields a credential, and the client still holds none.
        Assert.Null(client.Auth.CurrentToken);
        Assert.DoesNotContain(
            "token",
            typeof(EnrollResult).GetProperties().Select(property => property.Name.ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Requirement("AUT-052")]
    [Trait("Requirement", "AUT-052")]
    public async Task Enroll_omits_an_absent_comment_rather_than_sending_an_empty_one()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"status":"pending"}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Auth.Ferrogate.EnrollAsync("spiffe://example.internal/web-1");

        Assert.Equal("""{"spiffe_id":"spiffe://example.internal/web-1"}""", Body(transport.Requests[0]));
    }

    // ---- AUT-060: OIDC and SAML. ----

    [Fact]
    [Requirement("AUT-060")]
    [Trait("Requirement", "AUT-060")]
    public async Task Oidc_auth_url_posts_the_redirect_and_role_and_returns_the_providers_url()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"auth_url":"https://idp.example.com/authorize?state=s1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        string url = await client.Auth.Oidc.AuthUrlAsync("http://127.0.0.1:54321/callback", "engineers");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(Prefix + "auth/oidc/auth_url", request.Uri.ToString());
        Assert.Equal("""{"redirect_uri":"http://127.0.0.1:54321/callback","role":"engineers"}""", Body(request));
        Assert.Equal("https://idp.example.com/authorize?state=s1", url);
    }

    [Fact]
    [Requirement("AUT-060")]
    [Trait("Requirement", "AUT-060")]
    public async Task Oidc_auth_url_raises_BV_PROTOCOL_002_when_the_server_sends_no_auth_url()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"unexpected":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Oidc.AuthUrlAsync("http://127.0.0.1:1/callback"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal("data.auth_url", failure.Details["field"]);
    }

    [Fact]
    [Requirement("AUT-060")]
    [Trait("Requirement", "AUT-060")]
    public async Task Oidc_callback_is_the_login_and_installs_the_issued_token()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"auth":{"client_token":"{{{FakeTokens.Child}}}","policies":["oidc"],"renewable":true}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        AuthInfo auth = await client.Auth.Oidc.CallbackAsync("s1", "c1");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(Prefix + "auth/oidc/callback", request.Uri.ToString());
        Assert.Equal("""{"state":"s1","code":"c1"}""", Body(request));
        // TRN-015: a login carries no token header, even from an already-authenticated client.
        Assert.DoesNotContain("X-BastionVault-Token", request.Headers.Keys);
        Assert.Equal(["oidc"], auth.Policies);
        Assert.Equal(FakeTokens.Child, client.Auth.CurrentToken!.Reveal());
    }

    [Fact]
    [Requirement("AUT-060")]
    [Trait("Requirement", "AUT-060")]
    public async Task Saml_login_returns_the_sso_url_relay_state_and_request_id()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"sso_url":"https://idp.example.com/sso","relay_state":"r1","request_id":"q1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        SamlLoginRequest login = await client.Auth.Saml.LoginAsync("http://127.0.0.1:1/acs", "staff");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(Prefix + "auth/saml/login", request.Uri.ToString());
        Assert.Equal("""{"redirect_uri":"http://127.0.0.1:1/acs","role":"staff"}""", Body(request));
        Assert.Equal("https://idp.example.com/sso", login.SsoUrl);
        Assert.Equal("r1", login.RelayState);
        Assert.Equal("q1", login.RequestId);
    }

    [Fact]
    [Requirement("AUT-060")]
    [Trait("Requirement", "AUT-060")]
    public async Task Saml_login_omits_both_optional_arguments_and_tolerates_a_bare_sso_url()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"sso_url":"https://idp.example.com/sso"}}"""));
        BastionVaultClient client = BuildClient(transport);

        SamlLoginRequest login = await client.Auth.Saml.LoginAsync();

        Assert.Equal("{}", Body(transport.Requests[0]));
        Assert.Null(login.RelayState);
        Assert.Null(login.RequestId);
    }

    [Fact]
    [Requirement("AUT-060")]
    [Trait("Requirement", "AUT-060")]
    public async Task Saml_login_raises_BV_PROTOCOL_002_when_the_server_sends_no_sso_url()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"relay_state":"r1"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Saml.LoginAsync());

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal("data.sso_url", failure.Details["field"]);
    }

    [Fact]
    [Requirement("AUT-060")]
    [Trait("Requirement", "AUT-060")]
    public async Task Saml_callback_is_the_login_and_posts_the_assertion()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"auth":{"client_token":"{{{FakeTokens.Child}}}","policies":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Auth.Saml.CallbackAsync("PHNhbWw+", "r1");

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(Prefix + "auth/saml/callback", request.Uri.ToString());
        Assert.Equal("""{"saml_response":"PHNhbWw+","relay_state":"r1"}""", Body(request));
    }

    [Fact]
    [Requirement("AUT-060")]
    [Requirement("AUT-052")]
    [Trait("Requirement", "AUT-060")]
    public async Task A_204_with_no_body_at_all_is_absence_not_a_silently_empty_answer()
    {
        // TRN-050 gives these operations a null `Response`, which is a different shape from "a
        // response whose data is empty". The typed reads must each say so in their own terms: a
        // required field raises BV-PROTOCOL-002, an optional one stays absent.
        FakeTransport transport = new();
        transport.EnqueueResponse(204, body: ReadOnlyMemory<byte>.Empty);
        transport.EnqueueResponse(204, body: ReadOnlyMemory<byte>.Empty);
        transport.EnqueueResponse(204, body: ReadOnlyMemory<byte>.Empty);
        transport.EnqueueResponse(204, body: ReadOnlyMemory<byte>.Empty);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Saml.LoginAsync());
        BastionVaultException assertion = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Fido2.LoginBeginAsync("alice"));
        BastionVaultException requirement = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Ferrogate.RequirementAsync());
        MachineStatus status = await client.Auth.Ferrogate.StatusAsync(new SecretString(FakeTokens.Machine));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, assertion.Code);
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, requirement.Code);
        Assert.Equal(MachineIdentityStatus.Unknown, status.Status);
        Assert.Empty(status.Raw);
    }

    // ---- AUT-070: the disabled certificate backend. ----

    [Theory]
    [InlineData(404, "Router mount not found.")]
    [InlineData(500, "Logical backend path not supported.")]
    [Requirement("AUT-070")]
    [Trait("Requirement", "AUT-070")]
    public async Task Cert_login_maps_both_disabled_backend_answers_to_BV_SERVER_004_naming_the_backend(int status, string message)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(status, body: Json($$$"""{"error":"{{{message}}}"}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Cert.LoginAsync());

        Assert.Equal(ErrorCodes.ServerUnsupportedByServer, failure.Code);
        Assert.Equal(status, failure.StatusCode);
        Assert.False(failure.Retryable);
        Assert.Equal("cert", failure.Details["backend"]);
        Assert.Equal("cert", failure.Details["mount"]);
        Assert.Contains("`cert` auth backend is disabled", failure.Hint, StringComparison.Ordinal);
        Assert.Contains("CFG-044", failure.Hint, StringComparison.Ordinal);
        Assert.Equal(message, failure.ServerMessage);
        Assert.Equal(1, failure.Attempts);
        // The body is empty: CFG-044 presents the certificate at the TLS layer, not in the payload.
        Assert.Equal("{}", Body(transport.Requests[0]));
        Assert.Equal(Prefix + "auth/cert/login", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("AUT-070")]
    [Trait("Requirement", "AUT-070")]
    public async Task The_hint_names_the_disabled_backend_not_the_mount_it_was_reached_at()
    {
        // AUT-070 requires "a hint naming the disabled backend", and the disabled backend is
        // `cert` whichever mount it was mounted at. `logical backend path not supported` is the
        // backend answering for itself, so the override still applies at a non-default mount —
        // what changed (D-M6-18) is that the hint no longer interpolates the caller's mount as if
        // that were the backend's name. The mount asked for is kept in the hint and in
        // `Details.mount`, so an operator still sees which call produced this.
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"error":"Logical backend path not supported."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Cert.LoginAsync("mtls"));

        Assert.Equal(ErrorCodes.ServerUnsupportedByServer, failure.Code);
        Assert.Equal("cert", failure.Details["backend"]);
        Assert.Equal("mtls", failure.Details["mount"]);
        Assert.Contains("`cert` auth backend is disabled", failure.Hint, StringComparison.Ordinal);
        Assert.Contains("mount asked: `mtls`", failure.Hint, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("AUT-070")]
    [Trait("Requirement", "AUT-070")]
    public async Task A_mistyped_mount_stays_a_not_found_instead_of_becoming_a_disabled_backend()
    {
        // R2 of the R3 handback (D-M6-18). `router mount not found` is ambiguous, and at a mount
        // AUT-070 does not name the likelier reading is a typo — on a server where `cert` is in
        // fact enabled, the old override told the operator the `typo` backend was disabled, which
        // is false and is exactly the mis-mapping D-M6-8 rejected widening the catalogue to avoid.
        FakeTransport transport = new();
        transport.EnqueueResponse(404, body: Json("""{"error":"Router mount not found."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Cert.LoginAsync("typo"));

        Assert.NotEqual(ErrorCodes.ServerUnsupportedByServer, failure.Code);
        Assert.Equal(ErrorCodes.NotFoundMountNotFound, failure.Code);
        Assert.DoesNotContain("backend", failure.Details.Keys, StringComparer.Ordinal);
    }

    [Fact]
    [Requirement("AUT-070")]
    [Trait("Requirement", "AUT-070")]
    public async Task Cert_login_does_not_rewrite_an_unrelated_failure_into_UnsupportedByServer()
    {
        // A server that *does* have the backend enabled and refuses the certificate must surface
        // its own code, not AUT-070's override.
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.Cert.LoginAsync());

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, failure.Code);
    }

    [Fact]
    [Requirement("AUT-070")]
    [Trait("Requirement", "AUT-070")]
    public async Task Cert_login_succeeds_normally_against_a_server_that_has_the_backend_enabled()
    {
        // AUT-070 requires the operation to exist, not merely to fail well: the backend is disabled
        // "in current server builds", so the success arm is the one a later build exercises and the
        // one that must not have been written as a permanent error path.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$$"""{"auth":{"client_token":"{{{FakeTokens.Child}}}","policies":["mtls"],"renewable":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        AuthInfo auth = await client.Auth.Cert.LoginAsync();

        Assert.Equal(FakeTokens.Child, auth.ClientToken.Reveal());
        Assert.Equal(["mtls"], auth.Policies);
        Assert.Equal(FakeTokens.Child, client.Auth.CurrentToken!.Reveal());
    }

    // CFG-044 needs no test here. It is already verified end to end, against a real mTLS
    // handshake that *refuses* a client presenting no certificate, by
    // `CoverageGapTests.Client_certificate_is_presented_and_required_by_an_mTLS_server`. M6's brief
    // is to verify that requirement, not to rewrite it, and a second weaker assertion here — that
    // two strings survive configuration resolution — would dilute the traceability marker rather
    // than strengthen it.

    // ---- AUT-043 and AUT-054: the administration path tables. ----

    [Theory]
    [MemberData(nameof(AdminOperations))]
    [Requirement("AUT-043")]
    [Requirement("AUT-054")]
    [Trait("Requirement", "AUT-043")]
    public async Task Every_administration_operation_sends_the_verb_and_path_Appendix_A_lists(
        string _, string method, string path, Func<BastionVaultClient, Task> call)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["one","two"],"ok":true}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        await call(client);

        TransportRequest request = Assert.Single(transport.Requests);
        Assert.Equal(method, request.Method);
        Assert.Equal(Prefix + path, request.Uri.ToString());
        // Every one of these is authenticated (Appendix A), unlike the AUT-051/AUT-052 endpoints.
        Assert.Equal(FakeTokens.Client, request.Headers["X-BastionVault-Token"]);
    }

    public static TheoryData<string, string, Func<BastionVaultClient, Task>> AdminOperationsRaw()
    {
        return [];
    }

    /// <summary>
    /// Appendix A's AppID and FerroGate administration rows, one case each. The role name carries a
    /// <c>/</c> in a handful of them so TRN-020's single-segment encoding is asserted on the
    /// caller-supplied parts rather than only on the mount.
    /// </summary>
    public static TheoryData<string, string, string, Func<BastionVaultClient, Task>> AdminOperations()
    {
        JsonElement document = JsonDocument.Parse("""{"a":1}""").RootElement;
        TheoryData<string, string, string, Func<BastionVaultClient, Task>> data = [];

        void Add(string name, string method, string path, Func<BastionVaultClient, Task> call)
        {
            data.Add(name, method, path, call);
        }

        // AUT-043 — AppID.
        Add("AppId.ListRoles", "LIST", "auth/approle/role", c => c.Auth.AppId.Admin.ListRolesAsync());
        Add("AppId.ReadRole", "GET", "auth/approle/role/web", c => c.Auth.AppId.Admin.ReadRoleAsync("web"));
        Add("AppId.WriteRole", "POST", "auth/approle/role/web", c => c.Auth.AppId.Admin.WriteRoleAsync("web", document));
        Add("AppId.DeleteRole", "DELETE", "auth/approle/role/web", c => c.Auth.AppId.Admin.DeleteRoleAsync("web"));
        Add("AppId.WriteRoleId", "POST", "auth/approle/role/web/role-id", c => c.Auth.AppId.Admin.WriteRoleIdAsync("web", "r-1"));
        Add("AppId.ReadField", "GET", "auth/approle/role/web/policies", c => c.Auth.AppId.Admin.ReadFieldAsync("web", AppIdRoleField.Policies));
        Add("AppId.WriteField", "POST", "auth/approle/role/web/token-ttl", c => c.Auth.AppId.Admin.WriteFieldAsync("web", AppIdRoleField.TokenTtl, document));
        Add("AppId.DeleteField", "DELETE", "auth/approle/role/web/period", c => c.Auth.AppId.Admin.DeleteFieldAsync("web", AppIdRoleField.Period));
        Add("AppId.ReadLocalSecretIds", "GET", "auth/approle/role/web/local-secret-ids", c => c.Auth.AppId.Admin.ReadLocalSecretIdsAsync("web"));
        Add("AppId.ListSecretIdAccessors", "LIST", "auth/approle/role/web/secret-id/", c => c.Auth.AppId.Admin.ListSecretIdAccessorsAsync("web"));
        Add("AppId.LookupSecretId", "POST", "auth/approle/role/web/secret-id/lookup", c => c.Auth.AppId.Admin.LookupSecretIdAsync("web", new SecretString(FakeTokens.Explicit)));
        Add("AppId.DestroySecretId", "POST", "auth/approle/role/web/secret-id/destroy", c => c.Auth.AppId.Admin.DestroySecretIdAsync("web", new SecretString(FakeTokens.Explicit)));
        Add("AppId.LookupSecretIdAccessor", "POST", "auth/approle/role/web/secret-id-accessor/lookup", c => c.Auth.AppId.Admin.LookupSecretIdAccessorAsync("web", "acc-1"));
        Add("AppId.DestroySecretIdAccessor", "POST", "auth/approle/role/web/secret-id-accessor/destroy", c => c.Auth.AppId.Admin.DestroySecretIdAccessorAsync("web", "acc-1"));
        Add("AppId.CustomSecretId", "POST", "auth/approle/role/web/custom-secret-id", c => c.Auth.AppId.Admin.CustomSecretIdAsync("web", new SecretString(FakeTokens.Explicit)));
        Add("AppId.ListMachines", "LIST", "auth/approle/role/web/machine/", c => c.Auth.AppId.Admin.ListMachinesAsync("web"));
        Add("AppId.BindMachine", "POST", "auth/approle/role/web/machine/", c => c.Auth.AppId.Admin.BindMachineAsync("web", document));
        Add("AppId.ReadMachine", "GET", "auth/approle/role/web/machine/m%2F1", c => c.Auth.AppId.Admin.ReadMachineAsync("web", "m/1"));
        Add("AppId.UnbindMachine", "DELETE", "auth/approle/role/web/machine/m-1", c => c.Auth.AppId.Admin.UnbindMachineAsync("web", "m-1"));
        Add("AppId.ReadConfig", "GET", "auth/approle/config", c => c.Auth.AppId.Admin.ReadConfigAsync());
        Add("AppId.WriteConfig", "POST", "auth/approle/config", c => c.Auth.AppId.Admin.WriteConfigAsync(document));
        Add("AppId.TidySecretIds", "POST", "auth/approle/tidy/secret-id", c => c.Auth.AppId.Admin.TidySecretIdsAsync());
        Add("AppId.RoleNameIsOneSegment", "GET", "auth/approle/role/team%2Fweb", c => c.Auth.AppId.Admin.ReadRoleAsync("team/web"));

        // AUT-054 — FerroGate.
        Add("Ferrogate.ReadConfig", "GET", "auth/ferrogate/config", c => c.Auth.Ferrogate.Admin.ReadConfigAsync());
        Add("Ferrogate.WriteConfig", "POST", "auth/ferrogate/config", c => c.Auth.Ferrogate.Admin.WriteConfigAsync(document));
        Add("Ferrogate.Register", "POST", "auth/ferrogate/register", c => c.Auth.Ferrogate.Admin.RegisterAsync(document));
        Add("Ferrogate.ListMachines", "LIST", "auth/ferrogate/machines/", c => c.Auth.Ferrogate.Admin.ListMachinesAsync());
        Add("Ferrogate.ReadMachine", "GET", "auth/ferrogate/machines/m-1", c => c.Auth.Ferrogate.Admin.ReadMachineAsync("m-1"));
        Add("Ferrogate.DeleteMachine", "DELETE", "auth/ferrogate/machines/m-1", c => c.Auth.Ferrogate.Admin.DeleteMachineAsync("m-1"));
        Add("Ferrogate.Approve", "POST", "auth/ferrogate/machines/m-1/approve", c => c.Auth.Ferrogate.Admin.ApproveAsync("m-1"));
        Add("Ferrogate.Reject", "POST", "auth/ferrogate/machines/m-1/reject", c => c.Auth.Ferrogate.Admin.RejectAsync("m-1"));
        Add("Ferrogate.Revoke", "POST", "auth/ferrogate/machines/m-1/revoke", c => c.Auth.Ferrogate.Admin.RevokeAsync("m-1"));

        // Appendix A's `Auth.Userpass.Admin.*` — catalogue-only, no AUT-0nn requirement names this
        // row set (unlike AppID/FerroGate above), so no method-level [Requirement] tag governs it.
        Add("Userpass.ListUsers", "LIST", "auth/userpass/users/", c => c.Auth.Userpass.Admin.ListUsersAsync());
        Add("Userpass.ReadUser", "GET", "auth/userpass/users/alice", c => c.Auth.Userpass.Admin.ReadUserAsync("alice"));
        Add("Userpass.WriteUser", "POST", "auth/userpass/users/alice", c => c.Auth.Userpass.Admin.WriteUserAsync("alice", document));
        Add("Userpass.DeleteUser", "DELETE", "auth/userpass/users/alice", c => c.Auth.Userpass.Admin.DeleteUserAsync("alice"));
        Add("Userpass.SetPassword", "POST", "auth/userpass/users/alice/password", c => c.Auth.Userpass.Admin.SetPasswordAsync("alice", new SecretString(FakeTokens.Explicit)));
        Add("Userpass.Unlock", "POST", "auth/userpass/users/alice/unlock", c => c.Auth.Userpass.Admin.UnlockAsync("alice"));
        Add("Userpass.ReadFido2", "GET", "auth/userpass/users/alice/fido2", c => c.Auth.Userpass.Admin.ReadFido2Async("alice"));
        Add("Userpass.DeleteFido2", "DELETE", "auth/userpass/users/alice/fido2", c => c.Auth.Userpass.Admin.DeleteFido2Async("alice"));
        Add("Userpass.ReadLockout", "GET", "auth/userpass/config/lockout", c => c.Auth.Userpass.Admin.ReadLockoutAsync());
        Add("Userpass.WriteLockout", "POST", "auth/userpass/config/lockout", c => c.Auth.Userpass.Admin.WriteLockoutAsync(document));
        Add("Userpass.ReadMfa", "GET", "auth/userpass/config/mfa", c => c.Auth.Userpass.Admin.ReadMfaAsync());
        Add("Userpass.WriteMfa", "POST", "auth/userpass/config/mfa", c => c.Auth.Userpass.Admin.WriteMfaAsync(document));
        Add("Userpass.UsernameIsOneSegment", "GET", "auth/userpass/users/team%2Falice", c => c.Auth.Userpass.Admin.ReadUserAsync("team/alice"));

        // AUT-060 — the shared OIDC/SAML role admin, once per mount so D-M6-6's default is asserted.
        Add("Oidc.ReadConfig", "GET", "auth/oidc/config", c => c.Auth.Oidc.Admin.ReadConfigAsync());
        Add("Oidc.WriteConfig", "POST", "auth/oidc/config", c => c.Auth.Oidc.Admin.WriteConfigAsync(document));
        Add("Oidc.ListRoles", "LIST", "auth/oidc/role", c => c.Auth.Oidc.Admin.ListRolesAsync());
        Add("Oidc.ReadRole", "GET", "auth/oidc/role/eng", c => c.Auth.Oidc.Admin.ReadRoleAsync("eng"));
        Add("Oidc.WriteRole", "POST", "auth/oidc/role/eng", c => c.Auth.Oidc.Admin.WriteRoleAsync("eng", document));
        Add("Oidc.DeleteRole", "DELETE", "auth/oidc/role/eng", c => c.Auth.Oidc.Admin.DeleteRoleAsync("eng"));
        Add("Saml.ReadConfig", "GET", "auth/saml/config", c => c.Auth.Saml.Admin.ReadConfigAsync());
        Add("Saml.WriteConfig", "POST", "auth/saml/config", c => c.Auth.Saml.Admin.WriteConfigAsync(document));
        Add("Saml.ListRoles", "LIST", "auth/saml/role", c => c.Auth.Saml.Admin.ListRolesAsync());
        Add("Saml.ReadRole", "GET", "auth/saml/role/eng", c => c.Auth.Saml.Admin.ReadRoleAsync("eng"));
        Add("Saml.WriteRole", "POST", "auth/saml/role/eng", c => c.Auth.Saml.Admin.WriteRoleAsync("eng", document));
        Add("Saml.DeleteRole", "DELETE", "auth/saml/role/eng", c => c.Auth.Saml.Admin.DeleteRoleAsync("eng"));
        // D-M6-6: the same object, pointed at a mount the caller names, so a non-default OIDC mount
        // does not need a second type.
        Add("Saml.NonDefaultMount", "GET", "auth/saml-eu/config", c => c.Auth.Saml.Admin.ReadConfigAsync("saml-eu"));

        return data;
    }

    [Theory]
    [InlineData(AppIdRoleField.Policies, "policies")]
    [InlineData(AppIdRoleField.BoundCidrList, "bound-cidr-list")]
    [InlineData(AppIdRoleField.SecretIdBoundCidrs, "secret-id-bound-cidrs")]
    [InlineData(AppIdRoleField.BoundSourceIps, "bound-source-ips")]
    [InlineData(AppIdRoleField.BypassMachineBinding, "bypass-machine-binding")]
    [InlineData(AppIdRoleField.TokenBoundCidrs, "token-bound-cidrs")]
    [InlineData(AppIdRoleField.BindSecretId, "bind-secret-id")]
    [InlineData(AppIdRoleField.SecretIdNumUses, "secret-id-num-uses")]
    [InlineData(AppIdRoleField.SecretIdTtl, "secret-id-ttl")]
    [InlineData(AppIdRoleField.Period, "period")]
    [InlineData(AppIdRoleField.TokenNumUses, "token-num-uses")]
    [InlineData(AppIdRoleField.TokenTtl, "token-ttl")]
    [InlineData(AppIdRoleField.TokenMaxTtl, "token-max-ttl")]
    [Requirement("AUT-043")]
    [Trait("Requirement", "AUT-043")]
    public async Task Every_AppIdRoleField_maps_to_the_path_segment_Appendix_A_enumerates(AppIdRoleField field, string segment)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ok":true}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        _ = await client.Auth.AppId.Admin.ReadFieldAsync("web", field);

        Assert.Equal(Prefix + "auth/approle/role/web/" + segment, transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("AUT-043")]
    [Trait("Requirement", "AUT-043")]
    public async Task A_field_value_outside_the_enum_is_refused_client_side_rather_than_sent_as_a_path()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        BastionVaultException above = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.AppId.Admin.ReadFieldAsync("web", (AppIdRoleField)99));
        BastionVaultException below = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Auth.AppId.Admin.WriteFieldAsync("web", (AppIdRoleField)(-1), default));

        Assert.Equal(ErrorCodes.InputInvalidArgument, above.Code);
        Assert.Equal(ErrorCodes.InputInvalidArgument, below.Code);
        Assert.Equal("field", above.Details["argument"]);
        Assert.Equal("field", below.Details["argument"]);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("AUT-043")]
    [Trait("Requirement", "AUT-043")]
    public async Task A_secret_id_travels_in_the_body_and_a_list_with_no_keys_reads_as_empty()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"secret_id_num_uses":3}}"""));
        transport.EnqueueResponse(404, body: ReadOnlyMemory<byte>.Empty);
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        Response? lookup = await client.Auth.AppId.Admin.LookupSecretIdAsync("web", new SecretString(FakeTokens.Explicit));
        IReadOnlyList<string> accessors = await client.Auth.AppId.Admin.ListSecretIdAccessorsAsync("web");

        Assert.Equal($$$"""{"secret_id":"{{{FakeTokens.Explicit}}}"}""", Body(transport.Requests[0]));
        Assert.Equal(3, lookup!.Data!["secret_id_num_uses"].GetInt32());
        // TRN-050: a 404 with an empty body is absence, and absence is an empty list.
        Assert.Empty(accessors);
    }

    [Fact]
    [Requirement("AUT-043")]
    [Trait("Requirement", "AUT-043")]
    public async Task An_administration_write_serialises_the_callers_document_verbatim_and_a_tidy_sends_no_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ok":true}}"""));
        transport.EnqueueResponse(204, body: ReadOnlyMemory<byte>.Empty);
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);
        JsonElement role = JsonDocument.Parse("""{"policies":["web"],"token_ttl":3600}""").RootElement;

        _ = await client.Auth.AppId.Admin.WriteRoleAsync("web", role);
        _ = await client.Auth.AppId.Admin.TidySecretIdsAsync();

        Assert.Equal("""{"policies":["web"],"token_ttl":3600}""", Body(transport.Requests[0]));
        Assert.True(transport.Requests[1].Body.IsEmpty);
    }

    [Fact]
    public async Task Userpass_admin_set_password_sends_the_revealed_secret_as_the_password_field()
    {
        // Appendix A gives `Auth.Userpass.Admin.SetPassword` no schema; the wire field is `password`,
        // the same name `Auth.Userpass.Login`'s own body already uses for the same account.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ok":true}}"""));
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        _ = await client.Auth.Userpass.Admin.SetPasswordAsync("alice", new SecretString("hunter2"));

        Assert.Equal("""{"password":"hunter2"}""", Body(transport.Requests[0]));
    }

    [Fact]
    public async Task Userpass_admin_set_password_rejects_a_null_secret_before_any_network_call()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.Auth.Userpass.Admin.SetPasswordAsync("alice", null!));

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("AUT-054")]
    [Trait("Requirement", "AUT-054")]
    public async Task A_machines_list_returns_the_keys_and_an_approval_sends_an_empty_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["m-1","m-2"]}}"""));
        transport.EnqueueResponse(204, body: ReadOnlyMemory<byte>.Empty);
        BastionVaultClient client = BuildClient(transport, options => options.Token = FakeTokens.Client);

        IReadOnlyList<string> machines = await client.Auth.Ferrogate.Admin.ListMachinesAsync();
        _ = await client.Auth.Ferrogate.Admin.ApproveAsync("m-1");

        Assert.Equal(["m-1", "m-2"], machines);
        Assert.True(transport.Requests[1].Body.IsEmpty);
    }

    [Fact]
    [Requirement("AUT-043")]
    [Trait("Requirement", "AUT-043")]
    public void An_empty_or_whitespace_mount_or_segment_is_rejected_by_the_argument_guard()
    {
        BastionVaultClient client = BuildClient(new FakeTransport(), options => options.Token = FakeTokens.Client);

        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.AppId.Admin.ReadRoleAsync("web", "  "));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.AppId.Admin.ReadRoleAsync(" "));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Ferrogate.Admin.ReadMachineAsync(""));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Fido2.LoginBeginAsync(" "));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Oidc.AuthUrlAsync(" "));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Oidc.CallbackAsync("s", " "));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Saml.CallbackAsync(" ", "r"));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Ferrogate.EnrollAsync(" "));
        _ = Assert.ThrowsAsync<ArgumentNullException>(() => client.Auth.Ferrogate.LoginAsync(null!));
        _ = Assert.ThrowsAsync<ArgumentNullException>(() => client.Auth.Ferrogate.StatusAsync(null!));
        _ = Assert.ThrowsAsync<ArgumentNullException>(() => client.Auth.AppId.Admin.LookupSecretIdAsync("web", null!));
        _ = Assert.ThrowsAsync<ArgumentNullException>(() => client.Auth.AppId.Admin.DestroySecretIdAsync("web", null!));
        _ = Assert.ThrowsAsync<ArgumentNullException>(() => client.Auth.AppId.Admin.CustomSecretIdAsync("web", null!));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.AppId.Admin.DestroySecretIdAccessorAsync("web", " "));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.AppId.Admin.WriteRoleIdAsync("web", " "));
        _ = Assert.ThrowsAsync<ArgumentException>(() => client.Auth.Userpass.Admin.ReadUserAsync(" "));
        _ = Assert.ThrowsAsync<ArgumentNullException>(() => client.Auth.Userpass.Admin.SetPasswordAsync("alice", null!));
    }

    [Fact]
    [Requirement("AUT-035")]
    [Requirement("AUT-051")]
    [Trait("Requirement", "AUT-051")]
    public void Every_new_operation_group_is_reachable_from_a_namespaced_view_of_the_same_client()
    {
        // CFG-071: `Auth` is a view, so a `WithNamespace` child must expose the same groups over
        // the same token cell rather than a second, disconnected set.
        BastionVaultClient client = BuildClient(new FakeTransport(), options => options.Token = FakeTokens.Client);
        BastionVaultClient scoped = client.WithNamespace("team-a");

        Assert.NotNull(scoped.Auth.Fido2);
        Assert.NotNull(scoped.Auth.Ferrogate.Admin);
        Assert.NotNull(scoped.Auth.Oidc.Admin);
        Assert.NotNull(scoped.Auth.Saml.Admin);
        Assert.NotNull(scoped.Auth.Cert);
        Assert.NotNull(scoped.Auth.AppId.Admin);
        Assert.NotNull(scoped.Auth.Userpass.Admin);
        Assert.Equal(client.Auth.CurrentToken!.Reveal(), scoped.Auth.CurrentToken!.Reveal());
    }

    private static string Body(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

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

    /// <summary>The same frozen clock <c>AuthUnitTests</c> uses; M6's paths never read the time, but the client requires one.</summary>
    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset NowUtc()
        {
            return now;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedJitterSource : IJitterSource
    {
        public double NextDouble()
        {
            return 0.5;
        }
    }
}
