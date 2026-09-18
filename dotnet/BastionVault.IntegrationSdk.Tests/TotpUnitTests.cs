using System.Linq;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 11 — TOTP engine: TOT-001's client-side validation branches, TOT-002's redacting-type and
/// no-leak requirement, and TOT-004's leading-zero round trip. The wire-shape assertions
/// (<c>Totp.*</c> operations against a real request/response) live in the Appendix C
/// <c>totp.*</c> fixtures (<see cref="TotpFixturesTests"/>); this file covers what a
/// single-request fixture cannot: the argument-validation branches that never reach the wire.
/// </summary>
public sealed class TotpUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_rejects_zero_or_more_than_one_of_generate_key_url()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        // Zero: none of generate/key/url set.
        BastionVaultException zero = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync("alice", new TotpKeySpec { AccountName = "alice" }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, zero.Code);

        // Two: Generate and Key both set.
        BastionVaultException two = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync(
                "alice",
                new TotpKeySpec { Generate = true, Key = new SecretString("AAAAAAAAAAAAAAAA"), AccountName = "alice" }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, two.Code);

        // Two: Key and Url both set.
        BastionVaultException twoAgain = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync(
                "alice",
                new TotpKeySpec
                {
                    Key = new SecretString("AAAAAAAAAAAAAAAA"),
                    Url = new SecretString("otpauth://totp/alice?secret=AAAAAAAAAAAAAAAA"),
                }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, twoAgain.Code);

        Assert.Empty(new FakeTransport().Requests);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_rejects_digits_outside_6_or_8()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice", Digits = 7 }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_rejects_a_period_below_one_second()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice", Period = 0 }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_rejects_an_algorithm_outside_the_declared_enum()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync(
                "alice",
                new TotpKeySpec { Generate = true, AccountName = "alice", Algorithm = (TotpAlgorithm)99 }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_requires_AccountName_unless_the_url_carries_a_label()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        // No AccountName, and the url carries no label: rejected.
        BastionVaultException missing = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync(
                "alice",
                new TotpKeySpec { Url = new SecretString("otpauth://totp/?secret=AAAAAAAAAAAAAAAA") }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, missing.Code);

        // No AccountName, but the url carries a label: accepted (reaches the wire).
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"name":"alice","generate":false}}"""));
        BastionVaultClient labelled = BuildClient(transport);
        TotpKeyCreated created = await labelled.Totp.CreateKeyAsync(
            "alice",
            new TotpKeySpec { Url = new SecretString("otpauth://totp/issuer:alice?secret=AAAAAAAAAAAAAAAA") });
        Assert.Equal("alice", created.Name);
    }

    [Fact]
    [Requirement("TOT-002")]
    [Requirement("TST-051")]
    [Trait("Requirement", "TOT-002")]
    public async Task CreateKey_holds_key_and_url_in_SecretString_and_nothing_logs_the_seed()
    {
        const string seed = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
        const string url = "otpauth://totp/issuer:alice?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=issuer";
        const string barcodeBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

        CapturingClientLogger logger = new();
        CapturingRequestObserver observer = new();
        FakeTransport transport = new();
        // A server warning is what CapturingClientLogger actually captures on the response leg
        // (LogicalOperations.cs Warn on a `warnings` array); without it the logger scan below is
        // vacuous, so the fixture must carry one to make the non-vacuity guard meaningful.
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"name\":\"alice\",\"generate\":true,\"key\":\"" + seed + "\",\"url\":\"" + url + "\",\"barcode\":\"" + barcodeBase64
            + "\"},\"warnings\":[\"deprecated mount path\"]}"));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Logger = logger;
            options.Observer = observer;
        });

        TotpKeyCreated created = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice" });

        _ = Assert.IsType<SecretString>(created.Key);
        _ = Assert.IsType<SecretString>(created.Url);
        Assert.Equal(seed, created.Key!.Reveal());
        Assert.Equal(url, created.Url!.Reveal());
        Assert.Equal("[REDACTED]", created.Key!.ToString());
        Assert.Equal("[REDACTED]", created.Url!.ToString());
        Assert.True(created.Barcode.HasValue);

        // Non-vacuity guard: fails loudly if the capture ever stops observing anything, rather
        // than passing trivially the way the prior version of this test did.
        Assert.NotEmpty(logger.Lines);
        Assert.NotEmpty(observer.Events);

        // TST-051: the seed and the seed-bearing url never reach the log or the observer.
        string[] surfaced = [.. logger.Lines, .. observer.Events.Select(captured => captured.ToString())];
        Assert.All(surfaced, line => Assert.DoesNotContain(seed, line, StringComparison.Ordinal));
        Assert.All(surfaced, line => Assert.DoesNotContain(url, line, StringComparison.Ordinal));
    }

    [Fact]
    [Requirement("TOT-002")]
    [Requirement("TST-051")]
    [Trait("Requirement", "TOT-002")]
    public async Task CreateKey_provider_mode_never_logs_or_observes_the_supplied_seed_or_url()
    {
        const string seed = "JBSWY3DPEHPK3PXP";
        const string url = "otpauth://totp/issuer:alice?secret=JBSWY3DPEHPK3PXP&issuer=issuer";

        CapturingClientLogger logger = new();
        CapturingRequestObserver observer = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"name\":\"alice\",\"generate\":false},\"warnings\":[\"deprecated mount path\"]}"));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Logger = logger;
            options.Observer = observer;
        });

        _ = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Key = new SecretString(seed), AccountName = "alice" });

        // Non-vacuity guard, on the request leg this time: TotpWire.Serialise writes the seed and
        // the url straight into the outbound body (TotpWire.cs:119, :124), which is where a leak
        // is most likely and where no prior test looked.
        Assert.NotEmpty(logger.Lines);
        Assert.NotEmpty(observer.Events);

        string[] surfaced = [.. logger.Lines, .. observer.Events.Select(captured => captured.ToString())];
        Assert.All(surfaced, line => Assert.DoesNotContain(seed, line, StringComparison.Ordinal));
        Assert.All(surfaced, line => Assert.DoesNotContain(url, line, StringComparison.Ordinal));

        FakeTransport urlTransport = new();
        urlTransport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"name\":\"alice\",\"generate\":false},\"warnings\":[\"deprecated mount path\"]}"));
        CapturingClientLogger urlLogger = new();
        CapturingRequestObserver urlObserver = new();
        BastionVaultClient urlClient = BuildClient(urlTransport, options =>
        {
            options.Logger = urlLogger;
            options.Observer = urlObserver;
        });

        _ = await urlClient.Totp.CreateKeyAsync("alice", new TotpKeySpec { Url = new SecretString(url) });

        Assert.NotEmpty(urlLogger.Lines);
        Assert.NotEmpty(urlObserver.Events);
        string[] urlSurfaced = [.. urlLogger.Lines, .. urlObserver.Events.Select(captured => captured.ToString())];
        Assert.All(urlSurfaced, line => Assert.DoesNotContain(seed, line, StringComparison.Ordinal));
        Assert.All(urlSurfaced, line => Assert.DoesNotContain(url, line, StringComparison.Ordinal));
    }

    [Fact]
    [Requirement("TOT-003")]
    [Trait("Requirement", "TOT-003")]
    public async Task ValidateCode_answers_false_for_both_a_wrong_code_and_a_replayed_code()
    {
        // TOT-003: the server answers {valid:false} identically for a wrong code and a replay;
        // the SDK cannot and does not distinguish them, which is documented on
        // TotpOperations.ValidateCodeAsync rather than invented here.
        FakeTransport wrongCodeTransport = new();
        wrongCodeTransport.EnqueueResponse(200, body: Json("""{"data":{"valid":false}}"""));
        BastionVaultClient wrongCodeClient = BuildClient(wrongCodeTransport);
        Assert.False(await wrongCodeClient.Totp.ValidateCodeAsync("alice", "000000"));

        FakeTransport replayTransport = new();
        replayTransport.EnqueueResponse(200, body: Json("""{"data":{"valid":false}}"""));
        BastionVaultClient replayClient = BuildClient(replayTransport);
        Assert.False(await replayClient.Totp.ValidateCodeAsync("alice", "000000"));
    }

    [Fact]
    [Requirement("TOT-004")]
    [Trait("Requirement", "TOT-004")]
    public async Task A_leading_zero_code_survives_the_round_trip_as_a_string()
    {
        const string code = "007931";

        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"valid":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        bool valid = await client.Totp.ValidateCodeAsync("alice", code);

        Assert.True(valid);
        string sentBody = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        // The exact string, quoted — not the number 7931, which would drop the leading zero.
        Assert.Contains("\"code\":\"007931\"", sentBody, StringComparison.Ordinal);

        FakeTransport codeTransport = new();
        codeTransport.EnqueueResponse(200, body: Json("{\"data\":{\"code\":\"" + code + "\"}}"));
        BastionVaultClient codeClient = BuildClient(codeTransport);
        string generated = await codeClient.Totp.GenerateCodeAsync("alice");
        Assert.Equal(code, generated);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Requirement("TOT-002")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_leaves_barcode_absent_when_the_wire_omits_it()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{\"data\":{\"name\":\"alice\",\"generate\":false}}"));
        BastionVaultClient client = BuildClient(transport);

        TotpKeyCreated created = await client.Totp.CreateKeyAsync(
            "alice",
            new TotpKeySpec { Key = new SecretString("JBSWY3DPEHPK3PXP"), AccountName = "alice" });

        Assert.Null(created.Barcode);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Requirement("TOT-002")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_reads_a_valid_barcode_as_bytes()
    {
        const string barcodeBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"name\":\"alice\",\"generate\":true,\"barcode\":\"" + barcodeBase64 + "\"}}"));
        BastionVaultClient client = BuildClient(transport);

        TotpKeyCreated created = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice" });

        Assert.True(created.Barcode.HasValue);
        Assert.Equal(Convert.FromBase64String(barcodeBase64), created.Barcode!.Value.ToArray());
    }

    [Fact]
    [Requirement("TOT-002")]
    [Trait("Requirement", "TOT-002")]
    public async Task CreateKey_raises_a_protocol_error_for_a_present_but_unparsable_barcode()
    {
        // TOT-002: a corrupt barcode must not be silently swallowed into the same `null` that a
        // legitimately absent barcode produces — the caller needs to be able to tell them apart.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"name\":\"alice\",\"generate\":true,\"barcode\":\"not-valid-base64!!\"}}"));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice" }));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Theory]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    [InlineData(TotpAlgorithm.Sha1, "SHA1")]
    [InlineData(TotpAlgorithm.Sha512, "SHA512")]
    public async Task CreateKey_sends_every_declared_algorithm_spelling(TotpAlgorithm algorithm, string wireSpelling)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{\"data\":{\"name\":\"alice\",\"generate\":true}}"));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice", Algorithm = algorithm });

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains($"\"algorithm\":\"{wireSpelling}\"", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task GenerateCode_raises_a_protocol_error_when_the_wire_omits_code()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.GenerateCodeAsync("alice"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task An_empty_body_response_is_a_protocol_error_for_CreateKey_GenerateCode_and_ValidateCode()
    {
        // A 204 (or a 200 with an empty body) shapes to a null Response (TRN-050's other half),
        // which none of these three operations treats as absence: unlike a read, there is no
        // requirement-given meaning for "no envelope" here, so it is BV-PROTOCOL-002 rather than a
        // guessed default (D-M1c-25).
        FakeTransport createTransport = new();
        createTransport.EnqueueResponse(204);
        BastionVaultClient createClient = BuildClient(createTransport);
        BastionVaultException createFailure = await Assert.ThrowsAsync<BastionVaultException>(
            () => createClient.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice" }));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, createFailure.Code);

        FakeTransport generateTransport = new();
        generateTransport.EnqueueResponse(204);
        BastionVaultClient generateClient = BuildClient(generateTransport);
        BastionVaultException generateFailure = await Assert.ThrowsAsync<BastionVaultException>(
            () => generateClient.Totp.GenerateCodeAsync("alice"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, generateFailure.Code);

        FakeTransport validateTransport = new();
        validateTransport.EnqueueResponse(204);
        BastionVaultClient validateClient = BuildClient(validateTransport);
        BastionVaultException validateFailure = await Assert.ThrowsAsync<BastionVaultException>(
            () => validateClient.Totp.ValidateCodeAsync("alice", "000000"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, validateFailure.Code);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_serialises_every_optional_field_when_provided()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{\"data\":{\"name\":\"alice\",\"generate\":true}}"));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Totp.CreateKeyAsync(
            "alice",
            new TotpKeySpec
            {
                Generate = true,
                KeySize = 20,
                Issuer = "issuer",
                AccountName = "alice",
                Algorithm = TotpAlgorithm.Sha256,
                Digits = 8,
                Period = 60,
                Skew = 2,
                QrSize = 300,
                Exported = true,
                ReplayCheck = true,
            });

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"key_size\":20", body, StringComparison.Ordinal);
        Assert.Contains("\"issuer\":\"issuer\"", body, StringComparison.Ordinal);
        Assert.Contains("\"account_name\":\"alice\"", body, StringComparison.Ordinal);
        Assert.Contains("\"algorithm\":\"SHA256\"", body, StringComparison.Ordinal);
        Assert.Contains("\"digits\":8", body, StringComparison.Ordinal);
        Assert.Contains("\"period\":60", body, StringComparison.Ordinal);
        Assert.Contains("\"skew\":2", body, StringComparison.Ordinal);
        Assert.Contains("\"qr_size\":300", body, StringComparison.Ordinal);
        Assert.Contains("\"exported\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"replay_check\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Requirement("TOT-002")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_provider_mode_sends_the_supplied_key_as_the_seed()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{\"data\":{\"name\":\"alice\",\"generate\":false}}"));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Totp.CreateKeyAsync(
            "alice",
            new TotpKeySpec { Key = new SecretString("JBSWY3DPEHPK3PXP"), AccountName = "alice" });

        string body = Encoding.UTF8.GetString(transport.Requests[0].Body.Span);
        Assert.Contains("\"key\":\"JBSWY3DPEHPK3PXP\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"url\"", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task ListKeys_returns_the_wire_list_and_an_empty_list_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("{\"data\":{\"keys\":[\"alice\",\"bob\"]}}"));
        BastionVaultClient client = BuildClient(transport);
        IReadOnlyList<string> keys = await client.Totp.ListKeysAsync();
        Assert.Equal(["alice", "bob"], keys);

        FakeTransport emptyTransport = new();
        emptyTransport.EnqueueResponse(404);
        BastionVaultClient emptyClient = BuildClient(emptyTransport);
        Assert.Empty(await emptyClient.Totp.ListKeysAsync());
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task ReadKey_reads_every_field_and_returns_null_on_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"generate\":true,\"issuer\":\"issuer\",\"account_name\":\"alice\",\"algorithm\":\"SHA256\",\"digits\":8,\"period\":60,\"skew\":2,\"replay_check\":true}}"));
        BastionVaultClient client = BuildClient(transport);

        TotpKey? key = await client.Totp.ReadKeyAsync("alice");

        Assert.NotNull(key);
        Assert.True(key!.Generate);
        Assert.Equal("issuer", key.Issuer);
        Assert.Equal("alice", key.AccountName);
        Assert.Equal("SHA256", key.Algorithm);
        Assert.Equal(8, key.Digits);
        Assert.Equal(60, key.Period);
        Assert.Equal(2, key.Skew);
        Assert.True(key.ReplayCheck);

        FakeTransport missingTransport = new();
        missingTransport.EnqueueResponse(404);
        BastionVaultClient missingClient = BuildClient(missingTransport);
        Assert.Null(await missingClient.Totp.ReadKeyAsync("ghost"));
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task ReadKey_leaves_absent_optional_fields_null_or_false()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        TotpKey? key = await client.Totp.ReadKeyAsync("alice");

        Assert.NotNull(key);
        Assert.False(key!.Generate);
        Assert.Null(key.Issuer);
        Assert.Null(key.AccountName);
        Assert.Null(key.Algorithm);
        Assert.Null(key.Digits);
        Assert.Null(key.Period);
        Assert.Null(key.Skew);
        Assert.False(key.ReplayCheck);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_raises_a_protocol_error_when_the_wire_omits_name()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"generate":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Generate = true, AccountName = "alice" }));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("TOT-003")]
    [Trait("Requirement", "TOT-003")]
    public async Task ValidateCode_raises_a_protocol_error_when_valid_is_not_a_boolean()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"valid":"yes"}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.ValidateCodeAsync("alice", "000000"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task CreateKey_requires_AccountName_when_the_url_does_not_even_parse_as_a_uri()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Totp.CreateKeyAsync("alice", new TotpKeySpec { Url = new SecretString("not a uri at all") }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
    }

    [Fact]
    [Requirement("TOT-001")]
    [Trait("Requirement", "TOT-001")]
    public async Task DeleteKey_sends_a_delete_to_the_key_path()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Totp.DeleteKeyAsync("alice");

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.EndsWith("/v1/totp/keys/alice", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = "s.FAKEtoken0000000000000000",
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        configure?.Invoke(options);
        return new BastionVaultClient(options, EnvironmentSource.None);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
