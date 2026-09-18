using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// M7b's unit coverage for the policy half of <c>06-system-api.md</c> (DR-0012): SYS-040's two
/// surfaces and their two document keys, SYS-041's asymmetric reserved-name sets, SYS-042's two
/// server strings, SYS-043's HCL emitter and its escaping, and SYS-045's tri-state dry-run.
/// </summary>
public sealed class SysPolicyUnitTests
{
    private const string Address = "https://vault.example.com:8200";
    private const string Token = "s.FAKEtoken0000000000000000";

    // ---- SYS-040: the two surfaces ---------------------------------------------------------

    [Fact]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-040")]
    public async Task ListPolicies_reads_keys_from_both_surfaces_and_treats_an_absent_list_as_empty()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"keys":["default","admin","root"]}"""));
        transport.EnqueueResponse(200, body: Json("""{"keys":["default"]}"""));
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(404);
        using BastionVaultClient client = BuildClient(transport);

        // The server appends `root` in the root namespace; the SDK passes the list through as sent
        // rather than filtering a name it also refuses to write (SYS-041) — the listing is a fact
        // about the server, not a menu of legal arguments.
        Assert.Equal(["default", "admin", "root"], await client.Sys.ListPoliciesAsync());
        Assert.Equal(["default"], await client.Sys.Legacy.ListPoliciesAsync());
        Assert.Empty(await client.Sys.ListPoliciesAsync());
        Assert.Empty(await client.Sys.ListPoliciesAsync());

        Assert.Equal($"{Address}/v1/sys/policies/acl", transport.Requests[0].Uri.ToString());
        Assert.Equal($"{Address}/v1/sys/policy", transport.Requests[1].Uri.ToString());
    }

    [Fact]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-040")]
    public async Task ReadPolicy_fills_Hcl_from_policy_on_the_acl_surface_and_from_rules_on_the_legacy_one()
    {
        const string Document = "path \"secret/*\" { capabilities = [\"read\"] }";
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json($$"""{"name":"admin","policy":{{JsonSerializer.Serialize(Document)}}}"""));
        transport.EnqueueResponse(200, body: Json($$"""{"name":"admin","rules":{{JsonSerializer.Serialize(Document)}}}"""));
        transport.EnqueueResponse(200, body: Json("""{"name":"empty"}"""));
        using BastionVaultClient client = BuildClient(transport);

        Policy? acl = await client.Sys.ReadPolicyAsync("admin");
        Policy? legacy = await client.Sys.Legacy.ReadPolicyAsync("admin");

        // SYS-040 is exactly this: the caller cannot tell which surface answered.
        Assert.Equal(Document, acl!.Hcl);
        Assert.Equal(Document, legacy!.Hcl);
        Assert.Equal("admin", acl.Name);
        Assert.Equal($"{Address}/v1/sys/policies/acl/admin", transport.Requests[0].Uri.ToString());
        Assert.Equal($"{Address}/v1/sys/policy/admin", transport.Requests[1].Uri.ToString());

        // Neither key is an empty document, not a null one: `Hcl` is non-nullable, and a caller
        // writing it back must not be handed a null to re-serialise.
        Policy? neither = await client.Sys.ReadPolicyAsync("empty");
        Assert.Equal(string.Empty, neither!.Hcl);
    }

    [Fact]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-040")]
    public async Task ReadPolicy_returns_null_for_the_recognised_404_on_both_surfaces_and_for_an_empty_404()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(404, body: Json("""{"error":"No policy named: nope"}"""));
        transport.EnqueueResponse(404, body: Json("""{"error":"No policy named: nope"}"""));
        transport.EnqueueResponse(404);
        using BastionVaultClient client = BuildClient(transport);

        // Appendix B's `no policy named` rule is BV-NOTFOUND-005; the specification's response
        // column writes "→ null / BV-NOTFOUND-005", so a reader turns it into absence.
        Assert.Null(await client.Sys.ReadPolicyAsync("nope"));
        Assert.Null(await client.Sys.Legacy.ReadPolicyAsync("nope"));
        Assert.Null(await client.Sys.ReadPolicyAsync("nope"));
    }

    [Fact]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-040")]
    public async Task ReadPolicy_still_raises_an_unrelated_failure_rather_than_swallowing_it_as_absence()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        using BastionVaultClient client = BuildClient(transport);

        // The catch is scoped to one code. A 403 on a read is not "no such policy", and a reader
        // that returned null here would tell the caller the policy does not exist when the truth
        // is that the token may not look.
        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.ReadPolicyAsync("admin"));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, failure.Code);
    }

    [Theory]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-040")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public async Task Every_named_policy_member_refuses_an_empty_name_client_side(string name)
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        foreach (Func<Task> call in new Func<Task>[]
        {
            () => client.Sys.ReadPolicyAsync(name),
            () => client.Sys.Legacy.ReadPolicyAsync(name),
            () => client.Sys.WritePolicyAsync(name, "path \"x\" {}"),
            () => client.Sys.DeletePolicyAsync(name),
            () => client.Sys.PolicyHistoryAsync(name),
            () => client.Sys.ReadPolicyTestsAsync(name),
            () => client.Sys.WritePolicyTestsAsync(name, []),
        })
        {
            BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(call);
            Assert.Equal(ErrorCodes.InputInvalidArgument, failure.Code);
            Assert.Equal("name", failure.Details["argument"]);
            Assert.Equal(0, failure.Attempts);
        }

        // `sys/policies/acl/` addresses the collection, so a bare 404 there would be
        // indistinguishable from "no such policy" — the same reason SYS-022 gives for mounts.
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-040")]
    public async Task A_policy_name_is_encoded_as_one_path_segment_so_it_cannot_change_the_route()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"name":"x","policy":""}"""));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.ReadPolicyAsync("team/ops?admin");

        // Neither the separator nor the query delimiter survives as structure: an unencoded name
        // would have reached sys/policies/acl/team/ops with an `admin` query parameter.
        Assert.Equal($"{Address}/v1/sys/policies/acl/team%2Fops%3Fadmin", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-040")]
    public async Task PolicyHistory_reads_the_entries_array_and_is_empty_when_the_field_is_absent()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
            {"entries":[
              {"ts":"2026-02-03T04:05:06Z","user":"alice","op":"update","before_raw":"old","after_raw":"new"},
              {"op":"create","after_raw":"first"},
              "not-an-object"
            ]}
            """));
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(404);
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<PolicyHistoryEntry> entries = await client.Sys.PolicyHistoryAsync("admin");

        Assert.Equal(2, entries.Count);
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero), entries[0].Timestamp);
        Assert.Equal("alice", entries[0].User);
        Assert.Equal("update", entries[0].Op);
        Assert.Equal("old", entries[0].BeforeRaw);
        Assert.Equal("new", entries[0].AfterRaw);
        // A create has no `before_raw`, which is absence rather than an empty document.
        Assert.Null(entries[1].BeforeRaw);
        Assert.Null(entries[1].Timestamp);
        Assert.Equal($"{Address}/v1/sys/policies/acl/admin/history", transport.Requests[0].Uri.ToString());

        Assert.Empty(await client.Sys.PolicyHistoryAsync("admin"));
        Assert.Empty(await client.Sys.PolicyHistoryAsync("admin"));
    }

    // ---- SYS-041: the two reserved-name sets, which are deliberately different --------------

    [Theory]
    [Requirement("SYS-041")]
    [Trait("Requirement", "SYS-041")]
    [InlineData("root")]
    [InlineData("test")]
    [InlineData(" root ")]
    public async Task WritePolicy_refuses_root_and_test_client_side_without_sending_a_request(string name)
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.WritePolicyAsync(name, "path \"x\" { capabilities = [\"read\"] }"));

        Assert.Equal(ErrorCodes.InputReservedPolicyName, failure.Code);
        Assert.Equal(0, failure.Attempts);
        Assert.False(failure.Retryable);
        Assert.Equal(name.Trim(), failure.Details["name"]);
        // The trim happens before the check, not after: otherwise " root " would bypass the
        // refusal and then be sent as `root`.
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [Requirement("SYS-041")]
    [Trait("Requirement", "SYS-041")]
    [InlineData("root")]
    [InlineData("default")]
    public async Task DeletePolicy_refuses_root_and_default_client_side(string name)
    {
        FakeTransport transport = new();
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.DeletePolicyAsync(name));

        Assert.Equal(ErrorCodes.InputReservedPolicyName, failure.Code);
        Assert.Equal(0, failure.Attempts);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    [Requirement("SYS-041")]
    [Trait("Requirement", "SYS-041")]
    public async Task The_write_and_delete_reserved_sets_differ_exactly_as_SYS_041_writes_them()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        // `default` is writable (it is the policy every token carries, and it is edited in place)
        // but not deletable; `test` is deletable (nothing reserves the name on a delete) but not
        // writable (the dry-run route owns the segment). An implementation that used one set for
        // both would fail one of these two calls.
        await client.Sys.WritePolicyAsync("default", "path \"x\" { capabilities = [\"read\"] }");
        await client.Sys.DeletePolicyAsync("test");

        Assert.Equal($"{Address}/v1/sys/policies/acl/default", transport.Requests[0].Uri.ToString());
        Assert.Equal("POST", transport.Requests[0].Method);
        Assert.Equal($"{Address}/v1/sys/policies/acl/test", transport.Requests[1].Uri.ToString());
        Assert.Equal("DELETE", transport.Requests[1].Method);
    }

    [Fact]
    [Requirement("SYS-040")]
    [Requirement("SYS-041")]
    [Trait("Requirement", "SYS-041")]
    public async Task WritePolicy_sends_the_document_under_the_policy_key()
    {
        const string Document = "path \"secret/*\" { capabilities = [\"read\", \"list\"] }";
        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        await client.Sys.WritePolicyAsync("admin", Document);

        Assert.Equal($$"""{"policy":{{JsonSerializer.Serialize(Document)}}}""", BodyOf(transport.Requests[0]));
    }

    // ---- SYS-042: both rows are Appendix B recognition, and that claim needs a test ---------

    [Theory]
    [Requirement("SYS-042")]
    [Trait("Requirement", "SYS-042")]
    [InlineData("sentinel (RGP/EGP) policies cannot be created inside a namespace", "BV-INPUT-100")]
    [InlineData("namespace refuses this cross-namespace policy path", "BV-INPUT-102")]
    public async Task WritePolicy_maps_the_two_named_server_errors_through_the_shared_recognition(string message, string expected)
    {
        // Unlike SYS-023's third row (D-M7-6), both of these already have Appendix B §2 rules, so
        // no operation-local remap is needed and none is written. "It already works" is still a
        // claim, and this is the test that makes it one the build checks.
        FakeTransport transport = new();
        transport.EnqueueResponse(400, body: Json($$"""{"error":{{JsonSerializer.Serialize(message)}}}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.WritePolicyAsync("team", "path \"x\" { capabilities = [\"read\"] }"));

        Assert.Equal(expected, failure.Code);
        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    [Requirement("SYS-042")]
    [Trait("Requirement", "SYS-042")]
    public void BV_INPUT_102_carries_the_CrossNamespacePolicyPath_name_SYS_042_writes()
    {
        // SYS-042 names the code *and* the name. A catalogue whose 102 row had drifted onto a
        // different name would satisfy the mapping test above and still break the requirement.
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputCrossNamespacePolicyPath);
        Assert.Equal("BV-INPUT-102", entry.Code);
        Assert.Equal("CrossNamespacePolicyPath", entry.Name);
    }

    // ---- SYS-043: the HCL emitter ----------------------------------------------------------

    [Fact]
    [Requirement("SYS-043")]
    [Trait("Requirement", "SYS-043")]
    public void PolicyBuilder_emits_every_optional_key_SYS_043_names_and_omits_the_ones_not_set()
    {
        string hcl = new PolicyBuilder()
            .AddPath("secret/data/app/*", [Capability.Read, Capability.List])
            .AddPath(
                "transit/encrypt/+",
                [Capability.Update],
                requiredParameters: ["plaintext"],
                allowedParameters: ["context"],
                scopes: ["prod"],
                groups: ["platform"])
            .WithMetadata("owner", "platform")
            .WithMetadata("ticket", "OPS-1")
            .Build();

        Assert.Equal(
            """
            path "secret/data/app/*" {
              capabilities = ["read", "list"]
            }

            path "transit/encrypt/+" {
              capabilities = ["update"]
              required_parameters = ["plaintext"]
              allowed_parameters = ["context"]
              scopes = ["prod"]
              groups = ["platform"]
            }

            metadata {
              owner = "platform"
              ticket = "OPS-1"
            }

            """.ReplaceLineEndings("\n"),
            hcl);
    }

    [Fact]
    [Requirement("SYS-043")]
    [Trait("Requirement", "SYS-043")]
    public void PolicyBuilder_escapes_a_hostile_path_so_it_cannot_close_its_own_block()
    {
        // The whole point of "MUST escape quotes": this input is an attempt to end the path
        // string, close the block, and open a second block granting root everywhere.
        const string Hostile = "secret/\" { capabilities = [\"root\"] }\npath \"*";

        string hcl = new PolicyBuilder()
            .AddPath(Hostile, [Capability.Read])
            .WithMetadata("note", "he said \"hi\"\\bye")
            .WithMetadata("control", "tab\there\rand a carriage return")
            .Build();

        // The injected text stays *inside* the quoted string rather than becoming structure, so
        // it is checked line by line: the document the caller tried to inject would have produced
        // a second `path` line and a second `capabilities` line. Note that the hostile text is
        // still present as characters — that is correct, and is why counting substrings would
        // prove nothing.
        string[] lines = hcl.Split('\n');
        Assert.Equal(1, lines.Count(line => line.StartsWith("path \"", StringComparison.Ordinal)));
        Assert.Equal(1, lines.Count(line => line.StartsWith("  capabilities = ", StringComparison.Ordinal)));
        Assert.Equal("  capabilities = [\"read\"]", lines[1]);
        Assert.DoesNotContain(lines, line => string.Equals(line, "  capabilities = [\"root\"]", StringComparison.Ordinal));
        Assert.Contains("secret/\\\" { capabilities = [\\\"root\\\"] }\\npath \\\"*", hcl, StringComparison.Ordinal);
        // The backslash is escaped first, so the escapes the emitter adds are not re-escaped and
        // a caller's literal backslash survives as one.
        Assert.Contains("note = \"he said \\\"hi\\\"\\\\bye\"", hcl, StringComparison.Ordinal);
        // A raw tab or carriage return inside an HCL quoted string is not valid HCL either, so
        // both are escaped rather than emitted — and neither can add a line to the document.
        Assert.Contains("control = \"tab\\there\\rand a carriage return\"", hcl, StringComparison.Ordinal);
        Assert.DoesNotContain('\t', hcl);
        Assert.DoesNotContain('\r', hcl);
    }

    [Fact]
    [Requirement("SYS-043")]
    [Trait("Requirement", "SYS-043")]
    public void PolicyBuilder_is_deterministic_empty_when_unused_and_replaces_a_repeated_metadata_key_in_place()
    {
        Assert.Equal(string.Empty, new PolicyBuilder().Build());

        string metadataOnly = new PolicyBuilder()
            .WithMetadata("owner", "first")
            .WithMetadata("other", "x")
            .WithMetadata("owner", "second")
            .Build();

        // Replaced in place, not appended: a second Build() of a differently ordered caller would
        // otherwise produce a different document for the same intent, and the round trip below
        // would be asserting an accident.
        Assert.Equal("metadata {\n  owner = \"second\"\n  other = \"x\"\n}\n", metadataOnly);

        // An unrecognised capability (SYS-051's Other) is emitted verbatim rather than dropped.
        Assert.Contains("capabilities = [\"quantum\"]", new PolicyBuilder().AddPath("x", [Capability.Other("quantum")]).Build(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("SYS-043")]
    [Requirement("SYS-040")]
    [Trait("Requirement", "SYS-043")]
    public async Task A_built_policy_round_trips_through_WritePolicy_and_ReadPolicy_unchanged()
    {
        // SYS-043's "MUST be tested with round-trip fixtures": the document is built, written, and
        // read back through the real client and the real wire encoding, and must come back
        // byte-identical. A builder that emitted an unescaped quote would not survive the JSON
        // body it is written in.
        string built = new PolicyBuilder()
            .AddPath("secret/data/\"quoted\"/*", [Capability.Read, Capability.Delete], requiredParameters: ["a\\b"])
            .WithMetadata("owner", "platform")
            .Build();

        FakeTransport transport = new();
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json($$"""{"name":"round","policy":{{JsonSerializer.Serialize(built)}}}"""));
        using BastionVaultClient client = BuildClient(transport);

        await client.Sys.WritePolicyAsync("round", built);
        Policy? read = await client.Sys.ReadPolicyAsync("round");

        Assert.Equal(built, read!.Hcl);
        Assert.Equal($$"""{"policy":{{JsonSerializer.Serialize(built)}}}""", BodyOf(transport.Requests[0]));
    }

    // ---- SYS-045: the tri-state, proven on the wire ----------------------------------------

    [Fact]
    [Requirement("SYS-045")]
    [Requirement("TRN-071")]
    [Trait("Requirement", "SYS-045")]
    public async Task TestPolicy_omits_the_policies_key_entirely_when_the_case_leaves_it_null()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"parse_ok":true,"errors":[],"results":[]}"""));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.TestPolicyAsync("path \"x\" {}", [new PolicyTestCase { Path = "secret/a", Capability = Capability.Read }]);

        // State 1 of 3: absent. The server's own default is ["default"], and it can only apply it
        // if the key never arrives — an empty array would mean something else entirely.
        Assert.Equal(
            $$"""{"policy":{{JsonSerializer.Serialize("path \"x\" {}")}},"cases":[{"path":"secret/a","capability":"read"}]}""",
            BodyOf(transport.Requests[0]));
        Assert.DoesNotContain("policies", BodyOf(transport.Requests[0]), StringComparison.Ordinal);
        Assert.Equal($"{Address}/v2/sys/policies/acl/test", transport.Requests[0].Uri.ToString());
    }

    [Fact]
    [Requirement("SYS-045")]
    [Trait("Requirement", "SYS-045")]
    public async Task TestPolicy_sends_an_empty_array_when_the_case_carries_an_empty_list()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"parse_ok":true,"errors":[],"results":[]}"""));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.TestPolicyAsync(
            "path \"x\" {}",
            [new PolicyTestCase { Path = "secret/a", Capability = Capability.Read, Policies = [] }]);

        // State 2 of 3: []. "Evaluate the draft alone." Collapsing this onto state 1 would
        // silently add the `default` policy's grants to every answer.
        Assert.Contains("\"policies\":[]", BodyOf(transport.Requests[0]), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("SYS-045")]
    [Trait("Requirement", "SYS-045")]
    public async Task TestPolicy_sends_the_named_policies_when_the_case_carries_a_list()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"parse_ok":true,"errors":[],"results":[]}"""));
        using BastionVaultClient client = BuildClient(transport);

        _ = await client.Sys.TestPolicyAsync(
            "path \"x\" {}",
            [new PolicyTestCase { Path = "secret/a", Capability = Capability.Read, Policies = ["ops", "audit"], Env = "prod" }],
            name: "draft");

        // State 3 of 3: the draft plus those. `name` and `env` travel beside it.
        string body = BodyOf(transport.Requests[0]);
        Assert.Contains("\"policies\":[\"ops\",\"audit\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"draft\"", body, StringComparison.Ordinal);
        Assert.Contains("\"env\":\"prod\"", body, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("SYS-045")]
    [Requirement("TRN-071")]
    [Trait("Requirement", "SYS-045")]
    public async Task The_dry_run_route_is_v2_pinned_against_both_the_client_prefix_and_a_per_call_override()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""{"parse_ok":true,"errors":[],"results":[]}"""));
        transport.EnqueueResponse(200, body: Json("""{"cases":[]}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        // TRN-071: the v1 handler does not exist for these three routes (Appendix A, column
        // "Prefix"), so honouring ApiVersion here would be a 404 the caller could not diagnose.
        _ = await client.Sys.TestPolicyAsync("path \"x\" {}", [], options: new RequestOptions { ApiVersion = "v1" });
        _ = await client.Sys.ReadPolicyTestsAsync("admin", new RequestOptions { ApiVersion = "v1" });
        await client.Sys.WritePolicyTestsAsync("admin", [], new RequestOptions { ApiVersion = "v1" });

        Assert.Equal($"{Address}/v2/sys/policies/acl/test", transport.Requests[0].Uri.ToString());
        Assert.Equal($"{Address}/v2/sys/policy-tests/admin", transport.Requests[1].Uri.ToString());
        Assert.Equal($"{Address}/v2/sys/policy-tests/admin", transport.Requests[2].Uri.ToString());
        Assert.Equal("POST", transport.Requests[2].Method);
    }

    [Fact]
    [Requirement("SYS-045")]
    [Trait("Requirement", "SYS-045")]
    public async Task TestPolicy_sends_the_name_root_and_maps_the_400_and_surfaces_an_unreadable_policy_as_BV_AUTHZ_001()
    {
        // D-M7-26 overturns slice b's D-M7-17. SYS-045 writes both of its rows as HTTP status
        // codes and pairs the root refusal with an unreadable-policy 403 that no client can know,
        // where SYS-041 writes "client-side" in as many words. The request therefore goes out and
        // the server's answer is what the caller sees — with the code SYS-045 names.
        FakeTransport transport = new();
        transport.EnqueueResponse(400, body: Json("""{"error":"cannot dry-run the root policy"}"""));
        transport.EnqueueResponse(403, body: Json("""{"error":"permission denied"}"""));
        transport.EnqueueResponse(400, body: Json("""{"error":"line 1: unexpected }"}"""));
        using BastionVaultClient client = BuildClient(transport);

        BastionVaultException reserved = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.TestPolicyAsync("path \"x\" {}", [], name: " root "));
        Assert.Equal(ErrorCodes.InputReservedPolicyName, reserved.Code);
        // The whole of the reversal: the round trip happened, and both observables say so.
        Assert.Equal(1, reserved.Attempts);
        Assert.Equal(400, reserved.StatusCode);
        Assert.Equal("cannot dry-run the root policy", reserved.ServerMessage);
        Assert.Equal("root", reserved.Details["name"]);
        Assert.Equal("name", reserved.Details["argument"]);
        _ = Assert.Single(transport.Requests);
        // The name is trimmed on the wire exactly as SYS-041's writer trims it.
        Assert.Contains("\"name\":\"root\"", BodyOf(transport.Requests[0]), StringComparison.Ordinal);
        Assert.Equal($"{Address}/v2/sys/policies/acl/test", transport.Requests[0].Uri.ToString());

        // The second row is the server's call — naming a policy this token may not read — and
        // reaches the caller as the 403 → BV-AUTHZ-001 the shared status mapping already produces.
        BastionVaultException denied = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.TestPolicyAsync(
                "path \"x\" {}",
                [new PolicyTestCase { Path = "secret/a", Capability = Capability.Read, Policies = ["secret-ops"] }]));
        Assert.Equal(ErrorCodes.AuthzPermissionDenied, denied.Code);
        Assert.Equal(403, denied.StatusCode);

        // D-M7-17's rejected alternative was "remap any 400 on this route", and it is still
        // rejected: the remap is scoped to a call that named `root`, so a malformed draft keeps
        // the shared mapping's answer rather than being reported as a reserved name.
        BastionVaultException malformed = await Assert.ThrowsAsync<BastionVaultException>(
            () => client.Sys.TestPolicyAsync("path \"x\" {", []));
        Assert.Equal(ErrorCodes.InputServerRejectedRequest, malformed.Code);
        Assert.Equal(400, malformed.StatusCode);
    }

    [Fact]
    [Requirement("SYS-045")]
    [Trait("Requirement", "SYS-045")]
    public async Task TestPolicy_reads_every_result_field_and_preserves_an_unmodelled_match_kind()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
            {"parse_ok":false,"errors":["line 3: unexpected }"],
             "results":[
               {"path":"secret/a","capability":"read","allowed":true,"matched_path":"secret/*",
                "match_kind":"prefix","denied_by_deny":false,"granting_policies":["draft"],
                "evaluated_policies":["draft","default"],"missing_policies":["gone"],
                "draft_only_allowed":true},
               {"path":"secret/b","capability":"delete","allowed":false,"match_kind":"regex",
                "denied_by_deny":true},
               "not-an-object"
             ]}
            """));
        transport.EnqueueResponse(200, body: Json("""{"parse_ok":true}"""));
        transport.EnqueueResponse(204);
        using BastionVaultClient client = BuildClient(transport);

        PolicyTestResult result = await client.Sys.TestPolicyAsync("path \"x\" {}", []);

        Assert.False(result.ParseOk);
        Assert.Equal(["line 3: unexpected }"], result.Errors);
        Assert.Equal(2, result.Results.Count);
        Assert.True(result.Results[0].Allowed);
        Assert.Equal("secret/*", result.Results[0].MatchedPath);
        Assert.Equal(PolicyMatchKind.Prefix, result.Results[0].MatchKind);
        Assert.False(result.Results[0].MatchKind.IsOther);
        Assert.Equal(Capability.Read, result.Results[0].Capability);
        Assert.Equal(["draft"], result.Results[0].GrantingPolicies);
        Assert.Equal(["draft", "default"], result.Results[0].EvaluatedPolicies);
        Assert.Equal(["gone"], result.Results[0].MissingPolicies);
        Assert.True(result.Results[0].DraftOnlyAllowed);

        // A fifth match_kind the specification does not name is preserved, not flattened onto
        // `none` and not turned into a parse failure — the same rule SYS-051 sets for Capability.
        Assert.True(result.Results[1].MatchKind.IsOther);
        Assert.Equal("regex", result.Results[1].MatchKind.ToString());
        Assert.Null(result.Results[1].MatchedPath);
        Assert.True(result.Results[1].DeniedByDeny);
        Assert.Empty(result.Results[1].GrantingPolicies);

        // An absent `match_kind` is `none`, which is the value the specification names for
        // "nothing matched" (D-M1c-25), and an absent `results` array is no results.
        PolicyTestResult bare = await client.Sys.TestPolicyAsync("path \"x\" {}", []);
        Assert.Empty(bare.Results);
        Assert.Empty(bare.Errors);
        Assert.Equal(PolicyMatchKind.None, PolicyMatchKind.FromWire(null));
        Assert.Equal("none", default(PolicyMatchKind).ToString());

        BastionVaultException failure = await Assert.ThrowsAsync<BastionVaultException>(() => client.Sys.TestPolicyAsync("path \"x\" {}", []));
        Assert.Equal(ErrorCodes.ProtocolUnexpectedResponse, failure.Code);
    }

    [Fact]
    [Requirement("SYS-045")]
    [Trait("Requirement", "SYS-045")]
    public async Task Saved_effectivity_cases_round_trip_with_the_tri_state_intact()
    {
        FakeTransport transport = new();
        transport.EnqueueResponse(200, body: Json("""
            {"cases":[
              {"path":"a","capability":"read"},
              {"path":"b","capability":"list","policies":[]},
              {"path":"c","capability":"update","policies":["ops"],"env":"prod"},
              "not-an-object"
            ]}
            """));
        transport.EnqueueResponse(204);
        transport.EnqueueResponse(200, body: Json("{}"));
        transport.EnqueueResponse(404);
        using BastionVaultClient client = BuildClient(transport);

        IReadOnlyList<PolicyTestCase> cases = await client.Sys.ReadPolicyTestsAsync("admin");

        // Reading loses the tri-state just as easily as writing does: a parser that defaulted a
        // missing key to an empty list would turn case `a` into case `b`.
        Assert.Equal(3, cases.Count);
        Assert.Null(cases[0].Policies);
        Assert.Empty(cases[1].Policies!);
        Assert.Equal(["ops"], cases[2].Policies);
        Assert.Equal("prod", cases[2].Env);
        Assert.Null(cases[0].Env);

        await client.Sys.WritePolicyTestsAsync("admin", cases);
        string body = BodyOf(transport.Requests[1]);
        Assert.Equal(
            """{"cases":[{"path":"a","capability":"read"},{"path":"b","capability":"list","policies":[]},{"path":"c","capability":"update","policies":["ops"],"env":"prod"}]}""",
            body);

        Assert.Empty(await client.Sys.ReadPolicyTestsAsync("admin"));
        Assert.Empty(await client.Sys.ReadPolicyTestsAsync("admin"));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static BastionVaultClient BuildClient(ITransport transport)
    {
        return new BastionVaultClient(
            new BastionVaultClientOptions
            {
                Address = Address,
                Token = Token,
                Transport = transport,
                RateGate = new RateGate { RatePerSecond = 0 },
                RetryPolicy = new RetryPolicy { MaxAttempts = 1, InitialBackoff = TimeSpan.Zero },
            },
            EnvironmentSource.None);
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
