using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M4a's assertions not expressible as a single wire-shape fixture (DR-0009): the path helpers
/// (KV2-030), the client-side refusals no fixture carries, <c>GetSecret</c>'s three KV2-006 arms,
/// the environment write paths (KV2-021), the KV2-022 fail-fast on a write, KV2-023's enrichment
/// note, KV2-024's non-validation, and the per-segment path encoding that keeps a hostile
/// caller-supplied <c>path</c> on its own route.
/// </summary>
public sealed class KvUnitTests
{
    private const string Address = "https://vault.example.com:8200";

    // ---------------------------------------------------------------- KV2-030: path helpers

    [Theory]
    [Requirement("KV2-030")]
    [Trait("Requirement", "KV2-030")]
    [InlineData("app/db", "secret/data/app/db")]
    [InlineData("/app/db", "secret/data/app/db")]
    [InlineData("app/db/", "secret/data/app/db")]
    [InlineData("///app/db///", "secret/data/app/db")]
    public void DataPath_strips_leading_and_trailing_slashes(string path, string expected)
    {
        Assert.Equal(expected, KvV2Operations.DataPath(path));
    }

    [Fact]
    [Requirement("KV2-030")]
    [Trait("Requirement", "KV2-030")]
    public void The_four_path_helpers_return_the_full_logical_path_for_the_group_they_name()
    {
        Assert.Equal("kv/data/app/db", KvV2Operations.DataPath("app/db", "kv"));
        Assert.Equal("kv/metadata/app/db", KvV2Operations.MetadataPath("app/db", "kv"));
        Assert.Equal("kv/destroy/app/db", KvV2Operations.DestroyPath("app/db", "kv"));
        Assert.Equal("kv/undelete/app/db", KvV2Operations.UndeletePath("app/db", "kv"));

        // The mount's own slashes are stripped too, so a helper's output is always a single
        // well-formed logical path whichever way the caller spells the mount.
        Assert.Equal("kv/data/app", KvV2Operations.DataPath("app", "/kv/"));
    }

    [Fact]
    [Requirement("KV2-030")]
    [Trait("Requirement", "KV2-030")]
    public void A_path_helper_refuses_a_null_path_and_an_empty_mount()
    {
        _ = Assert.Throws<ArgumentNullException>(() => KvV2Operations.MetadataPath(null!));
        _ = Assert.Throws<ArgumentException>(() => KvV2Operations.DestroyPath("app", string.Empty));
    }

    // ------------------------------------------------- path safety: per-segment encoding

    [Fact]
    [Requirement("KV2-030")]
    [Trait("Requirement", "KV2-030")]
    public async Task A_hostile_path_cannot_move_the_request_to_another_route()
    {
        // M2b found this exact defect in Userpass login: an unencoded `/` or `?` in a
        // caller-supplied path parameter reached a different, unauthenticated route. A KV path is
        // caller-supplied too, so the separators the caller did not write must not survive as
        // structure — the `?` must not start a query and the encoded form must stay under
        // `secret/data/`.
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Kv.V2.ReadSecretAsync("app/db?version=9#frag");

        Assert.Equal(
            "https://vault.example.com:8200/v1/secret/data/app/db%3Fversion=9%23frag",
            transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("KV2-008")]
    [Trait("Requirement", "KV2-008")]
    public async Task A_dot_dot_segment_is_refused_on_every_kv_path_and_prefix()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        // KV2-008 states it for a list prefix; the `..` segment cannot be neutralised by
        // percent-encoding (`.` is unreserved in TRN-020's set), so it is refused on every
        // caller-supplied path as well.
        foreach (Func<Task> call in new Func<Task>[]
        {
            () => client.Kv.V2.ListAsync("app/../../auth"),
            () => client.Kv.V1.ListAsync("app/.."),
            () => client.Kv.V2.ReadSecretAsync("app/../../auth/token/lookup"),
            () => client.Kv.V2.WriteSecretAsync("app/..", Data("a", "1")),
            () => client.Kv.V2.SoftDeleteAsync("app/.."),
            () => client.Kv.V2.UndeleteAsync("app/..", [1]),
            () => client.Kv.V2.DestroyAsync("app/..", [1]),
            () => client.Kv.V2.ReadMetadataAsync("app/.."),
            () => client.Kv.V2.DeleteMetadataAsync("app/.."),
            () => client.Kv.V2.PatchEnvironmentAsync("app/..", "prod", Data("a", "1")),
            () => client.Kv.V2.WriteAllEnvironmentsAsync("app/..", Data("a", "1"), Envs("prod", "a", "2")),
            () => client.Kv.V1.ReadAsync("app/.."),
            () => client.Kv.V1.WriteAsync("app/..", Data("a", "1")),
            () => client.Kv.V1.DeleteAsync("app/.."),
            // RF-1: the same guard, applied to `mount` — equally caller-supplied and equally
            // multi-segment — on an operation and on a KV2-030 path helper.
            () => client.Kv.V1.ReadAsync("db", mount: "secret/../auth/token/lookup-self"),
            () => { _ = KvV2Operations.DataPath("db", mount: "secret/../auth"); return Task.CompletedTask; },
        })
        {
            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
            Assert.Equal(0, exception.Attempts);
        }

        Assert.Empty(transportRequests(client));

        static IReadOnlyList<TransportRequest> transportRequests(BastionVaultClient client)
        {
            return ((FakeTransport)client.Transport!).Requests;
        }
    }

    [Fact]
    [Requirement("KV2-008")]
    [Trait("Requirement", "KV2-008")]
    public async Task A_secret_named_with_a_double_dot_inside_a_segment_is_still_addressable()
    {
        // KV2-008's `Contains` is applied literally to a *prefix*, but `release..candidate` is a
        // legitimate key and only a whole `..` segment can change the route.
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Kv.V2.ReadSecretAsync("app/release..candidate");

        Assert.Equal(
            "https://vault.example.com:8200/v1/secret/data/app/release..candidate",
            transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("KV2-008")]
    [Trait("Requirement", "KV2-008")]
    public async Task List_of_an_empty_prefix_targets_the_group_root_with_one_trailing_slash()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        Assert.Empty(await client.Kv.V2.ListAsync());
        Assert.Equal("https://vault.example.com:8200/v1/secret/metadata/", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("KV-002")]
    [Trait("Requirement", "KV-002")]
    public async Task V1_list_of_an_empty_prefix_targets_the_mount_root_with_one_trailing_slash()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        Assert.Empty(await client.Kv.V1.ListAsync(mount: "kv"));
        Assert.Equal("https://vault.example.com:8200/v1/kv/", transport.Requests[0].Uri.ToString());
    }

