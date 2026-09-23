using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// 08 — Transit engine assertions not expressible as a single wire-shape fixture: TRS-001's
/// unknown-key-type passthrough, TRS-002's client-side <c>bvault:</c> prefix check and
/// <c>ParseCiphertext</c>, TRS-003's dual bytes-or-base64 argument, TRS-010's two <c>keys</c> wire
/// shapes, TRS-011's boundary, TRS-012's false-vs-raise split, and TRS-013's redaction.
/// </summary>
public sealed class TransitUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    // ---------------------------------------------------------------- TRS-001

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task ReadKey_passes_an_unrecognised_key_type_through_unchanged()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"future-pqc-9000","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Equal("future-pqc-9000", key!.Type);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public void TransitKeyTypes_exposes_every_08_type_as_a_constant()
    {
        Assert.Equal("chacha20-poly1305", TransitKeyTypes.ChaCha20Poly1305);
        Assert.Equal("hmac", TransitKeyTypes.Hmac);
        Assert.Equal("ed25519", TransitKeyTypes.Ed25519);
        Assert.Equal("ml-dsa-44", TransitKeyTypes.MlDsa44);
        Assert.Equal("ml-dsa-65", TransitKeyTypes.MlDsa65);
        Assert.Equal("ml-dsa-87", TransitKeyTypes.MlDsa87);
        Assert.Equal("ml-kem-768", TransitKeyTypes.MlKem768);
    }

    // ---------------------------------------------------------------- TRS-002

    [Fact]
    [Requirement("TRS-002")]
    [Trait("Requirement", "TRS-002")]
    public void ParseCiphertext_reads_the_symmetric_and_pqc_framings()
    {
        TransitParsedCiphertext symmetric = TransitOperations.ParseCiphertext("bvault:v1:aGVsbG8=");
        Assert.Equal(1, symmetric.Version);
        Assert.Null(symmetric.Algo);
        Assert.Equal("hello"u8.ToArray(), symmetric.Bytes.ToArray());

        TransitParsedCiphertext pqc = TransitOperations.ParseCiphertext("bvault:v2:pqc:ml-kem-768:aGVsbG8=");
        Assert.Equal(2, pqc.Version);
        Assert.Equal("ml-kem-768", pqc.Algo);
        Assert.Equal("hello"u8.ToArray(), pqc.Bytes.ToArray());
    }

    [Theory]
    [Requirement("TRS-002")]
    [Trait("Requirement", "TRS-002")]
    [InlineData("vault:v1:aGVsbG8=")]
    [InlineData("bvault:vX:aGVsbG8=")]
    [InlineData("bvault:v0:aGVsbG8=")]
    [InlineData("bvault:v1:not-base64!!")]
    [InlineData("bvault:v1:pqc::aGVsbG8=")]
    public void ParseCiphertext_rejects_malformed_input_as_BV_INPUT_011(string malformed)
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => TransitOperations.ParseCiphertext(malformed));
        Assert.Equal(ErrorCodes.InputInvalidCiphertextFormat, exception.Code);
    }

    [Fact]
    [Requirement("TRS-002")]
    [Trait("Requirement", "TRS-002")]
    public async Task Decrypt_and_Rewrap_refuse_a_non_bvault_ciphertext_client_side_before_sending_a_request()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException decryptFailure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Transit.DecryptAsync("k", "vault:v1:abc"));
        Assert.Equal(ErrorCodes.InputInvalidCiphertextFormat, decryptFailure.Code);

        BastionVaultException rewrapFailure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Transit.RewrapAsync("k", "vault:v1:abc"));
        Assert.Equal(ErrorCodes.InputInvalidCiphertextFormat, rewrapFailure.Code);

        // No request was sent for either: the refusal is client-side (attempts: 0).
        Assert.Empty(transport.Requests);
        Assert.Equal(0, decryptFailure.Attempts);
        Assert.Equal(0, rewrapFailure.Attempts);
    }

    // ---------------------------------------------------------------- TRS-003

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Encrypt_accepts_raw_bytes_and_encodes_them_on_the_wire()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ciphertext":"bvault:v1:xyz","key_version":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitEncryptResult result = await client.Transit.EncryptAsync("k", plaintext: "hello"u8.ToArray());

        Assert.Equal("{\"plaintext\":\"aGVsbG8=\"}", BodyOf(transport.Requests[0]));
        Assert.Equal("bvault:v1:xyz", result.Ciphertext);
        Assert.Equal(1, result.KeyVersion);
    }

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Encrypt_accepts_a_pre_encoded_base64_string_and_sends_it_unchanged()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ciphertext":"bvault:v1:xyz","key_version":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Transit.EncryptAsync("k", plaintextBase64: "aGVsbG8=");

        Assert.Equal("{\"plaintext\":\"aGVsbG8=\"}", BodyOf(transport.Requests[0]));
    }

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Encrypt_refuses_both_a_bytes_and_a_base64_argument_at_once_client_side()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Transit.EncryptAsync("k", plaintext: "hello"u8.ToArray(), plaintextBase64: "aGVsbG8="));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Encrypt_refuses_neither_a_bytes_nor_a_base64_argument_when_required()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Transit.EncryptAsync("k"));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Encrypt_carries_a_derived_key_context_as_base64_and_omits_it_when_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ciphertext":"bvault:v1:xyz","key_version":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Transit.EncryptAsync("k", plaintext: "hi"u8.ToArray(), context: "ctx"u8.ToArray());

        Assert.Equal("{\"plaintext\":\"aGk=\",\"context\":\"Y3R4\"}", BodyOf(transport.Requests[0]));
    }

    // ---------------------------------------------------------------- TRS-010

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_normalises_the_symmetric_keys_shape()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":2,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":"2026-01-01T00:00:00Z","2":"2026-02-01T00:00:00Z"}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Equal(2, key!.Keys.Count);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), key.Keys[1].CreationTime);
        Assert.Null(key.Keys[1].PublicKey);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), key.Keys[2].CreationTime);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_normalises_the_asymmetric_keys_shape()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"ed25519","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":{"public_key":"pk-1","creation_time":"2026-01-01T00:00:00Z"}}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Equal("pk-1", key!.Keys[1].PublicKey);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), key.Keys[1].CreationTime);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_accepts_a_bare_epoch_number_for_the_symmetric_keys_shape()
    {
        // DR-0021 F8a: a measured server sends {"1":1790163936} — a bare Unix-epoch number —
        // instead of the specified ISO-8601 string. The SDK must tolerate both.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":1790163936}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790163936), key!.Keys[1].CreationTime);
        Assert.Null(key.Keys[1].PublicKey);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_accepts_a_bare_epoch_number_for_the_asymmetric_keys_shape()
    {
        // DR-0021 F8a: a measured server sends {"1":{"creation_time":1790163936,"public_key":...}}.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"ed25519","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":{"public_key":"pk-1","creation_time":1790163936}}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Equal("pk-1", key!.Keys[1].PublicKey);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790163936), key.Keys[1].CreationTime);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_raises_a_protocol_mismatch_for_a_creation_time_number_that_does_not_fit_an_epoch()
    {
        // Regression guard for the defect half of DR-0021 F8a: a numeric creation_time that
        // System.Text.Json cannot read as Int64 must still raise BV-PROTOCOL-002, never a raw
        // System.Text.Json exception.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":1.5}}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Transit.ReadKeyAsync("k"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_falls_back_to_the_requested_name_and_an_empty_keys_map_when_the_wire_omits_them()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Equal("k", key!.Name);
        Assert.Empty(key.Keys);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_skips_a_non_numeric_keys_entry()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"not-a-version":"2026-01-01T00:00:00Z"}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Empty(key!.Keys);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_raises_a_protocol_mismatch_for_a_keys_entry_that_is_neither_string_number_nor_object()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":true}}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Transit.ReadKeyAsync("k"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_raises_a_protocol_mismatch_for_a_missing_or_malformed_creation_time()
    {
        FakeTransport missing = new();
        missing.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":""}}}"""));
        BastionVaultClient missingClient = BuildClient(missing);
        BastionVaultException missingException = await Assert.ThrowsAsync<BastionVaultException>(() => missingClient.Transit.ReadKeyAsync("k"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, missingException.Code);

        FakeTransport malformed = new();
        malformed.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":{"public_key":"pk"}}}}"""));
        BastionVaultClient malformedClient = BuildClient(malformed);
        BastionVaultException malformedException = await Assert.ThrowsAsync<BastionVaultException>(() => malformedClient.Transit.ReadKeyAsync("k"));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, malformedException.Code);
    }

    // ---------------------------------------------------------------- TRS-011

    [Fact]
    [Requirement("TRS-011")]
    [Trait("Requirement", "TRS-011")]
    public async Task Random_accepts_exactly_4096_bytes_and_refuses_4097_client_side()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"random_bytes\":\"" + Convert.ToBase64String(new byte[1]) + "\"}}"));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Transit.RandomAsync(4096);
        _ = Assert.Single(transport.Requests);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Transit.RandomAsync(4097));
        Assert.Equal(ErrorCodes.InputOutOfRange, exception.Code);
        Assert.Equal(0, exception.Attempts);
        // The refusal cost no second request.
        _ = Assert.Single(transport.Requests);
    }

    // ---------------------------------------------------------------- TRS-012

    [Fact]
    [Requirement("TRS-012")]
    [Trait("Requirement", "TRS-012")]
    public async Task Verify_returns_false_for_a_logical_mismatch_and_raises_for_a_framing_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"valid":false}}"""));
        BastionVaultClient client = BuildClient(transport);

        bool valid = await client.Transit.VerifyAsync("k", "bvault:v1:sig", input: "hi"u8.ToArray());
        Assert.False(valid);

        FakeTransport failing = new();
        failing.EnqueueResponse(500, body: Json("""{"error":"malformed ciphertext: too short"}"""));
        BastionVaultClient failingClient = BuildClient(failing);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => failingClient.Transit.VerifyAsync("k", "bvault:v1:sig", input: "hi"u8.ToArray()));
        Assert.Equal(ErrorCodes.InputInvalidCiphertextFormat, exception.Code);
    }

    // ---------------------------------------------------------------- TRS-012 (b-1 fix)

    [Theory]
    [Requirement("TRS-012")]
    [Trait("Requirement", "TRS-012")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"valid":null}}""")]
    [InlineData("""{"data":{"valid":"false"}}""")]
    public async Task Verify_raises_a_protocol_mismatch_when_valid_is_absent_or_not_a_boolean(string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Transit.VerifyAsync("k", "bvault:v1:sig", input: "hi"u8.ToArray()));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal("valid", exception.Details["expectedField"]);
    }

    [Theory]
    [Requirement("TRS-012")]
    [Trait("Requirement", "TRS-012")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"valid":null}}""")]
    [InlineData("""{"data":{"valid":"false"}}""")]
    public async Task VerifyHmac_raises_a_protocol_mismatch_when_valid_is_absent_or_not_a_boolean(string body)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Transit.VerifyHmacAsync("k", "bvault:v1:mac", input: "hi"u8.ToArray()));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal("valid", exception.Details["expectedField"]);
    }

    // ---------------------------------------------------------------- b-3 fix: malformed base64 from the server

    [Theory]
    [Requirement("TRS-013")]
    [Trait("Requirement", "TRS-013")]
    [MemberData(nameof(MalformedBase64ResponseFields))]
    public async Task A_malformed_base64_field_from_the_server_raises_a_protocol_mismatch_instead_of_a_raw_FormatException(
        string _, string body, string expectedField, Func<BastionVaultClient, Task> call)
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(body));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => call(client));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal(expectedField, exception.Details["expectedField"]);
    }

    public static TheoryData<string, string, string, Func<BastionVaultClient, Task>> MalformedBase64ResponseFields()
    {
        TheoryData<string, string, string, Func<BastionVaultClient, Task>> data = [];
        void Add(string name, string body, string field, Func<BastionVaultClient, Task> call) => data.Add(name, body, field, call);

        Add("Decrypt.plaintext", """{"data":{"plaintext":"not-base64!!"}}""", "plaintext",
            c => c.Transit.DecryptAsync("k", "bvault:v1:x"));
        Add("GenerateDataKey.plaintext", """{"data":{"ciphertext":"bvault:v1:wrapped","key_version":1,"plaintext":"not-base64!!"}}""", "plaintext",
            c => c.Transit.GenerateDataKeyAsync("k", TransitDataKeyMode.Plaintext));
        Add("UnwrapDataKey.plaintext", """{"data":{"plaintext":"not-base64!!"}}""", "plaintext",
            c => c.Transit.UnwrapDataKeyAsync("k", "bvault:v1:wrapped"));
        Add("Random.random_bytes", """{"data":{"random_bytes":"not-base64!!"}}""", "random_bytes",
            c => c.Transit.RandomAsync());
        Add("Hash.sum", """{"data":{"sum":"not-base64!!"}}""", "sum",
            c => c.Transit.HashAsync(input: "hi"u8.ToArray()));

        return data;
    }

    // ---------------------------------------------------------------- b-2 coverage: mount guards

    [Theory]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    [MemberData(nameof(MountGuardedMembers))]
    public async Task Every_member_taking_a_mount_refuses_a_null_or_empty_mount_before_sending_a_request(
        string name, Func<BastionVaultClient, Task> call)
    {
        Assert.NotEmpty(name);
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => call(client));

        Assert.Empty(transport.Requests);
    }

    public static TheoryData<string, Func<BastionVaultClient, Task>> MountGuardedMembers()
    {
        JsonElement byokBody = JsonDocument.Parse("""{"a":1}""").RootElement;
        TheoryData<string, Func<BastionVaultClient, Task>> data = [];
        void Add(string name, Func<BastionVaultClient, Task> call) => data.Add(name, call);

        Add("ListKeys", c => c.Transit.ListKeysAsync(mount: ""));
        Add("CreateKey", c => c.Transit.CreateKeyAsync("k", mount: ""));
        Add("ReadKey", c => c.Transit.ReadKeyAsync("k", mount: ""));
        Add("DeleteKey", c => c.Transit.DeleteKeyAsync("k", mount: ""));
        Add("RotateKey", c => c.Transit.RotateKeyAsync("k", mount: ""));
        Add("ConfigureKey", c => c.Transit.ConfigureKeyAsync("k", new TransitKeyConfig(), mount: ""));
        Add("TrimKey", c => c.Transit.TrimKeyAsync("k", mount: ""));
        Add("Encrypt", c => c.Transit.EncryptAsync("k", plaintext: "hi"u8.ToArray(), mount: ""));
        Add("Decrypt", c => c.Transit.DecryptAsync("k", "bvault:v1:x", mount: ""));
        Add("Rewrap", c => c.Transit.RewrapAsync("k", "bvault:v1:x", mount: ""));
        Add("Sign", c => c.Transit.SignAsync("k", input: "hi"u8.ToArray(), mount: ""));
        Add("Verify", c => c.Transit.VerifyAsync("k", "bvault:v1:sig", input: "hi"u8.ToArray(), mount: ""));
        Add("Hmac", c => c.Transit.HmacAsync("k", input: "hi"u8.ToArray(), mount: ""));
        Add("VerifyHmac", c => c.Transit.VerifyHmacAsync("k", "bvault:v1:mac", input: "hi"u8.ToArray(), mount: ""));
        Add("GenerateDataKey", c => c.Transit.GenerateDataKeyAsync("k", mount: ""));
        Add("UnwrapDataKey", c => c.Transit.UnwrapDataKeyAsync("k", "bvault:v1:x", mount: ""));
        Add("Random", c => c.Transit.RandomAsync(mount: ""));
        Add("Hash", c => c.Transit.HashAsync(input: "hi"u8.ToArray(), mount: ""));
        Add("Byok.WrappingKey", c => c.Transit.Byok.WrappingKeyAsync(mount: ""));
        Add("Byok.ImportKey", c => c.Transit.Byok.ImportKeyAsync("k", KvWireMap(byokBody), mount: ""));
        Add("Byok.ImportVersion", c => c.Transit.Byok.ImportVersionAsync("k", KvWireMap(byokBody), mount: ""));

        return data;
    }

    // ---------------------------------------------------------------- b-2 coverage: missing data envelope

    [Theory]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    [MemberData(nameof(NullEnvelopeMembers))]
    public async Task Every_crypto_endpoint_raises_a_protocol_mismatch_when_the_response_has_no_data_envelope(
        string _, Func<BastionVaultClient, Task> call, string expectedField)
    {
        // `{"data":null}`: the wire carries the `data` key so `Shape` takes the shapeA path and
        // evaluates a non-object `data` to a null `Response.Data` — the `response?.Data ?? throw`
        // guard's own null branch.
        FakeTransport envelopeMissing = new();
        envelopeMissing.EnqueueResponse(200, body: Json("""{"data":null}"""));
        BastionVaultClient envelopeMissingClient = BuildClient(envelopeMissing);
        BastionVaultException envelopeException = await Assert.ThrowsAsync<BastionVaultException>(() => call(envelopeMissingClient));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, envelopeException.Code);
        Assert.Equal(expectedField, envelopeException.Details["expectedField"]);

        // `{"data":{}}`: a present, empty envelope. `response?.Data` is non-null here, so this
        // exercises the second, field-level `KvWire.ReadString(wire, field) ?? throw` instead.
        FakeTransport fieldMissing = new();
        fieldMissing.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient fieldMissingClient = BuildClient(fieldMissing);
        BastionVaultException fieldException = await Assert.ThrowsAsync<BastionVaultException>(() => call(fieldMissingClient));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, fieldException.Code);
        Assert.Equal(expectedField, fieldException.Details["expectedField"]);

        // `204` (empty body): `RequestExecutor` sets `IsEmpty` for a 204 or a 200 with an empty
        // body independently of `treatNotFoundEmptyAsAbsent`, so `Shape` returns a null
        // `Response` — the `response is null` half of the `response?.Data ?? throw` guard that
        // the two shapes above do not reach.
        FakeTransport emptyBody = new();
        emptyBody.EnqueueResponse(204);
        BastionVaultClient emptyBodyClient = BuildClient(emptyBody);
        BastionVaultException emptyBodyException = await Assert.ThrowsAsync<BastionVaultException>(() => call(emptyBodyClient));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, emptyBodyException.Code);
        Assert.Equal(expectedField, emptyBodyException.Details["expectedField"]);
    }

    public static TheoryData<string, Func<BastionVaultClient, Task>, string> NullEnvelopeMembers()
    {
        TheoryData<string, Func<BastionVaultClient, Task>, string> data = [];
        void Add(string name, Func<BastionVaultClient, Task> call, string field) => data.Add(name, call, field);

        Add("Encrypt", c => c.Transit.EncryptAsync("k", plaintext: "hi"u8.ToArray()), "ciphertext");
        Add("Decrypt", c => c.Transit.DecryptAsync("k", "bvault:v1:x"), "plaintext");
        Add("Rewrap", c => c.Transit.RewrapAsync("k", "bvault:v1:x"), "ciphertext");
        Add("Sign", c => c.Transit.SignAsync("k", input: "hi"u8.ToArray()), "signature");
        Add("Hmac", c => c.Transit.HmacAsync("k", input: "hi"u8.ToArray()), "hmac");
        Add("GenerateDataKey", c => c.Transit.GenerateDataKeyAsync("k"), "ciphertext");
        Add("UnwrapDataKey", c => c.Transit.UnwrapDataKeyAsync("k", "bvault:v1:x"), "plaintext");
        Add("Random", c => c.Transit.RandomAsync(), "random_bytes");
        Add("Hash", c => c.Transit.HashAsync(input: "hi"u8.ToArray()), "sum");

        return data;
    }

    // ---------------------------------------------------------------- b-2 coverage: TransitWire branches

    [Theory]
    [Requirement("TRS-002")]
    [Trait("Requirement", "TRS-002")]
    [InlineData("bvault:v:aGVsbG8=")]
    [InlineData("bvault:v1:xyz:ml-kem-768:aGVsbG8=")]
    public void ParseCiphertext_rejects_a_short_version_segment_and_a_non_pqc_five_part_framing(string malformed)
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => TransitOperations.ParseCiphertext(malformed));
        Assert.Equal(ErrorCodes.InputInvalidCiphertextFormat, exception.Code);
    }

    [Fact]
    [Requirement("TRS-010")]
    [Trait("Requirement", "TRS-010")]
    public async Task ReadKey_leaves_public_key_null_when_the_asymmetric_entry_omits_it_and_falls_back_the_type_when_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{"1":{"creation_time":"2026-01-01T00:00:00Z"}}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("k");

        Assert.Null(key!.Keys[1].PublicKey);
        Assert.Equal(TransitKeyTypes.ChaCha20Poly1305, key.Type);
    }

    // ---------------------------------------------------------------- b-2 coverage: SecretBytes asymmetric null

    [Fact]
    [Requirement("TRS-013")]
    [Trait("Requirement", "TRS-013")]
    public void SecretBytes_equality_is_false_when_only_one_side_holds_no_value()
    {
        SecretBytes withValue = new([1, 2, 3]);

        Assert.False(withValue.Equals(SecretBytes.Empty));
        Assert.False(SecretBytes.Empty.Equals(withValue));
    }

    // ---------------------------------------------------------------- TRS-013

    [Fact]
    [Requirement("TRS-013")]
    [Trait("Requirement", "TRS-013")]
    public async Task Decrypt_returns_plaintext_in_a_redacting_type_and_never_logs_or_observes_it()
    {
        const string plaintext = "top-secret-value";
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        CapturingClientLogger logger = new();
        CapturingRequestObserver observer = new();
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"plaintext\":\"" + encoded + "\"}}"));
        BastionVaultClient client = BuildClient(transport, options =>
        {
            options.Logger = logger;
            options.Observer = observer;
        });

        SecretBytes revealed = await client.Transit.DecryptAsync("k", "bvault:v1:xyz");

        Assert.Equal("[REDACTED]", revealed.ToString());
        Assert.Equal(plaintext, Encoding.UTF8.GetString(revealed.Reveal()!));

        string[] surfaced = [.. logger.Lines, .. observer.Events.Select(captured => captured.ToString())];
        Assert.NotEmpty(observer.Events);
        Assert.All(surfaced, line => Assert.DoesNotContain(plaintext, line, StringComparison.Ordinal));
        Assert.All(surfaced, line => Assert.DoesNotContain(encoded, line, StringComparison.Ordinal));
    }

    [Fact]
    [Requirement("TRS-013")]
    [Trait("Requirement", "TRS-013")]
    public async Task GenerateDataKey_returns_plaintext_only_in_plaintext_mode_and_holds_it_redacted()
    {
        FakeTransport wrapped = new();
        wrapped.EnqueueResponse(200, body: Json("""{"data":{"ciphertext":"bvault:v1:wrapped","key_version":1}}"""));
        BastionVaultClient wrappedClient = BuildClient(wrapped);

        TransitDataKeyResult wrappedResult = await wrappedClient.Transit.GenerateDataKeyAsync("k", TransitDataKeyMode.Wrapped);
        Assert.Null(wrappedResult.Plaintext);
        Assert.Contains("/datakey/wrapped/k", wrapped.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport plaintextTransport = new();
        plaintextTransport.EnqueueResponse(200, body: Json(
            """{"data":{"ciphertext":"bvault:v1:wrapped","key_version":1,"plaintext":"cGxhaW50ZXh0"}}"""));
        BastionVaultClient plaintextClient = BuildClient(plaintextTransport);

        TransitDataKeyResult plaintextResult = await plaintextClient.Transit.GenerateDataKeyAsync("k", TransitDataKeyMode.Plaintext);
        Assert.Contains("/datakey/plaintext/k", plaintextTransport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", plaintextResult.Plaintext!.ToString());
        Assert.Equal("plaintext", Encoding.UTF8.GetString(plaintextResult.Plaintext.Reveal()!));
    }

    [Fact]
    [Requirement("TRS-013")]
    [Trait("Requirement", "TRS-013")]
    public async Task GenerateDataKey_sends_a_derived_key_context_when_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ciphertext":"bvault:v1:wrapped","key_version":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Transit.GenerateDataKeyAsync("k", TransitDataKeyMode.Wrapped, context: "ctx"u8.ToArray());

        Assert.Equal("{\"context\":\"Y3R4\"}", BodyOf(transport.Requests[0]));
    }

    [Fact]
    [Requirement("TRS-013")]
    [Trait("Requirement", "TRS-013")]
    public async Task UnwrapDataKey_returns_the_plaintext_in_a_redacting_type()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"plaintext":"cGxhaW50ZXh0"}}"""));
        BastionVaultClient client = BuildClient(transport);

        SecretBytes revealed = await client.Transit.UnwrapDataKeyAsync("k", "bvault:v1:wrapped");

        Assert.Equal("[REDACTED]", revealed.ToString());
        Assert.Equal("plaintext", Encoding.UTF8.GetString(revealed.Reveal()!));
        Assert.Contains("/datakey/unwrap/k", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- key lifecycle

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task ListKeys_reads_the_wire_keys_array()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["a","b"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<string> keys = await client.Transit.ListKeysAsync();

        Assert.Equal(["a", "b"], keys);
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.Contains("/transit/keys/", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task CreateKey_sends_an_empty_body_when_no_options_are_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey key = await client.Transit.CreateKeyAsync("k");

        Assert.Equal("{}", BodyOf(transport.Requests[0]));
        Assert.Equal("k", key.Name);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task CreateKey_sends_every_option_field_when_given()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":true,"exportable":true,"derived":true,"convergent_encryption":true,"keys":{}}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Transit.CreateKeyAsync("k", new TransitKeyOptions
        {
            KeyType = TransitKeyTypes.ChaCha20Poly1305,
            Exportable = true,
            DeletionAllowed = true,
            Derived = true,
            ConvergentEncryption = true,
        });

        Assert.Equal(
            "{\"key_type\":\"chacha20-poly1305\",\"exportable\":true,\"deletion_allowed\":true,\"derived\":true,\"convergent_encryption\":true}",
            BodyOf(transport.Requests[0]));
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task ReadKey_returns_null_for_a_404_with_an_empty_body()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        TransitKey? key = await client.Transit.ReadKeyAsync("missing");

        Assert.Null(key);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task DeleteKey_sends_DELETE_to_the_key_route()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Transit.DeleteKeyAsync("k");

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.Contains("/transit/keys/k", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task RotateKey_posts_to_the_rotate_route_and_returns_the_refreshed_metadata()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":2,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey key = await client.Transit.RotateKeyAsync("k");

        Assert.Equal(2, key.LatestVersion);
        Assert.Contains("/transit/keys/k/rotate", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task ConfigureKey_sends_an_empty_body_when_every_field_is_left_alone()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":1,"min_available_version":1,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{}}}"""));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Transit.ConfigureKeyAsync("k", new TransitKeyConfig());

        Assert.Equal("{}", BodyOf(transport.Requests[0]));
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task ConfigureKey_sends_every_field_that_is_set()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":1,"min_decryption_version":2,"min_available_version":1,"deletion_allowed":true,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{}}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitKey key = await client.Transit.ConfigureKeyAsync("k", new TransitKeyConfig
        {
            MinDecryptionVersion = 2,
            MinAvailableVersion = 1,
            DeletionAllowed = true,
        });

        Assert.Equal(
            "{\"min_decryption_version\":2,\"min_available_version\":1,\"deletion_allowed\":true}",
            BodyOf(transport.Requests[0]));
        Assert.Equal(2, key.MinDecryptionVersion);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task TrimKey_returns_the_dropped_versions_alongside_the_refreshed_metadata()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"name":"k","type":"chacha20-poly1305","latest_version":3,"min_decryption_version":3,"min_available_version":3,"deletion_allowed":false,"exportable":false,"derived":false,"convergent_encryption":false,"keys":{},"dropped_versions":[1,2]}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitTrimResult result = await client.Transit.TrimKeyAsync("k");

        Assert.Equal([1, 2], result.DroppedVersions);
        Assert.Equal(3, result.Key.LatestVersion);
        Assert.Contains("/transit/keys/k/trim", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TRS-002")]
    [Trait("Requirement", "TRS-002")]
    public async Task Rewrap_posts_the_ciphertext_and_returns_the_rewrapped_result()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"ciphertext":"bvault:v2:new","key_version":2}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitEncryptResult result = await client.Transit.RewrapAsync("k", "bvault:v1:old", context: "ctx"u8.ToArray());

        Assert.Equal("bvault:v2:new", result.Ciphertext);
        Assert.Equal(2, result.KeyVersion);
        Assert.Equal("{\"ciphertext\":\"bvault:v1:old\",\"context\":\"Y3R4\"}", BodyOf(transport.Requests[0]));
        Assert.Contains("/transit/rewrap/k", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- remaining crypto endpoints

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Sign_posts_the_input_and_returns_the_signature()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"signature":"bvault:v1:sig","key_version":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitSignResult result = await client.Transit.SignAsync("k", input: "hi"u8.ToArray());

        Assert.Equal("bvault:v1:sig", result.Signature);
        Assert.Contains("/transit/sign/k", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Hmac_posts_the_input_and_algorithm_and_returns_the_hmac()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"hmac":"bvault:v1:mac","key_version":1}}"""));
        BastionVaultClient client = BuildClient(transport);

        TransitHmacResult result = await client.Transit.HmacAsync("k", input: "hi"u8.ToArray(), algorithm: TransitHashAlgorithms.Sha2512);

        Assert.Equal("bvault:v1:mac", result.Hmac);
        Assert.Equal("{\"input\":\"aGk=\",\"algorithm\":\"sha2-512\"}", BodyOf(transport.Requests[0]));
    }

    [Fact]
    [Requirement("TRS-012")]
    [Trait("Requirement", "TRS-012")]
    public async Task VerifyHmac_returns_true_for_a_match_and_raises_for_a_framing_error()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"valid":true}}"""));
        BastionVaultClient client = BuildClient(transport);

        bool valid = await client.Transit.VerifyHmacAsync("k", "bvault:v1:mac", input: "hi"u8.ToArray());
        Assert.True(valid);
        Assert.Contains("/transit/verify/k/hmac", transport.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport failing = new();
        failing.EnqueueResponse(500, body: Json("""{"error":"HMAC framing must not carry a pqc tag"}"""));
        BastionVaultClient failingClient = BuildClient(failing);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => failingClient.Transit.VerifyHmacAsync("k", "bvault:v1:mac", input: "hi"u8.ToArray()));
        Assert.Equal(ErrorCodes.TransitAlgorithmMismatch, exception.Code);
    }

    [Fact]
    [Requirement("TRS-003")]
    [Trait("Requirement", "TRS-003")]
    public async Task Hash_posts_the_input_and_algorithm_and_returns_the_sum()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            "{\"data\":{\"sum\":\"" + Convert.ToBase64String("digest"u8.ToArray()) + "\"}}"));
        BastionVaultClient client = BuildClient(transport);

        byte[] sum = await client.Transit.HashAsync(input: "hi"u8.ToArray());

        Assert.Equal("digest"u8.ToArray(), sum);
        Assert.Equal("{\"input\":\"aGk=\",\"algorithm\":\"sha2-256\"}", BodyOf(transport.Requests[0]));
    }

    // ---------------------------------------------------------------- Feature-gated BYOK

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task Byok_surfaces_BV_SERVER_004_unchanged_when_the_feature_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(500, body: Json("""{"error":"Logical backend path not supported."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Transit.Byok.WrappingKeyAsync());

        Assert.Equal(ErrorCodes.ServerUnsupportedByServer, exception.Code);
    }

    [Fact]
    [Requirement("TRS-001")]
    [Trait("Requirement", "TRS-001")]
    public async Task Byok_wrapping_key_and_import_routes_bind_generically_when_the_feature_is_present()
    {
        FakeTransport wrappingKey = new();
        wrappingKey.EnqueueResponse(200, body: Json("""{"data":{"wrapping_key":"pem-data"}}"""));
        BastionVaultClient wrappingKeyClient = BuildClient(wrappingKey);

        IReadOnlyDictionary<string, JsonElement>? wrapped = await wrappingKeyClient.Transit.Byok.WrappingKeyAsync();
        Assert.Equal("pem-data", wrapped!["wrapping_key"].GetString());

        FakeTransport importKey = new();
        importKey.EnqueueResponse(200, body: Json("""{"data":{"imported":true}}"""));
        BastionVaultClient importKeyClient = BuildClient(importKey);

        using JsonDocument importBody = JsonDocument.Parse("""{"ciphertext":"blob"}""");
        IReadOnlyDictionary<string, JsonElement>? importResult = await importKeyClient.Transit.Byok.ImportKeyAsync(
            "k", KvWireMap(importBody.RootElement));
        Assert.True(importResult!["imported"].GetBoolean());
        Assert.Equal("{\"ciphertext\":\"blob\"}", BodyOf(importKey.Requests[0]));
        Assert.Contains("/transit/keys/k/import", importKey.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);

        FakeTransport importVersion = new();
        importVersion.EnqueueResponse(200, body: Json("""{"data":{"imported":true}}"""));
        BastionVaultClient importVersionClient = BuildClient(importVersion);

        using JsonDocument versionBody = JsonDocument.Parse("""{"ciphertext":"blob2"}""");
        _ = await importVersionClient.Transit.Byok.ImportVersionAsync("k", KvWireMap(versionBody.RootElement));
        Assert.Contains("/transit/keys/k/import_version", importVersion.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- SecretBytes (TRS-013)

    [Fact]
    [Requirement("TRS-013")]
    [Trait("Requirement", "TRS-013")]
    public void SecretBytes_equality_hash_and_default_state_behave_like_SecretString()
    {
        SecretBytes a = new([1, 2, 3]);
        SecretBytes b = new([1, 2, 3]);
        SecretBytes c = new([9]);

        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals((SecretBytes?)null));
        Assert.False(a.Equals((object?)null));
        Assert.False(a.Equals("not-a-secret-bytes"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.True(a.HasValue);
        Assert.False(SecretBytes.Empty.HasValue);
        Assert.True(SecretBytes.Empty.Equals(new SecretBytes(null)));
        Assert.Null(SecretBytes.Empty.Reveal());
        Assert.Equal("[REDACTED]", a.ToString());
        Assert.Equal(0, SecretBytes.Empty.GetHashCode());
    }

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyDictionary<string, JsonElement> KvWireMap(JsonElement element)
    {
        return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    private static string BodyOf(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static BastionVaultClient BuildClient(ITransport transport, Action<BastionVaultClientOptions>? configure = null)
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
}
