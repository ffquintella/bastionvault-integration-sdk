using System.Reflection;
using System.Text;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M7b's unit coverage for the namespace half of <c>06-system-api.md</c> (DR-0012): SYS-060's
/// full-replace write and its read-merge-write companion, SYS-061's unreachable root record, and
/// SYS-062's naming and cascade. TRN-072's <c>ApiPrefix</c> claim is asserted here too, because
/// this slice is the first to land an unpinned and a pinned typed <c>sys</c> operation that can be
/// compared side by side.
/// </summary>
public sealed class SysNamespaceUnitTests
{
    private const string Address = "https://vault.example.com:8200";
    private const string Token = "s.FAKE-token-0000000000000000";

    private const string Record = """
        {"uuid":"ns-1","path":"engineering","parent_uuid":"ns-root","created_at":"2026-01-02T03:04:05Z",
         "child_visible_default":true,
         "quotas":{"max_storage_bytes":1024,"max_leases":10,"request_rate":50,"max_mounts":3,
                   "max_entities":7,"max_child_namespaces":2}}
        """;

    // ---- SYS-060: listing, reading, and the full replace ------------------------------------

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task ListNamespaces_uses_the_LIST_verb_and_reads_keys()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["engineering","sales"]}"""));
        transport.EnqueueResponse(404);
        using BastionVaultClient client = BuildClient(transport);

        Assert.Equal(["engineering", "sales"], await client.Sys.ListNamespacesAsync());
        Assert.Equal("LIST", transport.Requests[0].Method);
        Assert.Equal($"{Address}/v1/sys/namespaces", transport.Requests[0].Uri.ToString());
        Assert.Empty(await client.Sys.ListNamespacesAsync());
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task ReadNamespace_parses_every_field_and_treats_a_missing_quotas_object_as_all_unlimited()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(Record));
        transport.EnqueueResponse(200, body: Json("""{"uuid":"ns-2"}"""));
        using BastionVaultClient client = BuildClient(transport);

        Namespace? full = await client.Sys.ReadNamespaceAsync("engineering/");

        Assert.Equal("ns-1", full!.Uuid);
        Assert.Equal("engineering", full.Path);
        Assert.Equal("ns-root", full.ParentUuid);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), full.CreatedAt);
        Assert.True(full.ChildVisibleDefault);
        Assert.Equal(1024, full.Quotas.MaxStorageBytes);
        Assert.Equal(10, full.Quotas.MaxLeases);
        Assert.Equal(50, full.Quotas.RequestRate);
        Assert.Equal(3, full.Quotas.MaxMounts);
        Assert.Equal(7, full.Quotas.MaxEntities);
        Assert.Equal(2, full.Quotas.MaxChildNamespaces);
        // The trailing slash is accepted and dropped: MountPaths is the one normaliser (D-M7-7).
        Assert.Equal($"{Address}/v1/sys/namespaces/engineering", transport.Requests[0].Uri.ToString());

        // 0 is "unlimited", so an omitted quotas object is a namespace with no limits, and a
        // missing `path` falls back to the one the caller asked for rather than to null.
        Namespace? bare = await client.Sys.ReadNamespaceAsync("sales");
        Assert.Equal(0, bare!.Quotas.MaxStorageBytes);
        Assert.Equal("sales", bare.Path);
        Assert.Null(bare.ParentUuid);
        Assert.Null(bare.CreatedAt);
        Assert.False(bare.ChildVisibleDefault);
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task ReadNamespace_returns_null_for_the_recognised_404_and_for_an_empty_one_but_not_for_a_403()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404, body: Json("""{"error":"no such namespace: \"nope\""}"""));
        transport.EnqueueResponse(404);
        transport.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        using BastionVaultClient client = BuildClient(transport);

        Assert.Null(await client.Sys.ReadNamespaceAsync("nope"));
        Assert.Null(await client.Sys.ReadNamespaceAsync("nope"));

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ReadNamespaceAsync("nope"));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, failure.Code);
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task WriteNamespace_is_a_full_replace_that_sends_every_omitted_quota_as_zero()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(Record));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.WriteNamespaceAsync("engineering", new NamespaceSpec());

        // This is SYS-060's ⚠️ in one assertion: a spec built from nothing does not mean "leave it
        // alone", it means "set everything to zero", and the SDK must make that visible on the
        // wire rather than quietly omitting the fields and hoping the server preserves them.
        Assert.Equal(
            """{"child_visible_default":false,"quotas":{"max_storage_bytes":0,"max_leases":0,"request_rate":0,"max_mounts":0,"max_entities":0,"max_child_namespaces":0}}""",
            BodyOf(transport.Requests[0]));
        Assert.Equal("POST", transport.Requests[0].Method);
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task WriteNamespace_returns_the_record_and_raises_when_the_server_answers_without_one()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(Record));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        Namespace written = await client.Sys.WriteNamespaceAsync(
            "engineering",
            new NamespaceSpec { ChildVisibleDefault = true, Quotas = new NamespaceQuotas { MaxMounts = 3 } });
        Assert.Equal("ns-1", written.Uuid);
        Assert.Contains("\"child_visible_default\":true", BodyOf(transport.Requests[0]), StringComparison.Ordinal);
        Assert.Contains("\"max_mounts\":3", BodyOf(transport.Requests[0]), StringComparison.Ordinal);

        // The response column says 200 with the record; a bodyless answer is an envelope
        // mismatch, not a namespace with empty fields (D-M1c-25).
        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.WriteNamespaceAsync("engineering", new NamespaceSpec()));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task UpdateNamespace_reads_merges_and_writes_leaving_every_unset_member_alone()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(Record));
        transport.EnqueueResponse(200, body: Json(Record));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.UpdateNamespaceAsync("engineering", new NamespacePatch { MaxMounts = 9 });

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal("GET", transport.Requests[0].Method);
        // Everything the patch did not name is carried over from the read, which is the only
        // thing that makes a patch safe against a full-replace endpoint.
        Assert.Equal(
            """{"child_visible_default":true,"quotas":{"max_storage_bytes":1024,"max_leases":10,"request_rate":50,"max_mounts":9,"max_entities":7,"max_child_namespaces":2}}""",
            BodyOf(transport.Requests[1]));
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task UpdateNamespace_can_set_every_member_and_can_clear_child_visible_default()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(Record));
        transport.EnqueueResponse(200, body: Json(Record));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.UpdateNamespaceAsync("engineering", new NamespacePatch
        {
            ChildVisibleDefault = false,
            MaxStorageBytes = 1,
            MaxLeases = 2,
            RequestRate = 3,
            MaxMounts = 4,
            MaxEntities = 5,
            MaxChildNamespaces = 6,
        });

        // `false` is a value, not an absence: a patch modelled on `bool` rather than `bool?`
        // could never turn the flag off.
        Assert.Equal(
            """{"child_visible_default":false,"quotas":{"max_storage_bytes":1,"max_leases":2,"request_rate":3,"max_mounts":4,"max_entities":5,"max_child_namespaces":6}}""",
            BodyOf(transport.Requests[1]));
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task UpdateNamespace_refuses_to_create_a_namespace_that_does_not_exist()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404, body: Json("""{"error":"no such namespace: \"nope\""}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.UpdateNamespaceAsync("nope", new NamespacePatch { MaxMounts = 1 }));

        // An upsert here would write the patch's unset members as zeroes under the guise of a
        // partial update — the exact failure SYS-060's ⚠️ warns about.
        Assert.Equal(ErrorCodes.NotFoundNamespaceNotFound, failure.Code);
        Assert.Equal("nope", failure.Details["namespace"]);
        _ = Assert.Single(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task NamespacesSelf_reads_its_three_fields_and_raises_on_a_bodyless_response()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"namespaces":["","engineering"],"token_namespace":"","root":true}"""));
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        NamespacesSelf self = await client.Sys.NamespacesSelfAsync();
        // "" denotes root and is a legitimate entry, not padding to be filtered out.
        Assert.Equal(["", "engineering"], self.Namespaces);
        Assert.Equal(string.Empty, self.TokenNamespace);
        Assert.True(self.Root);
        Assert.Equal($"{Address}/v1/sys/namespaces-self", transport.Requests[0].Uri.ToString());

        NamespacesSelf empty = await client.Sys.NamespacesSelfAsync();
        Assert.Empty(empty.Namespaces);
        Assert.False(empty.Root);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.NamespacesSelfAsync());
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    // ---- SYS-060: the paginated listing ------------------------------------------------------

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task ListNamespacesInfo_defaults_the_limit_passes_the_cursor_verbatim_and_zips_the_page()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
            {"keys":["engineering","sales"],
             "records":[{"path":"engineering","uuid":"u1"},{"uuid":"u2"}],
             "total":3,"next":"sales","truncated":true}
            """));
        transport.EnqueueResponse(200, body: Json("""{"keys":[],"records":[],"total":3,"next":"","truncated":false}"""));
        using BastionVaultClient client = BuildClient(transport);

        Page<Namespace> page = await client.Sys.ListNamespacesInfoAsync();

        Assert.Equal($"{Address}/v1/sys/namespaces-info?limit=100", transport.Requests[0].Uri.ToString());
        Assert.Equal(["engineering", "sales"], page.Keys);
        Assert.Equal(3, page.Total);
        Assert.Equal("sales", page.Next);
        Assert.True(page.Truncated);
        // Records[i] belongs to Keys[i], and a record that omits its own path takes the key's.
        Assert.Equal("sales", page.Records[1].Path);
        Assert.Equal(["engineering", "sales"], page.Entries.Select(entry => entry.Key));
        Assert.Equal("u2", page.Entries[1].Value.Uuid);

        // The cursor is a key, never an offset, and is encoded as a query value so a key
        // containing `&` or `=` cannot add a parameter.
        Page<Namespace> last = await client.Sys.ListNamespacesInfoAsync("sales&limit=1", 2);
        Assert.Equal($"{Address}/v1/sys/namespaces-info?after=sales%26limit%3D1&limit=2", transport.Requests[1].Uri.ToString());
        // An empty `next` is absence, and the last page is not truncated.
        Assert.Null(last.Next);
        Assert.False(last.Truncated);
        Assert.Empty(last.Entries);
    }

    [Theory]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task ListNamespacesInfo_validates_the_limit_client_side(int limit)
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ListNamespacesInfoAsync(limit: limit));

        Assert.Equal(ErrorCodes.InputOutOfRange, failure.Code);
        Assert.Equal("limit", failure.Details["argument"]);
        Assert.Equal(limit, failure.Details["limit"]);
        Assert.Equal(0, failure.Attempts);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task ListNamespacesInfo_rejects_a_page_whose_keys_and_records_disagree()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["a","b"],"records":[{"uuid":"u1"}],"total":2,"truncated":false}"""));
        transport.EnqueueResponse(200, body: Json("""{"keys":["a"],"records":["not-an-object"],"total":1,"truncated":false}"""));
        transport.EnqueueResponse(200, body: Json("""{"keys":["a"],"total":1,"truncated":false}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        // A zipped page whose halves are different lengths cannot be zipped, and guessing which
        // key belongs to which record would be worse than refusing.
        foreach (int _ in Enumerable.Range(0, 4))
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ListNamespacesInfoAsync(limit: 500));
            Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
        }
    }

    // ---- SYS-061: the root namespace record is not reachable --------------------------------

    [Theory]
    [Requirement("SYS-061")]
    [Trait("Requirement", "SYS-061")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("/")]
    public async Task Every_namespace_path_member_refuses_the_root_record_client_side(string path)
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        foreach (Func<Task> call in new Func<Task>[]
        {
            () => client.Sys.WriteNamespaceAsync(path, new NamespaceSpec()),
            () => client.Sys.ReadNamespaceAsync(path),
            () => client.Sys.DeleteNamespaceAsync(path),
            () => client.Sys.UpdateNamespaceAsync(path, new NamespacePatch()),
        })
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
            Assert.Equal("path", failure.Details["argument"]);
            Assert.Equal(0, failure.Attempts);
        }

        // SYS-061 names WriteNamespace("") explicitly; the read and the delete are refused on the
        // same grounds, because `sys/namespaces/` addresses the collection and the server's answer
        // would not tell the caller that the root record is simply not there over HTTP.
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-061")]
    [Requirement("SYS-062")]
    [Trait("Requirement", "SYS-061")]
    public void The_sys_surface_exposes_no_root_namespace_operation_and_names_the_delete_Delete()
    {
        string[] members = [.. typeof(SysOperations)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)];

        // SYS-061: "the SDK MUST NOT expose an operation for it". Asserted rather than assumed,
        // because the cheapest way to make WriteNamespace("") work would have been to add one.
        Assert.DoesNotContain(members, name => name.Contains("Root", StringComparison.Ordinal) && name.Contains("Namespace", StringComparison.Ordinal));

        // SYS-062: the destructive word, never a softer synonym. This is a naming requirement, so
        // the test is a naming test.
        Assert.Contains("DeleteNamespaceAsync", members);
        Assert.DoesNotContain(members, name => name.StartsWith("Remove", StringComparison.Ordinal));
        Assert.DoesNotContain(members, name => name.StartsWith("Drop", StringComparison.Ordinal));
    }

    [Fact]
    [Requirement("SYS-062")]
    [Trait("Requirement", "SYS-062")]
    public async Task DeleteNamespace_issues_the_delete_and_surfaces_the_children_refusal_unchanged()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(409, body: Json("""{"error":"namespace has child namespaces"}"""));
        using BastionVaultClient client = BuildClient(transport);

        await client.Sys.DeleteNamespaceAsync("engineering/");
        Assert.Equal("DELETE", transport.Requests[0].Method);
        Assert.Equal($"{Address}/v1/sys/namespaces/engineering", transport.Requests[0].Uri.ToString());

        // The cascade is the server's, and the "blocked while children exist" refusal is passed
        // through rather than retried or turned into a recursive delete the SDK invents.
        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.DeleteNamespaceAsync("engineering"));
        Assert.Equal(409, failure.StatusCode);
    }

    [Fact]
    [Requirement("SYS-060")]
    [Trait("Requirement", "SYS-060")]
    public async Task A_namespace_path_keeps_its_separators_and_encodes_the_rest()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json(Record));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.ReadNamespaceAsync("/eng/sub team?x/");

        // A namespace path is hierarchical, so `/` stays a separator, while the space and the
        // query delimiter are encoded — the multi-segment counterpart of a policy name. Asserted
        // on AbsoluteUri, not ToString(), because the latter unescapes %20 for display and would
        // hide exactly the escaping this test exists to check.
        Assert.Equal($"{Address}/v1/sys/namespaces/eng/sub%20team%3Fx", transport.Requests[0].Uri.AbsoluteUri);
    }

    // ---- TRN-072: typed sys operations use ApiPrefix unless pinned --------------------------

    [Fact]
    [Requirement("TRN-071")]
    [Requirement("TRN-072")]
    [Trait("Requirement", "TRN-072")]
    public async Task Unpinned_typed_sys_operations_follow_ApiPrefix_while_pinned_ones_do_not()
    {
        // TRN-072's claim has two halves and needs both to be asserted: the whole sys surface is
        // duplicated on both prefixes, so an unpinned typed operation must *move* when ApiPrefix
        // moves; and TRN-071's pinned routes must *not* move. Before M7b the tree had no test
        // that ran the same typed sys operation under both prefixes, so the requirement was
        // implemented and unproven.
        FakeTransport v1 = new();
        v1.EnqueueResponse(200, body: Json("""{"keys":[]}"""));
        v1.EnqueueResponse(200, body: Json("""{"type":"hsm"}"""));
        using BastionVaultClient v1Client = BuildClient(v1);

        _ = await v1Client.Sys.ListPoliciesAsync();
        _ = await v1Client.Sys.HsmStatusAsync();

        FakeTransport v2 = new();
        v2.EnqueueResponse(200, body: Json("""{"keys":[]}"""));
        v2.EnqueueResponse(200, body: Json("""{"type":"hsm"}"""));
        v2.EnqueueResponse(200, body: Json("""{"keys":[]}"""));
        using BastionVaultClient v2Client = BuildClient(v2, options => options.ApiPrefix = "v2");

        _ = await v2Client.Sys.ListPoliciesAsync();
        _ = await v2Client.Sys.HsmStatusAsync();
        _ = await v2Client.Sys.ListNamespacesAsync();

        // Unpinned: the same operation lands on whichever prefix the client is configured for.
        Assert.Equal($"{Address}/v1/sys/policies/acl", v1.Requests[0].Uri.ToString());
        Assert.Equal($"{Address}/v2/sys/policies/acl", v2.Requests[0].Uri.ToString());
        Assert.Equal($"{Address}/v2/sys/namespaces", v2.Requests[2].Uri.ToString());

        // Pinned (TRN-071): unmoved under either prefix, which is what makes "unless pinned" an
        // exception rather than the rule.
        Assert.Equal($"{Address}/v2/sys/hsm/status", v1.Requests[1].Uri.ToString());
        Assert.Equal($"{Address}/v2/sys/hsm/status", v2.Requests[1].Uri.ToString());
    }

    // ---- helpers ---------------------------------------------------------------------------

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

    private static string BodyOf(TransportRequest request)
    {
        return Encoding.UTF8.GetString(request.Body.Span);
    }

    private static ReadOnlyMemory<byte> Json(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }
}
