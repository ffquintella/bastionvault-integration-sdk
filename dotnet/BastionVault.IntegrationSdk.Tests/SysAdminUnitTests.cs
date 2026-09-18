using System.Reflection;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M7a's unit coverage for the parts of <c>06-system-api.md</c> that a single-exchange wire fixture
/// cannot express (DR-0012): the client-side refusals of SYS-010 and SYS-022, SYS-011's redaction
/// and zeroing, SYS-013's non-retryable and no-failover flags, the SYS-026 cache's TTL, scoping and
/// invalidation, SYS-023's third error row, and KV-001's mapping of a mount type onto a KV version.
/// </summary>
public sealed class SysAdminUnitTests
{
    private const string Address = "https://vault.example.com:8200";
    private const string Token = "s.FAKEtoken0000000000000000";
    private const string TwoMounts = """{"secret/":{"type":"kv-v2","description":"kv"},"transit/":{"type":"transit","description":""}}""";

    // ---- SYS-010: the two client-side validation rules ------------------------------------

    [Theory]
    [Requirement("SYS-010")]
    [Trait("Requirement", "SYS-010")]
    [InlineData(3, null, "threshold")]
    [InlineData(null, 2, "shares")]
    [InlineData(0, 0, "shares")]
    [InlineData(256, 2, "shares")]
    [InlineData(3, 0, "threshold")]
    [InlineData(3, 4, "threshold")]
    public async Task Init_refuses_a_broken_shares_threshold_pair_client_side_without_sending_a_request(int? shares, int? threshold, string argument)
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.InitAsync(shares, threshold));

        Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
        Assert.False(failure.Retryable);
        // "Client-side" is the load-bearing half of SYS-010: zero attempts and an untouched wire.
        Assert.Equal(0, failure.Attempts);
        Assert.Empty(transport.Requests);
        Assert.Equal(argument, failure.Details["argument"]);
    }

    [Fact]
    [Requirement("SYS-010")]
    [Trait("Requirement", "SYS-010")]
    public async Task Init_with_both_arguments_sends_both_wire_fields_and_with_neither_sends_an_empty_object()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["aa"],"root_token":"s.root"}"""));
        transport.EnqueueResponse(200, body: Json("""{"keys":["aa"],"root_token":"s.root"}"""));
        using BastionVaultClient client = BuildClient(transport);

        using (InitResult _ = await client.Sys.InitAsync(5, 3))
        {
        }

        // Both omitted is SYS-010's HSM auto-unseal case, and it must not invent a default pair.
        using (InitResult _ = await client.Sys.InitAsync())
        {
        }

        Assert.Equal("""{"secret_shares":5,"secret_threshold":3}""", BodyOf(transport.Requests[0]));
        Assert.Equal("{}", BodyOf(transport.Requests[1]));
        Assert.Equal($"{Address}/v1/sys/init", transport.Requests[0].Uri.ToString());
        Assert.Equal("PUT", transport.Requests[0].Method);
    }

    [Theory]
    [Requirement("SYS-010")]
    [Trait("Requirement", "SYS-010")]
    [InlineData(400, "secret_shares and secret_threshold are required when the vault uses the shamir seal", "BV-INPUT-100")]
    [InlineData(400, "secret_shares and secret_threshold must be provided together", "BV-INPUT-100")]
    [InlineData(409, "BastionVault is already initialized.", "BV-CONFLICT-005")]
    public async Task Init_maps_the_three_documented_server_strings(int status, string message, string expected)
    {
        // The first two are an unrecognised 4xx, which the shared mapping already answers with
        // BV-INPUT-100 (ERR step 5); the third is an Appendix B §2 exact rule. Asserted here
        // because SYS-010 names all three, and "it already works" is a claim that needs a test.
        FakeTransport transport = new();
        transport.EnqueueResponse(status, body: Json($$"""{"error":{{JsonSerializer.Serialize(message)}}}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.InitAsync(5, 3));

        Assert.Equal(expected, failure.Code);
        Assert.Equal(status, failure.StatusCode);
    }

    [Fact]
    [Requirement("SYS-010")]
    [Trait("Requirement", "SYS-010")]
    public async Task InitStatus_reads_the_initialized_field_and_raises_on_a_bodyless_response()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"initialized":true}"""));
        transport.EnqueueResponse(200, body: Json("""{"initialized":false}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        Assert.True(await client.Sys.InitStatusAsync());
        Assert.False(await client.Sys.InitStatusAsync());
        Assert.Equal($"{Address}/v1/sys/init", transport.Requests[0].Uri.ToString());
        Assert.Equal("GET", transport.Requests[0].Method);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.InitStatusAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    [Fact]
    [Requirement("SYS-010")]
    [Trait("Requirement", "SYS-010")]
    public async Task Init_raises_BV_PROTOCOL_002_when_the_response_carries_no_root_token()
    {
        // D-M1c-25: the response table names `root_token`, so an absent one is an envelope
        // mismatch rather than an empty token a caller might store and later try to use.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["aa"]}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException missingToken = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.InitAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, missingToken.Code);
        Assert.Equal("root_token", missingToken.Details["expectedField"]);

        BastionVaultException noBody = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.InitAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, noBody.Code);
    }

    // ---- SYS-011: redaction, non-logging, and zeroing on dispose --------------------------

    [Fact]
    [Requirement("SYS-011")]
    [Trait("Requirement", "SYS-011")]
    public async Task Init_keys_and_root_token_never_reach_a_log_line_an_observer_event_or_a_ToString()
    {
        const string ShareOne = "1f2e3d4c5b6a79880f1e2d3c4b5a6978";
        const string ShareTwo = "aa11bb22cc33dd44ee55ff6600778899";
        const string RootToken = "s.FAKErootFROMinit00000000";

        CapturingClientLogger logger = new();
        CapturingRequestObserver observer = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$"""{"keys":["{{ShareOne}}","{{ShareTwo}}"],"root_token":"{{RootToken}}"}"""));
        using BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Logger = logger;
            options.Observer = observer;
        });

        using InitResult result = await client.Sys.InitAsync(2, 2);

        // (a) the redacting type, on both members.
        Assert.Equal("[REDACTED]", result.RootToken.ToString());
        Assert.All(result.Keys, key => Assert.Equal("[REDACTED]", key.ToString()));

        // (b) the container's own ToString, which is the likeliest accidental log line.
        Assert.DoesNotContain(ShareOne, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ShareTwo, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(RootToken, result.ToString(), StringComparison.Ordinal);

        // (c) nothing the SDK surfaced carries the material: every log line and every observer
        // event, rendered in full, for all three secrets.
        string[] surfaced = [.. logger.Lines, .. observer.Events.Select(captured => captured.ToString())];
        Assert.All(
            new[] { ShareOne, ShareTwo, RootToken },
            secret => Assert.All(surfaced, line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal)));

        // (d) and the values are still retrievable through the deliberately-named accessor, so
        // this is redaction rather than loss.
        Assert.Equal([ShareOne, ShareTwo], result.Keys.Select(key => key.Reveal()));
        Assert.Equal(RootToken, result.RootToken.Reveal());
        Assert.Equal(2, result.KeyCount);
    }

    [Fact]
    [Requirement("SYS-011")]
    [Trait("Requirement", "SYS-011")]
    public async Task Disposing_an_InitResult_zeroes_the_buffers_it_owns_and_closes_both_accessors()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["1f2e3d4c","aa11bb22"],"root_token":"s.FAKErootFROMinit00000000"}"""));
        using BastionVaultClient client = BuildClient(transport);

        InitResult result = await client.Sys.InitAsync(2, 2);
        Assert.False(result.IsDisposed);

        result.Dispose();
        result.Dispose(); // idempotent

        Assert.True(result.IsDisposed);
        _ = Assert.Throws<ObjectDisposedException>(() => result.Keys);
        _ = Assert.Throws<ObjectDisposedException>(() => result.RootToken);
        // A count is not secret material, so it survives dispose.
        Assert.Equal(2, result.KeyCount);
        Assert.DoesNotContain("1f2e3d4c", result.ToString(), StringComparison.Ordinal);

        // The claim SYS-011 actually makes is about the bytes, so it is asserted against the
        // bytes: every buffer this instance owns is all-zero after Dispose. A test that only
        // checked the accessors would pass against an implementation that merely set a flag.
        Assert.All(OwnedBuffers(result), buffer => Assert.All(buffer, character => Assert.Equal('\0', character)));
    }

    // ---- SYS-012 / SYS-013: unseal, seal, and their two exclusions ------------------------

    [Fact]
    [Requirement("SYS-012")]
    [Trait("Requirement", "SYS-012")]
    public async Task Unseal_returns_a_SealStatus_with_the_SYS_005_swap_applied_and_refuses_an_empty_key()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"sealed":false,"t":5,"n":3,"progress":3}"""));
        using BastionVaultClient client = BuildClient(transport);

        SealStatus status = await client.Sys.UnsealAsync("1f2e3d4c");

        Assert.False(status.Sealed);
        Assert.Equal(5, status.T);
        Assert.Equal(3, status.N);
        // SYS-005's swap is applied by the same function SealStatus uses, so Unseal cannot drift.
        Assert.Equal(5, status.KeyShares);
        Assert.Equal(3, status.KeyThreshold);
        Assert.Equal(3, status.Progress);
        Assert.Equal("PUT", transport.Requests[0].Method);
        Assert.Equal($"{Address}/v1/sys/unseal", transport.Requests[0].Uri.ToString());
        Assert.Equal("""{"key":"1f2e3d4c"}""", BodyOf(transport.Requests[0]));

        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Sys.UnsealAsync(string.Empty));
    }

    [Fact]
    [Requirement("SYS-012")]
    [Trait("Requirement", "SYS-012")]
    public async Task Unseal_on_an_uninitialised_vault_is_BV_SERVER_007_and_a_bodyless_200_is_an_envelope_mismatch()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(400, body: Json("""{"error":"BastionVault is not initialized."}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.UnsealAsync("1f2e"));
        Assert.Equal(ErrorCodes.ServerNotInitialized, failure.Code);

        BastionVaultException mismatch = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.UnsealAsync("1f2e"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, mismatch.Code);
    }

    [Fact]
    [Requirement("SYS-013")]
    [Trait("Requirement", "SYS-013")]
    public async Task Seal_succeeds_on_204_and_is_never_retried_however_the_retry_policy_is_configured()
    {
        // The policy here is the most permissive one a caller can write: three attempts, the
        // failure's own code on RetryOn, and RetryIdempotentOnly switched off so writes are
        // eligible. SYS-013's flag must beat all three, so the assertion is exactly one request.
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(500, body: Json("""{"error":"internal"}"""));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport, options => options.RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialBackoff = TimeSpan.Zero,
            RetryIdempotentOnly = false,
            RetryOn = [ErrorCodes.ServerInternalError],
        });

        await client.Sys.SealAsync();
        Assert.Equal("PUT", transport.Requests[0].Method);
        Assert.Equal($"{Address}/v1/sys/seal", transport.Requests[0].Uri.ToString());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.SealAsync());

        Assert.Equal(ErrorCodes.ServerInternalError, failure.Code);
        Assert.Equal(1, failure.Attempts);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    [Requirement("SYS-013")]
    [Trait("Requirement", "SYS-013")]
    public async Task Unseal_is_never_retried_however_the_retry_policy_is_configured()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"error":"internal"}"""));
        transport.EnqueueResponse(200, body: Json("""{"sealed":false,"t":1,"n":1,"progress":0}"""));
        using BastionVaultClient client = BuildClient(transport, options => options.RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialBackoff = TimeSpan.Zero,
            RetryIdempotentOnly = false,
            RetryOn = [ErrorCodes.ServerInternalError],
        });

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.UnsealAsync("1f2e"));

        Assert.Equal(1, failure.Attempts);
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-013")]
    [Requirement("DSC-045")]
    [Trait("Requirement", "SYS-013")]
    public async Task Seal_and_Unseal_are_excluded_from_failover_on_an_armed_discovery_client()
    {
        // DSC-045's seam, not a parallel one: both operations pass `nodeLocal: true` into the M5
        // executor, so `WillFailover` declines before the replay is ever considered. Without the
        // flag a refused connection on the pinned node would probe and replay against bv-2, which
        // for `Seal` would seal the wrong node and for `Unseal` would spend a key share against a
        // different node's progress counter.
        foreach (bool sealing in new[] { true, false })
        {
            RecordingTransport transport = new(_ => throw TransportFailureMapper.Map(TransportFailureKind.ConnectionRefused));
            using BastionVaultClient client = ArmedClient(transport);

            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
                () => sealing ? client.Sys.SealAsync() : client.Sys.UnsealAsync("1f2e"));

            // One request, to the pinned node only: no health probe, no replay.
            string path = sealing ? "seal" : "unseal";
            Assert.Equal([$"https://bv-1.corp.example:8200/v1/sys/{path}"], transport.Urls);
            Assert.Equal(ErrorCodes.DiscoveryNodeUnavailable, failure.Code);
            Assert.Equal("https://bv-1.corp.example:8200", client.SelectedNode!.Url);
        }
    }

    // ---- SYS-020 / SYS-021: the two-field table and the type constants --------------------

    [Fact]
    [Requirement("SYS-020")]
    [Trait("Requirement", "SYS-020")]
    public void MountRequest_offers_no_Config_member_and_no_lease_tuning_of_any_spelling()
    {
        // ⚠️ SYS-020 is a prohibition, so the test is a prohibition: the server ignores `config`
        // outright, and a member the SDK accepted and the server discarded would fail silently.
        // Asserted by reflection because the requirement is about the *absence* of a member, which
        // no behavioural test can observe.
        string[] members = typeof(MountRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["Description", "Options", "Type"], members.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Config", members, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DefaultLeaseTtl", members, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("MaxLeaseTtl", members, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Requirement("SYS-021")]
    [Trait("Requirement", "SYS-021")]
    public void The_mount_type_constants_are_exactly_the_strings_SYS_021_names()
    {
        Assert.Equal(
            ["kv", "kv-v2", "transit", "pki", "ssh", "ssh-broker", "totp", "openldap", "files", "resource", "rustion", "notifications", "cert-lifecycle"],
            MountTypes.Secret);
        // `cert` is on the list and disabled on current servers; SYS-021 names it either way, and
        // omitting it would make an operator's `BV-SERVER-004` unexplainable.
        Assert.Equal(["userpass", "approle", "ferrogate", "fido2", "oidc", "saml", "cert"], MountTypes.Auth);
        Assert.Equal("kv-v2", MountTypes.KvV2);
        Assert.Equal("cert", AuthTypes.Cert);
    }

    [Fact]
    [Requirement("SYS-020")]
    [Trait("Requirement", "SYS-020")]
    public async Task ListMounts_skips_a_table_property_that_is_not_a_typed_entry_and_raises_on_a_bodyless_response()
    {
        // Shape B means the table *is* the top-level object, so a scalar or a typeless object
        // alongside the entries must be skipped rather than become a mount with no type.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv-v2"},"request_id":"abc","broken/":{"description":"no type"},"typed/":{"type":7}}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, MountInfo> table = await client.Sys.ListMountsAsync();

        Assert.Equal(["secret/"], table.Keys);
        Assert.Null(table["secret/"].Description);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ListMountsAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    [Fact]
    [Requirement("SYS-020")]
    [Trait("Requirement", "SYS-020")]
    public async Task Mount_sends_the_options_map_when_one_is_given_and_omits_it_otherwise()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        await client.Sys.MountAsync("kv2", new MountRequest
        {
            Type = MountTypes.KvV2,
            Options = new Dictionary<string, string>(StringComparer.Ordinal) { ["version"] = "2" },
        });
        await client.Sys.MountAsync("kv3", new MountRequest
        {
            Type = MountTypes.KvV2,
            Options = new Dictionary<string, string>(StringComparer.Ordinal),
        });

        Assert.Equal("""{"type":"kv-v2","options":{"version":"2"}}""", BodyOf(transport.Requests[0]));
        // An empty map is not an empty `options` object on the wire: it is no field at all.
        Assert.Equal("""{"type":"kv-v2"}""", BodyOf(transport.Requests[1]));
    }

    // ---- SYS-022: path normalisation, both directions -------------------------------------

    [Theory]
    [Requirement("SYS-022")]
    [Trait("Requirement", "SYS-022")]
    [InlineData("kv2")]
    [InlineData("kv2/")]
    [InlineData("/kv2/")]
    [InlineData("  kv2/  ")]
    public async Task A_mount_path_is_accepted_with_or_without_its_slashes_and_reaches_one_url(string path)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        await client.Sys.MountAsync(path, new MountRequest { Type = MountTypes.KvV2 });
        await client.Sys.UnmountAsync(path);

        Assert.Equal([$"{Address}/v1/sys/mounts/kv2", $"{Address}/v1/sys/mounts/kv2"], transport.Requests.Select(request => request.Uri.ToString()));
        Assert.Equal(["POST", "DELETE"], transport.Requests.Select(request => request.Method));
    }

    [Fact]
    [Requirement("SYS-022")]
    [Trait("Requirement", "SYS-022")]
    public async Task A_server_key_with_no_trailing_slash_is_normalised_and_an_unusable_one_is_passed_through()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"secret":{"type":"kv-v2"},"transit/":{"type":"transit"},"/":{"type":"weird"},"   ":{"type":"weirder"}}"""));
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, MountInfo> table = await client.Sys.ListMountsAsync();

        // A server key is not the caller's argument, so a degenerate one is passed through rather
        // than refused: an BV-INPUT-001 would blame the caller for the server's answer.
        // Both degenerate forms D-M7-7 names — a bare `/` and an all-whitespace key — reach
        // MountPaths.NormaliseServerKey's pass-through arm and survive as the server sent them.
        Assert.Equal(["   ", "/", "secret/", "transit/"], table.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Requirement("SYS-022")]
    [Trait("Requirement", "SYS-022")]
    public async Task An_empty_mount_path_is_refused_client_side_on_every_operation_that_takes_one()
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        Func<Task>[] calls =
        [
            () => client.Sys.MountAsync("  ", new MountRequest { Type = MountTypes.Kv }),
            () => client.Sys.MountAsync("/", new MountRequest { Type = MountTypes.Kv }),
            // `null!` is the `path ?? string.Empty` arm of MountPaths.ToWire and ToAuthWire. A
            // caller reaching a non-nullable parameter with null is a nullable-reference-types
            // violation rather than an API contract, but the arm exists, is reachable from a
            // non-annotated consumer (F# , VB, or a C# project with NRT off), and must produce
            // the same BV-INPUT-001 refusal as `""` rather than a NullReferenceException.
            () => client.Sys.MountAsync(null!, new MountRequest { Type = MountTypes.Kv }),
            () => client.Sys.EnableAuthMethodAsync(null!, new MountRequest { Type = AuthTypes.Userpass }),
            () => client.Sys.UnmountAsync(string.Empty),
            () => client.Sys.ReadMountAsync(string.Empty),
            () => client.Sys.MountTypeOfAsync(string.Empty),
            () => client.Sys.RemountAsync(string.Empty, "kv2"),
            () => client.Sys.RemountAsync("kv", string.Empty),
            () => client.Sys.EnableAuthMethodAsync("auth/", new MountRequest { Type = AuthTypes.Userpass }),
            () => client.Sys.DisableAuthMethodAsync("auth/"),
            () => client.Kv.DetectVersionAsync(string.Empty),
        ];

        foreach (Func<Task> call in calls)
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
            Assert.Equal(0, failure.Attempts);
        }

        // SYS-022 says the server answers an empty path with a bare 404 and an empty body, which a
        // caller could not tell from "no such mount" — so nothing was sent.
        Assert.Empty(transport.Requests);
        // The refusal names which argument was wrong, which matters most for Remount's pair.
        BastionVaultException fromFailure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.RemountAsync(" ", "kv2"));
        Assert.Equal("from", fromFailure.Details["argument"]);
        BastionVaultException toFailure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.RemountAsync("kv", " "));
        Assert.Equal("to", toFailure.Details["argument"]);
    }

    // ---- SYS-023 / SYS-024: the remount and quota error rows ------------------------------

    [Theory]
    [Requirement("SYS-023")]
    [Trait("Requirement", "SYS-023")]
    [InlineData("no matching mount at kv/", "BV-NOTFOUND-002")]
    [InlineData("path already in use at secret/", "BV-CONFLICT-004")]
    [InlineData("Unknown mount table type.", "BV-INPUT-100")]
    [InlineData("something else entirely", "BV-CONFLICT-001")]
    public async Task Remount_maps_each_409_row_and_leaves_an_unlisted_409_on_the_default(string message, string expected)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(409, body: Json($$"""{"error":{{JsonSerializer.Serialize(message)}}}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.RemountAsync("kv", "secret"));

        Assert.Equal(expected, failure.Code);
        Assert.Equal(409, failure.StatusCode);
        // The remap keeps everything a caller diagnoses with; only the code changes.
        Assert.Equal(message, failure.ServerMessage);
        Assert.Equal(1, failure.Attempts);
        // SYS-022: both paths are sent in the table form the server's own error text uses.
        Assert.Equal("""{"from":"kv/","to":"secret/"}""", BodyOf(transport.Requests[0]));
        Assert.Equal($"{Address}/v1/sys/remount", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("SYS-024")]
    [Trait("Requirement", "SYS-024")]
    public async Task A_mount_quota_breach_is_BV_QUOTA_001()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(507, body: Json("""{"error":"namespace quota exceeded: mounts limit of 12 reached"}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.MountAsync("kv2", new MountRequest { Type = MountTypes.KvV2 }));

        Assert.Equal(ErrorCodes.QuotaNamespaceQuotaExceeded, failure.Code);
        Assert.Equal(507, failure.StatusCode);
    }

    // ---- SYS-025: the client-side per-mount read ------------------------------------------

    [Fact]
    [Requirement("SYS-025")]
    [Trait("Requirement", "SYS-025")]
    public async Task ReadMount_filters_the_whole_table_client_side_and_returns_null_when_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        using BastionVaultClient client = BuildClient(transport);

        MountInfo? found = await client.Sys.ReadMountAsync("secret");
        MountInfo? absent = await client.Sys.ReadMountAsync("nope/");

        Assert.Equal("kv-v2", found!.Type);
        Assert.Equal("kv", found.Description);
        Assert.Null(absent);
        // There is no per-mount endpoint (SYS-025), so both calls hit the table endpoint, and
        // neither is served from the SYS-026 cache — that cache is scoped to MountTypeOf.
        Assert.All(transport.Requests, request => Assert.Equal($"{Address}/v1/sys/mounts", request.Uri.ToString()));
        Assert.Equal(2, transport.Requests.Count);
    }

    // ---- SYS-026: the cache's TTL, its invalidation, and its scope ------------------------

    [Fact]
    [Requirement("SYS-026")]
    [Trait("Requirement", "SYS-026")]
    public async Task MountTypeOf_answers_from_one_request_within_the_TTL_and_refetches_after_it()
    {
        MutableClock clock = new(DateTimeOffset.UnixEpoch);
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv"}}"""));
        using BastionVaultClient client = BuildClient(transport, options => options.Clock = clock);

        Assert.Equal("kv-v2", await client.Sys.MountTypeOfAsync("secret"));
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("kv-v2", await client.Sys.MountTypeOfAsync("secret/"));
        Assert.Equal("transit", await client.Sys.MountTypeOfAsync("transit"));
        // An absent mount is null, not an error: SYS-026's return type is nullable and KV-001 is
        // what turns the null into BV-NOTFOUND-002.
        Assert.Null(await client.Sys.MountTypeOfAsync("nope"));
        _ = Assert.Single(transport.Requests);

        // 60 s is the TTL, so at exactly 60 s the entry is expired rather than still live.
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("kv", await client.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [Requirement("SYS-026")]
    [Trait("Requirement", "SYS-026")]
    [InlineData("mount")]
    [InlineData("unmount")]
    [InlineData("remount")]
    public async Task Mount_Unmount_and_Remount_each_invalidate_the_cache(string operation)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv"}}"""));
        using BastionVaultClient client = BuildClient(transport);

        Assert.Equal("kv-v2", await client.Sys.MountTypeOfAsync("secret"));

        await (operation switch
        {
            "mount" => client.Sys.MountAsync("kv2", new MountRequest { Type = MountTypes.KvV2 }),
            "unmount" => client.Sys.UnmountAsync("kv2"),
            _ => client.Sys.RemountAsync("kv2", "kv3"),
        });

        // Refetched rather than answered from a 60-second-old snapshot the mutation invalidated.
        Assert.Equal("kv", await client.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(3, transport.Requests.Count);
    }

    [Fact]
    [Requirement("SYS-026")]
    [Trait("Requirement", "SYS-026")]
    public async Task The_cache_is_keyed_by_namespace_so_one_tenants_table_never_answers_for_another()
    {
        // The mount table is namespace-scoped and every WithNamespace view shares one
        // ClientContext (D-M1b-9), so an un-keyed cache would give a *wrong* answer here, not a
        // stale one. A mutation in one namespace likewise leaves the other's entry alone.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv"}}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient root = BuildClient(transport);
        BastionVaultClient tenant = root.WithNamespace("team-a");

        Assert.Equal("kv-v2", await root.Sys.MountTypeOfAsync("secret"));
        Assert.Equal("kv", await tenant.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(2, transport.Requests.Count);

        await tenant.Sys.MountAsync("kv2", new MountRequest { Type = MountTypes.KvV2 });

        // The root namespace's entry survives the tenant's mount.
        Assert.Equal("kv-v2", await root.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(3, transport.Requests.Count);
    }

    // ---- SYS-026: the key is the *effective* namespace, not the view's (B1, D-M7-25) ------

    [Fact]
    [Requirement("SYS-026")]
    [Trait("Requirement", "SYS-026")]
    public async Task A_per_call_namespace_override_does_not_poison_the_views_cache_entry()
    {
        // Failure 1 of 3. The request goes out under RequestOptions.Namespace
        // (RequestExecutor.EffectiveNamespace), so keying on the view's namespace would file
        // tenant-b's table under tenant-a and answer the next unqualified read with it.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"transit"}}"""));
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        using BastionVaultClient client = BuildClient(transport);
        BastionVaultClient tenantA = client.WithNamespace("tenant-a");

        Assert.Equal("transit", await tenantA.Sys.MountTypeOfAsync("secret", new RequestOptions { Namespace = "tenant-b" }));
        Assert.Equal("tenant-b", transport.Requests[0].Headers["X-BastionVault-Namespace"]);

        // tenant-a was never fetched, so this must go to the wire and must return tenant-a's answer.
        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal("tenant-a", transport.Requests[1].Headers["X-BastionVault-Namespace"]);
    }

    [Fact]
    [Requirement("SYS-026")]
    [Trait("Requirement", "SYS-026")]
    public async Task A_per_call_namespace_override_is_never_served_from_another_namespaces_entry()
    {
        // Failure 2 of 3, and the reverse ordering of the test above: with tenant-a warm, a call
        // explicitly aimed at tenant-b must not be answered out of tenant-a's snapshot with no
        // request issued at all.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"transit"}}"""));
        using BastionVaultClient client = BuildClient(transport);
        BastionVaultClient tenantA = client.WithNamespace("tenant-a");

        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret"));
        Assert.Equal("transit", await tenantA.Sys.MountTypeOfAsync("secret", new RequestOptions { Namespace = "tenant-b" }));

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal("tenant-b", transport.Requests[1].Headers["X-BastionVault-Namespace"]);
        // And tenant-a's own entry is still warm: the second call neither read nor evicted it.
        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [Requirement("SYS-026")]
    [Trait("Requirement", "SYS-026")]
    [InlineData("mount")]
    [InlineData("unmount")]
    [InlineData("remount")]
    public async Task A_mutation_under_a_namespace_override_invalidates_the_namespace_it_mutated(string operation)
    {
        // Failure 3 of 3, and the direct miss of SYS-026's invalidation clause: the mutation
        // changes tenant-b, so tenant-b's entry must go and tenant-a's must stay.
        RequestOptions tenantB = new() { Namespace = "tenant-b" };
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));       // tenant-a, warm
        transport.EnqueueResponse(200, body: Json(TwoMounts));       // tenant-b, warm
        transport.EnqueueResponse(204);                              // the mutation, under tenant-b
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv"}}"""));
        using BastionVaultClient client = BuildClient(transport);
        BastionVaultClient tenantA = client.WithNamespace("tenant-a");

        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret"));
        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret", tenantB));

        await (operation switch
        {
            "mount" => tenantA.Sys.MountAsync("kv2", new MountRequest { Type = MountTypes.KvV2 }, tenantB),
            "unmount" => tenantA.Sys.UnmountAsync("kv2", tenantB),
            _ => tenantA.Sys.RemountAsync("kv2", "kv3", tenantB),
        });

        Assert.Equal("tenant-b", transport.Requests[2].Headers["X-BastionVault-Namespace"]);
        // tenant-b refetches, and sees the mutation this same client performed.
        Assert.Equal("kv", await tenantA.Sys.MountTypeOfAsync("secret", tenantB));
        Assert.Equal(4, transport.Requests.Count);
        // tenant-a was not mutated, so its entry survives (D-M7-4's no-stampede rule).
        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(4, transport.Requests.Count);
    }

    [Fact]
    [Requirement("SYS-026")]
    [Trait("Requirement", "SYS-026")]
    public async Task A_trailing_slash_on_the_namespace_does_not_open_a_second_cache_entry()
    {
        // The key trims exactly as EffectiveNamespace does, so "tenant-a" and "tenant-a/" are one
        // namespace on the wire and must be one entry in the cache.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        using BastionVaultClient client = BuildClient(transport);
        BastionVaultClient tenantA = client.WithNamespace("tenant-a");

        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret", new RequestOptions { Namespace = "tenant-a/" }));
        Assert.Equal("kv-v2", await tenantA.Sys.MountTypeOfAsync("secret"));
        _ = Assert.Single(transport.Requests);
        Assert.Equal("tenant-a", transport.Requests[0].Headers["X-BastionVault-Namespace"]);
    }

    [Fact]
    [Requirement("KV-001")]
    [Requirement("SYS-026")]
    [Trait("Requirement", "KV-001")]
    public async Task DetectVersion_under_a_namespace_override_never_answers_from_another_tenants_table()
    {
        // KvOperations.DetectVersionAsync forwards `options` verbatim to Sys.MountTypeOf, so the
        // cross-tenant read reached the caller as a wrong KvVersion — i.e. a read or write against
        // the wrong KV path across a tenancy boundary. This is that path, end to end.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv-v2"}}"""));
        transport.EnqueueResponse(200, body: Json("""{"secret/":{"type":"kv"}}"""));
        using BastionVaultClient client = BuildClient(transport);
        BastionVaultClient tenantA = client.WithNamespace("tenant-a");

        Assert.Equal(KvVersion.V2, await tenantA.Kv.DetectVersionAsync("secret"));
        Assert.Equal(KvVersion.V1, await tenantA.Kv.DetectVersionAsync("secret", new RequestOptions { Namespace = "tenant-b" }));
        Assert.Equal(2, transport.Requests.Count);
    }

    // ---- The detailed table ---------------------------------------------------------------

    [Fact]
    [Requirement("SYS-020")]
    [Requirement("SYS-030")]
    [Trait("Requirement", "SYS-020")]
    public async Task ListMountsDetailed_splits_the_two_halves_carries_four_fields_and_keeps_auth_paths_relative()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """
            {"secret":{"secret/":{"type":"kv-v2","description":"kv","uuid":"u-1","options":{"version":"2"}},"bad/":{"description":"no type"}},
             "auth":{"auth/userpass/":{"type":"userpass"}}}
            """));
        transport.EnqueueResponse(200, body: Json("""{"secret":{}}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        MountTable table = await client.Sys.ListMountsDetailedAsync();

        MountDetail detail = table.Secret["secret/"];
        Assert.Equal("kv-v2", detail.Type);
        Assert.Equal("kv", detail.Description);
        Assert.Equal("u-1", detail.Uuid);
        Assert.Equal("2", detail.Options!["version"].GetString());
        Assert.Equal(["secret/"], table.Secret.Keys);
        // SYS-030: relative, never `auth/`-prefixed, even though this endpoint sends the prefix.
        Assert.Equal(["userpass/"], table.Auth.Keys);
        Assert.Null(table.Auth["userpass/"].Description);
        Assert.Null(table.Auth["userpass/"].Uuid);
        Assert.Null(table.Auth["userpass/"].Options);
        Assert.Equal($"{Address}/v1/sys/internal/ui/mounts", transport.Requests[0].Uri.ToString());

        // A half the server omits is an empty map, not a null one.
        MountTable half = await client.Sys.ListMountsDetailedAsync();
        Assert.Empty(half.Secret);
        Assert.Empty(half.Auth);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ListMountsDetailedAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    // ---- SYS-030: auth-method administration ----------------------------------------------

    [Theory]
    [Requirement("SYS-030")]
    [Trait("Requirement", "SYS-030")]
    [InlineData("userpass")]
    [InlineData("userpass/")]
    [InlineData("auth/userpass")]
    [InlineData("auth/userpass/")]
    [InlineData("/auth/userpass/")]
    public async Task An_auth_mount_path_is_accepted_in_either_form_and_reaches_the_relative_url(string path)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        await client.Sys.EnableAuthMethodAsync(path, new MountRequest { Type = AuthTypes.Userpass });
        await client.Sys.DisableAuthMethodAsync(path);

        Assert.Equal(
            [$"{Address}/v1/sys/auth/userpass", $"{Address}/v1/sys/auth/userpass"],
            transport.Requests.Select(request => request.Uri.ToString()));
        Assert.Equal(["POST", "DELETE"], transport.Requests.Select(request => request.Method));
        Assert.Equal("""{"type":"userpass"}""", BodyOf(transport.Requests[0]));
    }

    [Fact]
    [Requirement("SYS-030")]
    [Trait("Requirement", "SYS-030")]
    public async Task ListAuthMethods_returns_relative_paths_whichever_form_the_server_sent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"auth/userpass/":{"type":"userpass","description":"u"},"approle":{"type":"approle"}}"""));
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyDictionary<string, MountInfo> methods = await client.Sys.ListAuthMethodsAsync();

        Assert.Equal(["approle/", "userpass/"], methods.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("userpass", methods["userpass/"].Type);
        Assert.Equal($"{Address}/v1/sys/auth", transport.Requests[0].Uri.ToString());
    }

    // ---- HsmStatus (v2-only, TRN-071) -----------------------------------------------------

    // D-M7-10's open question is closed by D-M7-12: the Strategic tree ruled that TRN-071 is
    // implemented and tested — by M3's `capabilities-self` pin, by this test's `HsmStatus` pin,
    // and by M7b's `/v2`-pinned SYS-045 routes — so the marker is applied here and TRN-071 leaves
    // the baseline. The tag names a requirement this test would fail without: the call below sets
    // `ApiVersion = "v1"` and still expects `/v2` on the wire, which is the pin and nothing else
    // (CLA-004).
    [Fact]
    [Requirement("TRN-071")]
    [Trait("Requirement", "TRN-071")]
    public async Task HsmStatus_pins_the_v2_prefix_and_leaves_an_omitted_field_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"type":"hsm","auto_unseal":true,"sealed":false,"initialized":true,"slot":3}"""));
        transport.EnqueueResponse(200, body: Json("""{"type":"shamir"}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        HsmStatus status = await client.Sys.HsmStatusAsync();

        Assert.Equal("hsm", status.Type);
        Assert.True(status.AutoUnseal);
        Assert.False(status.Sealed);
        Assert.True(status.Initialized);
        // The specification's body ends in an ellipsis, so an unmodelled field stays reachable.
        Assert.Equal(3, status.Raw.GetProperty("slot").GetInt32());
        Assert.Equal($"{Address}/v2/sys/hsm/status", transport.Requests[0].Uri.ToString());

        // TRN-071: the pin beats a caller's own ApiVersion, exactly as it does for
        // CapabilitiesSelf — the v1 handler does not exist, so honouring the override would be a
        // 404 the caller could not diagnose.
        HsmStatus shamir = await client.Sys.HsmStatusAsync(new RequestOptions { ApiVersion = "v1" });
        Assert.Equal($"{Address}/v2/sys/hsm/status", transport.Requests[1].Uri.ToString());
        Assert.Equal("shamir", shamir.Type);
        Assert.Null(shamir.AutoUnseal);
        Assert.Null(shamir.Sealed);
        Assert.Null(shamir.Initialized);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.HsmStatusAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    // ---- KV-001 ----------------------------------------------------------------------------

    [Theory]
    [Requirement("KV-001")]
    [Trait("Requirement", "KV-001")]
    [InlineData("kv", "V1")]
    [InlineData("kv-v2", "V2")]
    public async Task DetectVersion_maps_the_two_KV_mount_types(string type, string expected)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$"""{"secret/": {"type": "{{type}}"} }"""));
        using BastionVaultClient client = BuildClient(transport);

        KvVersion version = await client.Kv.DetectVersionAsync("secret");

        Assert.Equal(Enum.Parse<KvVersion>(expected), version);
    }

    [Fact]
    [Requirement("KV-001")]
    [Trait("Requirement", "KV-001")]
    public async Task DetectVersion_raises_BV_KV_010_for_a_non_KV_mount_and_BV_NOTFOUND_002_for_an_absent_one()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException notKv = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.DetectVersionAsync("transit"));
        Assert.Equal(ErrorCodes.KvNotAKvMount, notKv.Code);
        Assert.Equal("transit", notKv.Details["type"]);
        Assert.Equal("transit", notKv.Details["mount"]);

        BastionVaultException absent = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.DetectVersionAsync("nope"));
        Assert.Equal(ErrorCodes.NotFoundMountNotFound, absent.Code);
        Assert.False(absent.Details.ContainsKey("type"));

        // KV-001 names Sys.MountTypeOf specifically, and this is why: both lookups were served by
        // the one SYS-026 snapshot, so detecting N mounts costs one request rather than N.
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("KV-001")]
    [Requirement("SYS-026")]
    [Trait("Requirement", "KV-001")]
    public async Task DetectVersion_and_MountTypeOf_share_one_cache_on_one_client()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(TwoMounts));
        using BastionVaultClient client = BuildClient(transport);

        Assert.Equal("kv-v2", await client.Sys.MountTypeOfAsync("secret"));
        Assert.Equal(KvVersion.V2, await client.Kv.DetectVersionAsync("secret/"));

        // BastionVaultClient.Sys and .Kv both build a fresh operations object per property read,
        // so a cache held on either of them would have had a lifetime of one call.
        _ = Assert.Single(transport.Requests);
    }

    // ---- helpers ----------------------------------------------------------------------------

    /// <summary>Reflects out the <see cref="char"/> buffers an <see cref="InitResult"/> owns, for the zeroing assertion.</summary>
    private static IEnumerable<char[]> OwnedBuffers(InitResult result)
    {
        foreach (FieldInfo field in typeof(InitResult).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            switch (field.GetValue(result))
            {
                case char[] buffer:
                    yield return buffer;
                    break;
                case char[][] buffers:
                    foreach (char[] buffer in buffers)
                    {
                        yield return buffer;
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = Token,
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1, InitialBackoff = TimeSpan.Zero },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    /// <summary>A discovery-mode client with the pin and the candidate set already seeded, as <c>FailoverUnitTests.Pinned</c> does.</summary>
    private static BastionVaultClient ArmedClient(ITransport transport)
    {
        BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                Token = Token,
                Transport = transport,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = new RetryPolicy { MaxAttempts = 3, InitialBackoff = TimeSpan.Zero },
            },
            EnvironmentSource.None);

        string[] urls = ["https://bv-1.corp.example:8200", "https://bv-2.corp.example:8200"];
        client.Context.Discovery.Seed(
            new NodeSelection(urls[0], NodeState.ActiveLeader, null),
            urls.Select(url => new Uri(url, UriKind.Absolute))
                .Select(uri => new Candidate(uri.AbsoluteUri.TrimEnd('/'), uri.Host, uri.Port, null, null))
                .ToArray());
        return client;
    }

    private static string BodyOf(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>An <see cref="IClock"/> the test advances by hand, for the SYS-026 TTL boundary.</summary>
    private sealed class MutableClock : IClock
    {
        private DateTimeOffset now;

        public MutableClock(DateTimeOffset start)
        {
            now = start;
        }

        public void Advance(TimeSpan by)
        {
            now += by;
        }

        public DateTimeOffset NowUtc()
        {
            return now;
        }

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            now += duration;
            return Task.CompletedTask;
        }
    }

    /// <summary>A transport that answers from a delegate and records the URLs it was asked for.</summary>
    private sealed class RecordingTransport : ITransport
    {
        private readonly Func<TransportRequest, TransportResponse> answer;
        private readonly List<string> urls = [];

        public RecordingTransport(Func<TransportRequest, TransportResponse> answer)
        {
            this.answer = answer;
        }

        public IReadOnlyList<string> Urls => urls.ToArray();

        public bool SupportsCustomVerbs => true;

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            urls.Add(request.Uri.ToString());
            return Task.FromResult(answer(request));
        }
    }
}