    // ---------------------------------------------------------------- KV1-002, KV1-004

    [Fact]
    [Requirement("KV1-002")]
    [Trait("Requirement", "KV1-002")]
    public async Task V1_read_of_a_missing_path_is_null_and_V1_get_raises_BV_KV_001()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        Assert.Null(await client.Kv.V1.ReadAsync("app", "kv"));

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V1.GetAsync("app", "kv"));
        Assert.Equal(ErrorCodes.KvSecretNotFound, exception.Code);
        Assert.Equal("kv/app", exception.Path);
    }

    [Fact]
    [Requirement("KV1-003")]
    [Trait("Requirement", "KV1-003")]
    public async Task V1_read_applies_the_specified_3600_second_lease_default_when_the_wire_omits_it()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"k":"v"}}"""));
        BastionVaultClient client = BuildClient(transport);

        KvV1Secret secret = await client.Kv.V1.GetAsync("app", "kv");

        Assert.Equal(TimeSpan.FromSeconds(3600), secret.LeaseDuration);
        Assert.False(secret.Renewable);
        Assert.Equal("v", secret.Data["k"].GetString());
    }

    [Fact]
    [Requirement("KV1-003")]
    [Trait("Requirement", "KV1-003")]
    public async Task V1_read_of_a_non_object_body_yields_an_empty_map_rather_than_null()
    {
        // TRN-043 keeps the body reachable whatever its shape, and KV1-003 declares `Data` as a
        // non-optional map, so a body the envelope cannot project onto one yields an empty map —
        // not a null the caller would have to guard, and not a throw the requirement does not name.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("[1,2]"));
        BastionVaultClient client = BuildClient(transport);

        KvV1Secret secret = await client.Kv.V1.GetAsync("app", "kv");

        Assert.Empty(secret.Data);
        Assert.Equal(TimeSpan.FromSeconds(3600), secret.LeaseDuration);
    }

    [Fact]
    [Requirement("KV1-001")]
    [Trait("Requirement", "KV1-001")]
    public async Task V1_write_merges_the_ttl_into_the_body_as_a_Go_style_duration()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Kv.V1.WriteAsync("app", Data("username", "admin"), "kv", TimeSpan.FromMinutes(90));

        Assert.Equal("""{"username":"admin","ttl":"1h30m"}""", Body(transport.Requests[0]));
        Assert.Equal("https://vault.example.com:8200/v1/kv/app", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("KV1-001")]
    [Trait("Requirement", "KV1-001")]
    public async Task V1_write_without_a_ttl_sends_the_data_verbatim_and_no_ttl_field()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Kv.V1.WriteAsync("app", Data("username", "admin"), "kv");

        Assert.Equal("""{"username":"admin"}""", Body(transport.Requests[0]));
    }

    [Fact]
    [Requirement("KV1-001")]
    [Trait("Requirement", "KV1-001")]
    public async Task V1_write_refuses_a_negative_ttl_client_side()
    {
        // D-M4-13: section 07 is silent on a negative `ttl`, `GoDuration.Format` would happily
        // emit "-1h", and a negative lease has no meaning the server defines.
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V1.WriteAsync("app", Data("username", "admin"), "kv", TimeSpan.FromHours(-1)));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Equal("ttl", exception.Details["argument"]);
        Assert.Equal(0, exception.Attempts);
        Assert.Empty(((FakeTransport)client.Transport!).Requests);
    }

    [Fact]
    [Requirement("KV-002")]
    [Trait("Requirement", "KV-002")]
    public async Task V1_delete_and_list_use_the_mount_relative_route()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":["db"]}}"""));
        BastionVaultClient client = BuildClient(transport);

        await client.Kv.V1.DeleteAsync("app", "kv");
        Assert.Equal(["db"], await client.Kv.V1.ListAsync("app", "kv"));

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.Equal("https://vault.example.com:8200/v1/kv/app", transport.Requests[0].Uri.ToString());
        Assert.Equal("LIST", transport.Requests[1].Method);
        Assert.Equal("https://vault.example.com:8200/v1/kv/app/", transport.Requests[1].Uri.ToString());
    }

    [Fact]
    [Requirement("KV1-004")]
    [Trait("Requirement", "KV1-004")]
    public void No_v1_operation_offers_an_env_parameter()
    {
        // KV1-004 is an absence, so the test is a reflective one: the requirement is satisfied by
        // there being no such parameter, and only reflection can assert the absence.
        IEnumerable<string> parameterNames = typeof(KvV1Operations)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name!);

        Assert.DoesNotContain("env", parameterNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("envs", parameterNames, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- KV2-006: GetSecret's arms

    [Fact]
    [Requirement("KV2-006")]
    [Trait("Requirement", "KV2-006")]
    public async Task GetSecret_with_no_env_raises_BV_KV_001_and_performs_no_metadata_read()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.GetSecretAsync("app/db"));

        Assert.Equal(ErrorCodes.KvSecretNotFound, exception.Code);
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("KV2-006")]
    [Trait("Requirement", "KV2-006")]
    public async Task GetSecret_with_env_raises_BV_KV_006_when_the_metadata_read_shows_the_secret_exists()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(200, body: Json(MetadataBody));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.GetSecretAsync("app/db", env: "dev"));

        Assert.Equal(ErrorCodes.KvEnvironmentNotDeclared, exception.Code);
        Assert.Equal("dev", exception.Details["env"]);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal("https://vault.example.com:8200/v1/secret/metadata/app/db", transport.Requests[1].Uri.ToString());
    }

    [Fact]
    [Requirement("KV2-006")]
    [Trait("Requirement", "KV2-006")]
    public async Task GetSecret_with_env_falls_back_to_BV_KV_001_when_the_secret_does_not_exist_either()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.GetSecretAsync("app/db", env: "dev"));

        Assert.Equal(ErrorCodes.KvSecretNotFound, exception.Code);
    }

    [Fact]
    [Requirement("KV2-006")]
    [Trait("Requirement", "KV2-006")]
    public async Task GetSecret_with_env_falls_back_to_BV_KV_001_when_the_metadata_read_is_not_permitted()
    {
        // "if permitted", read as written: a 403 leaves the SDK unable to tell "no such secret"
        // from "no such environment", so it reports the one it can justify rather than guessing.
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.GetSecretAsync("app/db", env: "dev"));

        Assert.Equal(ErrorCodes.KvSecretNotFound, exception.Code);
    }

    [Fact]
    [Requirement("KV2-004")]
    [Trait("Requirement", "KV2-004")]
    public async Task GetSecret_raises_BV_KV_007_for_a_soft_deleted_version_that_ReadSecret_returns_as_state()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(SoftDeletedBody));
        transport.EnqueueResponse(200, body: Json(SoftDeletedBody));
        BastionVaultClient client = BuildClient(transport);

        KvV2Secret? read = await client.Kv.V2.ReadSecretAsync("app/db");
        Assert.Equal(KvV2SecretState.SoftDeleted, read!.State);
        Assert.Null(read.Data);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.GetSecretAsync("app/db"));
        Assert.Equal(ErrorCodes.KvVersionSoftDeleted, exception.Code);
        Assert.Equal(2, exception.Details["version"]);
    }

    [Fact]
    [Requirement("KV2-004")]
    [Trait("Requirement", "KV2-004")]
    public async Task A_version_with_no_data_and_no_deletion_time_is_Live_not_SoftDeleted()
    {
        // KV2-004's rule is a conjunction, so only one of its two conditions holding must not
        // produce the soft-deleted state.
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"data":null,"metadata":{"version":2,"created_time":"2026-09-13T09:00:00Z","deletion_time":"","destroyed":false}}}"""));
        BastionVaultClient client = BuildClient(transport);

        KvV2Secret secret = await client.Kv.V2.GetSecretAsync("app/db");

        Assert.Equal(KvV2SecretState.Live, secret.State);
        Assert.Null(secret.Data);
    }

    [Fact]
    [Requirement("KV2-001")]
    [Trait("Requirement", "KV2-001")]
    public async Task Version_zero_means_latest_and_is_omitted_from_the_query()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Kv.V2.ReadSecretAsync("app/db", version: 0);

        Assert.Equal("https://vault.example.com:8200/v1/secret/data/app/db", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("KV2-001")]
    [Trait("Requirement", "KV2-001")]
    public async Task Both_selectors_travel_together_as_query_parameters_with_the_env_value_encoded()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Kv.V2.ReadSecretAsync("app/db", version: 3, env: "eu west&prod");

        // `Uri.ToString()` unescapes `%20`, so the escaped form is asserted through PathAndQuery,
        // which is what actually goes on the wire.
        Assert.Equal(
            "/v1/secret/data/app/db?version=3&env=eu%20west%26prod",
            transport.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    [Requirement("KV2-001")]
    [Trait("Requirement", "KV2-001")]
    public async Task A_read_response_with_no_metadata_object_raises_BV_PROTOCOL_002()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"data":{"k":"v"}}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.ReadSecretAsync("app/db"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal("data.metadata", exception.Details["expectedField"]);
    }

    [Fact]
    [Requirement("KV2-011")]
    [Trait("Requirement", "KV2-011")]
    public async Task A_version_metadata_with_no_created_time_raises_BV_PROTOCOL_002_rather_than_defaulting_it()
    {
        // 07 §Types declares `CreatedTime: instant` with no `?`, so its absence is a protocol
        // violation the catalogue already names — not a zero timestamp (D-M1c-25).
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"data":{"k":"v"},"metadata":{"version":1,"destroyed":false}}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.ReadSecretAsync("app/db"));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal("created_time", exception.Details["expectedField"]);
    }

    [Fact]
    [Requirement("KV2-003")]
    [Trait("Requirement", "KV2-003")]
    public async Task Cas_zero_is_sent_as_zero_and_never_omitted()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(WriteBody));
        BastionVaultClient client = BuildClient(transport);

        KvV2VersionMetadata written = await client.Kv.V2.WriteSecretAsync("app/db", Data("k", "v"), options: new KvWriteOptions { Cas = 0 });

        Assert.Equal("""{"data":{"k":"v"},"options":{"cas":0}}""", Body(transport.Requests[0]));
        Assert.Equal(3, written.Version);
    }

    [Fact]
    [Requirement("KV2-002")]
    [Trait("Requirement", "KV2-002")]
    public async Task A_write_response_with_no_data_object_raises_BV_PROTOCOL_002()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.WriteSecretAsync("app/db", Data("k", "v")));

        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, exception.Code);
        Assert.Equal("data", exception.Details["expectedField"]);
    }

    [Theory]
    [Requirement("KV2-002")]
    [Trait("Requirement", "KV2-002")]
    [InlineData("eu/west", "must not contain `/`")]
    [InlineData("prod", "must not contain control characters")]
    [InlineData("   ", "must not be blank")]
    public async Task An_invalid_env_is_refused_client_side_with_BV_INPUT_001(string env, string _)
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException read = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.ReadSecretAsync("app/db", env: env));
        Assert.Equal(ErrorCodes.InputInvalidArgument, read.Code);
        Assert.Equal("env", read.Details["argument"]);
        Assert.Equal(0, read.Attempts);

        BastionVaultException write = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.WriteSecretAsync("app/db", Data("k", "v"), options: new KvWriteOptions { Env = env }));
        Assert.Equal(ErrorCodes.InputInvalidArgument, write.Code);

        BastionVaultException patch = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.PatchEnvironmentAsync("app/db", env, Data("k", "v")));
        Assert.Equal(ErrorCodes.InputInvalidArgument, patch.Code);

        BastionVaultException all = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.WriteAllEnvironmentsAsync("app/db", Data("k", "v"), Envs(env, "k", "v")));
        Assert.Equal(ErrorCodes.InputInvalidArgument, all.Code);

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("KV2-002")]
    [Trait("Requirement", "KV2-002")]
    public async Task WriteSecret_validates_every_key_of_Envs_not_only_Env()
    {
        // RF-3: KV2-002's environment-name rule applies to an environment name wherever it appears
        // on the wire, and WriteSecret validated `Env` but not a key of `Envs` — two client-side
        // contracts for one wire shape.
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.WriteSecretAsync(
            "app/db",
            Data("k", "v"),
            options: new KvWriteOptions { Envs = Envs("pr\nod", "k", "v") }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Equal("env", exception.Details["argument"]);
        Assert.Equal(0, exception.Attempts);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("KV2-002")]
    [Trait("Requirement", "KV2-002")]
    public async Task An_empty_data_map_is_refused_client_side_on_every_v2_write_shape()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);
        IReadOnlyDictionary<string, JsonElement> empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (Func<Task> call in new Func<Task>[]
        {
            () => client.Kv.V2.WriteSecretAsync("app/db", empty),
            () => client.Kv.V2.PatchEnvironmentAsync("app/db", "prod", empty),
            () => client.Kv.V2.WriteAllEnvironmentsAsync("app/db", empty, Envs("prod", "k", "v")),
            () => client.Kv.V1.WriteAsync("app", empty, "kv"),
        })
        {
            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
            Assert.Equal(0, exception.Attempts);
        }

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("KV2-007")]
    [Trait("Requirement", "KV2-007")]
    public async Task An_empty_version_list_is_refused_with_BV_INPUT_002()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        foreach (Func<Task> call in new Func<Task>[]
        {
            () => client.Kv.V2.UndeleteAsync("app/db", []),
            () => client.Kv.V2.DestroyAsync("app/db", []),
            // An explicit empty list on a soft delete is refused rather than silently widened to
            // "the latest version", which is what omitting the argument means.
            () => client.Kv.V2.SoftDeleteAsync("app/db", versions: []),
        })
        {
            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputEmptyCollection, exception.Code);
            Assert.Equal("versions", exception.Details["argument"]);
            Assert.Equal(0, exception.Attempts);
        }

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("KV2-004")]
    [Trait("Requirement", "KV2-004")]
    public async Task Soft_delete_with_no_versions_sends_no_body_and_targets_the_data_route()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Kv.V2.SoftDeleteAsync("app/db");

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.Equal("https://vault.example.com:8200/v1/secret/data/app/db", transport.Requests[0].Uri.ToString());
        Assert.True(transport.Requests[0].Body.IsEmpty);
    }

    [Fact]
    [Requirement("KV2-005")]
    [Trait("Requirement", "KV2-005")]
    public async Task Destroy_posts_the_version_list_to_the_destroy_route()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Kv.V2.DestroyAsync("app/db", [1, 2]);

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.Equal("https://vault.example.com:8200/v1/secret/destroy/app/db", transport.Requests[0].Uri.ToString());
        Assert.Equal("""{"versions":[1,2]}""", Body(transport.Requests[0]));
    }

    [Fact]
    [Requirement("KV-002")]
    [Trait("Requirement", "KV-002")]
    public async Task Delete_metadata_removes_every_version_through_the_metadata_route()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Kv.V2.DeleteMetadataAsync("app/db");

        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.Equal("https://vault.example.com:8200/v1/secret/metadata/app/db", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    public async Task Read_metadata_of_a_missing_secret_is_null_and_a_non_integer_version_key_is_skipped()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"current_version":1,"created_time":"2026-09-13T09:00:00Z","updated_time":"2026-09-13T09:00:00Z","versions":{"1":{"version":1,"created_time":"2026-09-13T09:00:00Z","destroyed":false},"latest":{"version":9,"created_time":"2026-09-13T09:00:00Z","destroyed":false}}}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.Null(await client.Kv.V2.ReadMetadataAsync("app/db"));

        KvV2Metadata metadata = (await client.Kv.V2.ReadMetadataAsync("app/db"))!;
        Assert.Equal([1], metadata.Versions.Keys);
        // KV2-010 names "0s" as the disabled spelling, so an absent field reports it in the
        // specification's own words.
        Assert.Equal("0s", metadata.DeleteVersionAfter);
        Assert.Equal(0, metadata.MaxVersions);
        Assert.Equal(0, metadata.OldestVersion);
        Assert.False(metadata.CasRequired);
    }

    [Fact]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    public async Task Read_metadata_with_no_versions_object_yields_an_empty_version_table()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"current_version":1,"created_time":"2026-09-13T09:00:00Z","updated_time":"2026-09-13T09:00:00Z"}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.Empty((await client.Kv.V2.ReadMetadataAsync("app/db"))!.Versions);
    }

    // ---------------------------------------------------------------- KV2-021: environments

    [Fact]
    [Requirement("KV2-021")]
    [Trait("Requirement", "KV2-021")]
    public async Task PatchEnvironment_sends_one_write_with_env_and_performs_no_read_merge_write()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(WriteBody));
        BastionVaultClient client = BuildClient(transport);

        KvV2VersionMetadata written = await client.Kv.V2.PatchEnvironmentAsync("app/db", "prod", Data("pool", "50"), cas: 2);

        _ = Assert.Single(transport.Requests);
        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.Equal("https://vault.example.com:8200/v1/secret/data/app/db", transport.Requests[0].Uri.ToString());
        Assert.Equal("""{"data":{"pool":"50"},"options":{"cas":2},"env":"prod"}""", Body(transport.Requests[0]));
        Assert.Equal(3, written.Version);
    }

    [Fact]
    [Requirement("KV2-021")]
    [Trait("Requirement", "KV2-021")]
    public async Task WriteAllEnvironments_sends_base_as_data_and_the_whole_override_map_as_envs()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(WriteBody));
        BastionVaultClient client = BuildClient(transport);

        _ = await client.Kv.V2.WriteAllEnvironmentsAsync("app/db", Data("host", "db"), Envs("prod", "pool", "50"));

        _ = Assert.Single(transport.Requests);
        Assert.Equal("""{"data":{"host":"db"},"envs":{"prod":{"pool":"50"}}}""", Body(transport.Requests[0]));
    }

    [Fact]
    [Requirement("KV2-021")]
    [Trait("Requirement", "KV2-021")]
    public async Task PatchEnvironment_refuses_a_null_env_because_the_operation_is_meaningless_without_one()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.PatchEnvironmentAsync("app/db", null!, Data("a", "1")));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Equal("env", exception.Details["argument"]);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("KV2-002")]
    [Trait("Requirement", "KV2-002")]
    public async Task Env_and_Envs_together_are_refused_client_side_before_any_request()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.WriteSecretAsync(
                "app/db",
                Data("a", "1"),
                options: new KvWriteOptions { Env = "prod", Envs = Envs("prod", "a", "2") }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Equal("options", exception.Details["argument"]);
        Assert.Equal(0, exception.Attempts);
        Assert.Empty(transport.Requests);
    }

    // ---------------------------------------------------------------- KV2-022, KV2-023, KV2-024

    [Fact]
    [Requirement("KV2-022")]
    [Trait("Requirement", "KV2-022")]
    public async Task An_env_scoped_credential_fails_fast_on_every_v2_data_operation_without_env()
    {
        FakeTransport transport = new();
        BastionVaultClient client = BuildClient(transport);
        RecordEnvScopedLogin(client);

        foreach (Func<Task> call in new Func<Task>[]
        {
            () => client.Kv.V2.ReadSecretAsync("app/db"),
            () => client.Kv.V2.GetSecretAsync("app/db"),
            () => client.Kv.V2.WriteSecretAsync("app/db", Data("a", "1")),
            () => client.Kv.V2.WriteAllEnvironmentsAsync("app/db", Data("a", "1"), Envs("prod", "a", "2")),
        })
        {
            BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.KvEnvironmentRequired, exception.Code);
            Assert.Equal(0, exception.Attempts);
            Assert.Null(exception.StatusCode);
            Assert.Equal<IEnumerable<string>>(["prod-*"], (IReadOnlyList<string>)exception.Details["secret_globs"]!);
            Assert.Equal<IEnumerable<string>>(["*"], (IReadOnlyList<string>)exception.Details["machine_globs"]!);
            // KV2-022: "MUST include the allowed globs in the hint".
            Assert.Contains("prod-*", exception.Hint, StringComparison.Ordinal);
        }

        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("KV2-022")]
    [Trait("Requirement", "KV2-022")]
    public async Task An_env_scoped_credential_with_env_supplied_proceeds_and_never_touches_v1()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(200, body: Json("""{"data":{"keys":[]}}"""));
        BastionVaultClient client = BuildClient(transport);
        RecordEnvScopedLogin(client);

        Assert.Null(await client.Kv.V2.ReadSecretAsync("app/db", env: "prod-eu"));
        // KV1-004: v1 is never subject to the check, with or without a scoped credential.
        Assert.Null(await client.Kv.V1.ReadAsync("app", "kv"));
        Assert.Empty(await client.Kv.V2.ListAsync("app"));

        Assert.Equal(3, transport.Requests.Count);
    }

    [Fact]
    [Requirement("KV2-022")]
    [Trait("Requirement", "KV2-022")]
    public async Task An_unscoped_credential_and_a_client_that_never_logged_in_are_not_subject_to_the_check()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        // No recorded credential at all.
        Assert.Null(await client.Kv.V2.ReadSecretAsync("app/db"));

        // A recorded credential carrying no approle_env_* metadata.
        client.Context.RecordLogin(
            new AuthInfo { ClientToken = new SecretString(FakeTokens.Client), IssuedAt = DateTimeOffset.UnixEpoch },
            install: false);
        Assert.Null(await client.Kv.V2.ReadSecretAsync("app/db"));
    }

    [Fact]
    [Requirement("KV2-023")]
    [Trait("Requirement", "KV2-023")]
    public async Task A_403_on_a_v2_read_without_env_gets_the_policy_may_require_env_note()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.ReadSecretAsync("app/db"));

        Assert.Contains("The policy may require `env`", exception.Hint, StringComparison.Ordinal);
        Assert.Contains("required_parameters", exception.Hint, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("KV2-023")]
    [Trait("Requirement", "KV2-023")]
    public async Task The_note_is_absent_when_the_read_did_carry_an_env()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"Permission denied."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.ReadSecretAsync("app/db", env: "prod"));

        Assert.DoesNotContain("The policy may require `env`", exception.Hint, StringComparison.Ordinal);
    }

    [Theory]
    [Requirement("KV2-023")]
    [Trait("Requirement", "KV2-023")]
    // A metadata route, a mount literally named `data`, a `data` route with no path segment, a
    // path too short to be a route at all, a different query parameter, an ordinary v1-shaped GET
    // (KV2-023 narrowing's v1-read negative case), and a v2 write to the same route the read case
    // matches (KV2-023 narrowing's v2-write negative case, RF corrected in M4b).
    [InlineData("secret/metadata/app/db", "GET", false)]
    [InlineData("data/app/db", "GET", false)]
    [InlineData("secret/data/", "GET", false)]
    [InlineData("secret", "GET", false)]
    [InlineData("secret/data/app?version=2", "GET", true)]
    [InlineData("[ns=team] secret/data/app", "GET", true)]
    [InlineData("kv/app/db", "GET", false)]
    [InlineData("secret/data/app", "POST", false)]
    public void The_KV2_023_condition_matches_only_a_GET_on_a_v2_data_route_with_no_env(string path, string method, bool expected)
    {
        string hint = HintEnrichment.Enrich(
            ErrorCodes.AuthzPermissionDenied,
            "base.",
            new HintEnrichment.Context(403, null, path, "team", HasCaCertificate: true, Address, Method: method));

        Assert.Equal(expected, hint.Contains("The policy may require `env`", StringComparison.Ordinal));
    }

    [Fact]
    [Requirement("KV2-024")]
    [Trait("Requirement", "KV2-024")]
    public async Task An_env_absent_from_the_config_registry_is_still_sent_because_the_registry_is_advisory()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        // KV2-024: no read of `config.environments` happens, and no client-side validation against
        // it — so an environment the registry does not list reaches the server as written, and it
        // costs exactly one request rather than two.
        _ = await client.Kv.V2.ReadSecretAsync("app/db", env: "not-registered");

        _ = Assert.Single(transport.Requests);
        Assert.EndsWith("?env=not-registered", transport.Requests[0].Uri.ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- KV2-009: config

    [Fact]
    [Requirement("KV2-009")]
    [Trait("Requirement", "KV2-009")]
    public async Task ReadConfig_of_an_absent_mount_is_null_and_UpdateConfig_then_raises_BV_NOTFOUND_002()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(404);
        BastionVaultClient client = BuildClient(transport);

        Assert.Null(await client.Kv.V2.ReadConfigAsync());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.UpdateConfigAsync(new KvV2ConfigPatch { MaxVersions = 5 }));

        Assert.Equal(ErrorCodes.NotFoundMountNotFound, exception.Code);
        Assert.Equal("secret/config", exception.Path);
        // The failed read is the only request: nothing is written against a config that is not there.
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    [Requirement("KV2-009")]
    [Trait("Requirement", "KV2-009")]
    public async Task WriteConfig_replaces_by_sending_all_four_fields()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        await client.Kv.V2.WriteConfigAsync(new KvV2Config
        {
            MaxVersions = 5,
            CasRequired = true,
            DeleteVersionAfter = "24h",
            Environments = ["prod"],
        });

        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.Equal("https://vault.example.com:8200/v1/secret/config", transport.Requests[0].Uri.ToString());
        Assert.Equal(
            """{"max_versions":5,"cas_required":true,"delete_version_after":"24h","environments":["prod"]}""",
            Body(transport.Requests[0]));
    }

    [Fact]
    [Requirement("KV2-009")]
    [Trait("Requirement", "KV2-009")]
    public async Task UpdateConfig_leaves_every_null_patch_field_alone_and_returns_what_it_wrote()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"max_versions":10,"cas_required":false,"delete_version_after":"0s","environments":["prod"]}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        KvV2Config merged = await client.Kv.V2.UpdateConfigAsync(new KvV2ConfigPatch { CasRequired = true });

        Assert.Equal(
            """{"max_versions":10,"cas_required":true,"delete_version_after":"0s","environments":["prod"]}""",
            Body(transport.Requests[1]));
        Assert.True(merged.CasRequired);
        Assert.Equal(10, merged.MaxVersions);
        Assert.Equal(["prod"], merged.Environments);
        Assert.Equal("0s", merged.DeleteVersionAfter);
    }

    [Fact]
    [Requirement("KV2-009")]
    [Trait("Requirement", "KV2-009")]
    public async Task UpdateConfig_can_set_all_four_fields_at_once()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"max_versions":10,"cas_required":false,"delete_version_after":"0s","environments":[]}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        KvV2Config merged = await client.Kv.V2.UpdateConfigAsync(new KvV2ConfigPatch
        {
            MaxVersions = 3,
            CasRequired = true,
            DeleteVersionAfter = "1h",
            Environments = ["prod", "dev"],
        });

        Assert.Equal(3, merged.MaxVersions);
        Assert.Equal("1h", merged.DeleteVersionAfter);
        Assert.Equal(["prod", "dev"], merged.Environments);
    }

    [Fact]
    [Requirement("KV2-009")]
    [Trait("Requirement", "KV2-009")]
    public async Task ReadConfig_defaults_only_the_fields_the_specification_names_a_default_for()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{}}"""));
        BastionVaultClient client = BuildClient(transport);

        KvV2Config config = (await client.Kv.V2.ReadConfigAsync("kv"))!;

        Assert.Equal("0s", config.DeleteVersionAfter);
        Assert.Empty(config.Environments);
        Assert.Equal("https://vault.example.com:8200/v1/kv/config", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("KV2-009")]
    [Trait("Requirement", "KV2-009")]
    public async Task Config_and_patch_arguments_are_null_checked()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Kv.V2.WriteConfigAsync(null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Kv.V2.UpdateConfigAsync(null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.Kv.V2.WriteAllEnvironmentsAsync("app", Data("a", "1"), null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Kv.V2.WriteSecretAsync("app", null!));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Kv.V2.UndeleteAsync("app", null!));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Kv.V2.ReadSecretAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Kv.V2.ReadSecretAsync("app", mount: string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => client.Kv.V1.ReadAsync(string.Empty));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => client.Kv.V1.ListAsync(null!));
    }

    // ---------------------------------------------------------------- KV2-010: Go-style durations

    [Theory]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    [InlineData(0, "0s")]
    [InlineData(1, "1s")]
    [InlineData(60, "1m")]
    [InlineData(90, "1m30s")]
    [InlineData(3600, "1h")]
    [InlineData(5400, "1h30m")]
    [InlineData(3661, "1h1m1s")]
    [InlineData(86400, "24h")]
    [InlineData(-90, "-1m30s")]
    public void GoDuration_emits_the_spelling_KV2_010_names(int seconds, string expected)
    {
        Assert.Equal(expected, GoDuration.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    public void GoDuration_rounds_a_sub_second_value_to_whole_seconds()
    {
        // No field in 07 is sub-second, and "1.5s" is a spelling the specification does not state.
        Assert.Equal("2s", GoDuration.Format(TimeSpan.FromMilliseconds(1500)));
        Assert.Equal("0s", GoDuration.Format(TimeSpan.FromMilliseconds(400)));
    }

    [Theory]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    [InlineData(0, "0s")]
    [InlineData(1, "1s")]
    [InlineData(60, "1m")]
    [InlineData(90, "1m30s")]
    [InlineData(3600, "1h")]
    [InlineData(5400, "1h30m")]
    [InlineData(3661, "1h1m1s")]
    [InlineData(86400, "24h")]
    [InlineData(-90, "-1m30s")]
    [InlineData(5400, "90m")]
    public void GoDuration_TryParse_round_trips_the_spelling_Format_emits(int seconds, string spelling)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), GoDuration.TryParse(spelling));
    }

    [Theory]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-duration")]
    [InlineData("1x")]
    [InlineData("1m1h")]
    [InlineData("-")]
    public void GoDuration_TryParse_is_null_for_a_string_it_cannot_understand_and_never_throws(string? value)
    {
        Assert.Null(GoDuration.TryParse(value));
    }

    [Fact]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    public async Task ReadConfig_parses_DeleteVersionAfter_into_DeleteVersionAfterDuration_including_disabled()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"max_versions":0,"cas_required":false,"delete_version_after":"0s","environments":[]}}"""));
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"max_versions":0,"cas_required":false,"delete_version_after":"garbage","environments":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        KvV2Config disabled = (await client.Kv.V2.ReadConfigAsync())!;
        Assert.Equal(TimeSpan.Zero, disabled.DeleteVersionAfterDuration);

        KvV2Config unparseable = (await client.Kv.V2.ReadConfigAsync())!;
        Assert.Null(unparseable.DeleteVersionAfterDuration);
    }

    [Fact]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    public async Task UpdateConfig_formats_DeleteVersionAfterDuration_Go_style_onto_the_wire()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"max_versions":10,"cas_required":false,"delete_version_after":"0s","environments":[]}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        KvV2Config merged = await client.Kv.V2.UpdateConfigAsync(
            new KvV2ConfigPatch { DeleteVersionAfterDuration = TimeSpan.FromHours(1) });

        Assert.Equal("1h", merged.DeleteVersionAfter);
        Assert.Contains(""""delete_version_after":"1h"""", Body(transport.Requests[1]), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    public async Task UpdateConfig_rejects_a_patch_whose_string_and_duration_forms_disagree()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"max_versions":10,"cas_required":false,"delete_version_after":"0s","environments":[]}}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.UpdateConfigAsync(new KvV2ConfigPatch
            {
                DeleteVersionAfter = "2h",
                DeleteVersionAfterDuration = TimeSpan.FromHours(1),
            }));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        // The read is the only request: nothing is written for a patch the SDK refuses client-side.
        Assert.Equal(1, transport.Requests.Count);
    }

    [Fact]
    [Requirement("KV2-010")]
    [Trait("Requirement", "KV2-010")]
    public async Task UpdateConfig_accepts_a_patch_whose_string_and_duration_forms_agree()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(
            """{"data":{"max_versions":10,"cas_required":false,"delete_version_after":"0s","environments":[]}}"""));
        transport.EnqueueResponse(204);
        BastionVaultClient client = BuildClient(transport);

        KvV2Config merged = await client.Kv.V2.UpdateConfigAsync(new KvV2ConfigPatch
        {
            DeleteVersionAfter = "1h",
            DeleteVersionAfterDuration = TimeSpan.FromHours(1),
        });

        Assert.Equal("1h", merged.DeleteVersionAfter);
        Assert.Equal(2, transport.Requests.Count);
    }

    // ---------------------------------------------------------------- the pre-encoded query seam

    [Fact]
    [Requirement("KV2-001")]
    [Trait("Requirement", "KV2-001")]
    public void A_pre_encoded_path_still_has_its_query_split_off_and_passed_through_verbatim()
    {
        // The regression this guards: before M4 the `pathIsEncoded` arm returned the whole string
        // as the path, so an appended `?env=…` would have been sent as part of the path. Login was
        // the only pre-encoded caller and never appends a query, so nothing caught it.
        (string path, string? query) = UrlBuilder.SplitAndEncode("secret/data/a%2Fb?env=pr%26od", pathIsEncoded: true);

        Assert.Equal("secret/data/a%2Fb", path);
        Assert.Equal("env=pr%26od", query);

        // And the unencoded route is unchanged.
        (string plainPath, string? plainQuery) = UrlBuilder.SplitAndEncode("secret/data/a b?env=p+q");
        Assert.Equal("secret/data/a%20b", plainPath);
        Assert.Equal("env=p%2Bq", plainQuery);

        // No query component at all yields no query, on both arms.
        Assert.Null(UrlBuilder.SplitAndEncode("secret/data/a", pathIsEncoded: true).EncodedQuery);
        Assert.Null(UrlBuilder.SplitAndEncode("secret/data/a").EncodedQuery);
    }

    // ---------------------------------------------------------------- KV-011, KV-012, KV-013: convenience helpers

    [Fact]
    [Requirement("KV-011")]
    [Trait("Requirement", "KV-011")]
    public async Task WriteIfAbsent_sends_cas_0_and_returns_the_written_metadata()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(WriteBody));
        BastionVaultClient client = BuildClient(transport);

        KvV2VersionMetadata metadata = await client.Kv.V2.WriteIfAbsentAsync("app/db", Data("k", "v"));

        // KV2-003: `Cas = 0` is sent as `0`, never omitted.
        Assert.Equal("""{"data":{"k":"v"},"options":{"cas":0}}""", Body(transport.Requests[0]));
        Assert.Equal(3, metadata.Version);
    }

    [Fact]
    [Requirement("KV-011")]
    [Trait("Requirement", "KV-011")]
    public async Task WriteIfAbsent_translates_a_cas_mismatch_into_secret_already_exists()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(400, body: Json("""{"error":"Check-and-set parameter did not match the current version."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.WriteIfAbsentAsync("app/db", Data("k", "v")));

        Assert.Equal(ErrorCodes.ConflictSecretAlreadyExists, exception.Code);
        Assert.Equal(1, exception.Attempts);
        Assert.Equal(ErrorCodes.KvCasMismatch, Assert.IsType<BastionVaultException>(exception.Cause).Code);
    }

    [Fact]
    [Requirement("KV-012")]
    [Trait("Requirement", "KV-012")]
    public async Task UpdateWithRetry_reads_then_writes_with_the_read_version_as_cas()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"data":{"k":"old"},"metadata":{"version":2,"created_time":"2026-09-13T09:00:00Z","deletion_time":"","destroyed":false}}}"""));
        transport.EnqueueResponse(200, body: Json(WriteBody));
        BastionVaultClient client = BuildClient(transport);

        KvV2VersionMetadata metadata = await client.Kv.V2.UpdateWithRetryAsync(
            "app/db",
            current => Data("k", current!.Data!["k"].GetString() + "-next"));

        Assert.Equal("GET", transport.Requests[0].Method);
        Assert.Equal("""{"data":{"k":"old-next"},"options":{"cas":2}}""", Body(transport.Requests[1]));
        Assert.Equal(3, metadata.Version);
    }

    [Fact]
    [Requirement("KV-012")]
    [Trait("Requirement", "KV-012")]
    public async Task UpdateWithRetry_writes_cas_0_when_the_secret_does_not_exist_and_the_callback_sees_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(200, body: Json(WriteBody));
        BastionVaultClient client = BuildClient(transport);

        bool sawNull = false;
        _ = await client.Kv.V2.UpdateWithRetryAsync("app/db", current =>
        {
            sawNull = current is null;
            return Data("k", "v");
        });

        Assert.True(sawNull);
        Assert.Equal("""{"data":{"k":"v"},"options":{"cas":0}}""", Body(transport.Requests[1]));
    }

    [Fact]
    [Requirement("KV-012")]
    [Trait("Requirement", "KV-012")]
    public async Task UpdateWithRetry_retries_on_cas_mismatch_up_to_maxAttempts_then_surfaces_it()
    {
        FakeTransport transport = new();
        // Two full read/write cycles, both mismatching; maxAttempts = 2 means no third attempt.
        string readBody = """{"data":{"data":{"k":"old"},"metadata":{"version":2,"created_time":"2026-09-13T09:00:00Z","deletion_time":"","destroyed":false}}}""";
        transport.EnqueueResponse(200, body: Json(readBody));
        transport.EnqueueResponse(400, body: Json("""{"error":"Check-and-set parameter did not match the current version."}"""));
        transport.EnqueueResponse(200, body: Json(readBody));
        transport.EnqueueResponse(400, body: Json("""{"error":"Check-and-set parameter did not match the current version."}"""));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.UpdateWithRetryAsync("app/db", _ => Data("k", "v"), maxAttempts: 2));

        // The server's own CasMismatch is surfaced, not an invented "retries exhausted" code.
        Assert.Equal(ErrorCodes.KvCasMismatch, exception.Code);
        Assert.Equal(4, transport.Requests.Count);
    }

    [Fact]
    [Requirement("KV-012")]
    [Trait("Requirement", "KV-012")]
    public async Task UpdateWithRetry_refuses_a_maxAttempts_below_one()
    {
        BastionVaultClient client = BuildClient(new FakeTransport());

        BastionVaultException exception = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Kv.V2.UpdateWithRetryAsync("app/db", _ => Data("k", "v"), maxAttempts: 0));

        Assert.Equal(ErrorCodes.InputInvalidArgument, exception.Code);
        Assert.Equal("maxAttempts", exception.Details["argument"]);
        Assert.Equal(0, exception.Attempts);
    }

    [Fact]
    [Requirement("KV-013")]
    [Trait("Requirement", "KV-013")]
    public async Task ReadField_is_null_for_a_missing_secret_and_for_a_missing_field()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(200, body: Json("""{"data":{"data":{"k":"v"},"metadata":{"version":1,"created_time":"2026-09-13T09:00:00Z","deletion_time":"","destroyed":false}}}"""));
        BastionVaultClient client = BuildClient(transport);

        Assert.Null(await client.Kv.V2.ReadFieldAsync("app/db", "k"));
        Assert.Null(await client.Kv.V2.ReadFieldAsync("app/db", "missing"));
    }

    [Fact]
    [Requirement("KV-013")]
    [Trait("Requirement", "KV-013")]
    public async Task ReadField_returns_the_fields_value_when_present()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"data":{"k":"v"},"metadata":{"version":1,"created_time":"2026-09-13T09:00:00Z","deletion_time":"","destroyed":false}}}"""));
        BastionVaultClient client = BuildClient(transport);

        JsonElement? field = await client.Kv.V2.ReadFieldAsync("app/db", "k");

        Assert.Equal("v", field!.Value.GetString());
    }

    [Fact]
    [Requirement("KV-013")]
    [Trait("Requirement", "KV-013")]
    public async Task GetField_raises_BV_KV_011_for_a_missing_field_and_propagates_BV_KV_001_and_BV_KV_007_unchanged()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"data":{"data":{"k":"v"},"metadata":{"version":1,"created_time":"2026-09-13T09:00:00Z","deletion_time":"","destroyed":false}}}"""));
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(200, body: Json(SoftDeletedBody));
        BastionVaultClient client = BuildClient(transport);

        BastionVaultException fieldNotFound = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.GetFieldAsync("app/db", "missing"));
        Assert.Equal(ErrorCodes.KvFieldNotFound, fieldNotFound.Code);
        Assert.Equal(["k"], Assert.IsType<List<string>>(fieldNotFound.Details["fields"]));

        // BV-KV-001 propagates from GetSecretAsync unchanged (no secret at all).
        BastionVaultException notFound = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.GetFieldAsync("app/db", "k"));
        Assert.Equal(ErrorCodes.KvSecretNotFound, notFound.Code);

        // BV-KV-007 propagates from GetSecretAsync unchanged (soft-deleted version).
        BastionVaultException softDeleted = await Assert.ThrowsAsync<BastionVaultException>(() => client.Kv.V2.GetFieldAsync("app/db", "k"));
        Assert.Equal(ErrorCodes.KvVersionSoftDeleted, softDeleted.Code);
    }

    [Fact]
    [Requirement("KV-002")]
    [Trait("Requirement", "KV-002")]
    public void Kv_exposes_exactly_the_two_version_explicit_sub_clients_and_no_agnostic_facade()
    {
        // KV-002's MUST is the version-explicit shape; D-M4-9 declines its MAY, so the absence of a
        // `ReadSecret` on the root is asserted rather than left to inspection. D-M7-8 keeps that
        // ruling while landing KV-001: `DetectVersionAsync` is now present (SYS-026 exists, which
        // is the only reason D-M4-2 deferred it), and the façade it would have powered is still
        // declined, because KV-002's fallback-to-V2 rule is a guess this SDK does not make.
        BastionVaultClient client = BuildClient(new FakeTransport());

        Assert.NotNull(client.Kv.V1);
        Assert.NotNull(client.Kv.V2);
        Assert.NotNull(typeof(KvOperations).GetMethod("DetectVersionAsync"));
        Assert.Null(typeof(KvOperations).GetMethod("ReadSecretAsync"));
        Assert.Null(typeof(KvOperations).GetMethod("ReadManyAsync"));
    }

    // ---------------------------------------------------------------- helpers

    private const string WriteBody =
        """{"data":{"version":3,"created_time":"2026-09-13T12:00:00Z","deletion_time":"","destroyed":false}}""";

    private const string SoftDeletedBody =
        """{"data":{"data":null,"metadata":{"version":2,"created_time":"2026-09-13T09:00:00Z","deletion_time":"2026-09-13T11:00:00Z","destroyed":false}}}""";

    private const string MetadataBody =
        """{"data":{"current_version":1,"oldest_version":1,"max_versions":10,"cas_required":false,"delete_version_after":"0s","created_time":"2026-09-13T09:00:00Z","updated_time":"2026-09-13T09:00:00Z","versions":{}}}""";

    private static void RecordEnvScopedLogin(BastionVaultClient client)
    {
        client.Context.RecordLogin(
            new AuthInfo
            {
                ClientToken = new SecretString(FakeTokens.Client),
                IssuedAt = DateTimeOffset.UnixEpoch,
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [EnvironmentScope.ScopedKey] = "true",
                    [EnvironmentScope.SecretGlobsKey] = "prod-*",
                    [EnvironmentScope.MachineGlobsKey] = "*",
                },
            },
            install: false);
    }

    private static IReadOnlyDictionary<string, JsonElement> Data(string key, string value)
    {
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [key] = JsonDocument.Parse($"\"{value}\"").RootElement.Clone(),
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Envs(string env, string key, string value)
    {
        return new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.Ordinal)
        {
            [env] = Data(key, value),
        };
    }

    private static string Body(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    private static BastionVaultClient BuildClient(ITransport transport)
    {
        BastionVaultClientOptions options = new()
        {
            Address = Address,
            Token = FakeTokens.Client,
            Transport = transport,
            RateGate = new RateGate { RatePerSecond = 0 },
            RetryPolicy = new RetryPolicy { MaxAttempts = 1 },
        };
        return new BastionVaultClient(options, EnvironmentSource.None);
    }
}
