using System.Net;
using System.Reflection;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text.Json;
using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

public sealed class HarnessTests
{
    [Fact]
    [Requirement("CNF-041")]
    [Trait("Requirement", "CNF-041")]
    public void SdkInfo_reports_the_specification_version_and_sdk_version()
    {
        Assert.Equal("1.1.0", BastionVault.IntegrationSdk.SdkInfo.SpecificationVersion);

        // CNF-041 is a statement about the *release*, so this assertion reads the package
        // version out of the built assembly (MSBuild derives it from the csproj's <Version>)
        // rather than restating a literal. A literal is what let 0.8.0 ship while
        // SdkInfo.SdkVersion — and therefore the User-Agent — still said 0.7.0, with this
        // test green the whole time: the R-10 shape, a gate trusting a record over an
        // execution.
        string assemblyVersion = typeof(BastionVault.IntegrationSdk.BastionVaultClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        Assert.StartsWith(BastionVault.IntegrationSdk.SdkInfo.SdkVersion, assemblyVersion, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("CNF-047")]
    [Trait("Requirement", "CNF-047")]
    public void SdkInfo_exposes_the_upstream_specification_source_matching_provenance_json()
    {
        Assert.Equal("0.42.0", BastionVault.IntegrationSdk.SdkInfo.SpecificationSourceRelease);
        Assert.Equal("v0.42.0", BastionVault.IntegrationSdk.SdkInfo.SpecificationSourceRef);

        FixtureRepository repository = new();
        string provenancePath = Path.Combine(repository.RepositoryRoot, "specifications", "provenance.json");
        if (!File.Exists(provenancePath))
        {
            // specifications/provenance.json is produced by a companion change (DR-0011);
            // skip the manifest-consistency check rather than fail when it is absent.
            return;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(provenancePath));
        JsonElement upstream = document.RootElement.GetProperty("upstream");
        Assert.Equal(
            BastionVault.IntegrationSdk.SdkInfo.SpecificationSourceRelease,
            upstream.GetProperty("release").GetString());
        Assert.Equal(
            BastionVault.IntegrationSdk.SdkInfo.SpecificationSourceRef,
            upstream.GetProperty("ref").GetString());
    }

    [Fact]
    [Requirement("FIX-001")]
    [Requirement("TST-010")]
    [Requirement("TST-012")]
    [Trait("Requirement", "FIX-001")]
    public void Repository_loads_and_validates_every_fixture_on_disk()
    {
        FixtureRepository repository = new();
        FixtureDocument[] fixtures = repository.EnumerateAll().ToArray();

        // 210 = the 74 fixtures of M0, plus the (then) 124 errors.recognition.* fixtures
        // tools/error-catalogue generates from Appendix B §2, plus the five hand-authored
        // errors.enrichment.* fixtures (D-M1c-10), plus the five section-05 fixtures authored
        // from DR-0006 (auth.userpass.login-ok, auth.userpass.totp-required,
        // auth.appid.gated-403, auth.appid.env-scope-derived, and
        // auth.token.lookup-self-no-token-client-side, the last of which D-M2-10 holds pending
        // through M2a), plus M2c's two auth.autorenew.* fixtures, authorable only once D-M2-27
        // settled the clock question they depend on. 213 = 210 + D-M3-5's three newly authored
        // fixtures (sys.info.tiers, sys.cluster-status.ok, sys.cluster-status.forbidden), closing
        // the appendix-C gap DR-0007's grounding pass found. 218 = 213 + D-M4-8's five newly
        // authored kv.* fixtures (kv.v1.list, kv.v1.write-empty-data-rejected, kv.v2.undelete,
        // kv.v2.metadata-read, kv.v2.config-environments), closing the appendix-C gap DR-0009's
        // grounding pass found for section 07. 222 = 218 + D-M5-4's four newly authored
        // resilience.* fixtures (resilience.address.classification,
        // resilience.srv.sorted-and-verbatim-underscore, resilience.probe.classify,
        // resilience.pick.leader-over-follower-rtt-weight), closing four of the six Appendix C gaps
        // DR-0010's grounding pass found for section 13. 224 = 222 + M5b's two
        // (resilience.failover.not-armed-single-candidate, resilience.backoff.math-seeded), which
        // completes Appendix C's resilience.* list. The last six are M6's and M7's, merged here.
        // 226 = 224 + M6's two (auth.ferrogate.requirement-unauthenticated,
        // auth.ferrogate.enrolment-pending), which completes Appendix C's auth.* list and closes
        // the last gap in section 05. 228 = 226 + D-M7-9's two (sys.mount.204,
        // sys.unseal.invalid-key), the Appendix C line 123 names that fall in M7's first slice.
        // 230 = 228 + D-M7-24's two (sys.policies.acl-read, sys.namespaces.write-full-replace),
        // the other two gaps on that line, so line 123's sys.* list now has no gap either.
        // M7c re-authored errors.enrichment.404-kv2-hint in place (D-M7-43), which is why the
        // errors.* count is unchanged at 134. 236 = 230 + M8a's six generated
        // errors.recognition.* fixtures, one per alternative of an Appendix B §2 qualifier
        // group (R-23, D-M8-3), which takes the generated set 124 → 130 and errors.* 134 → 140.
        // 238 = 236 + M8c's two newly authored totp.* fixtures (totp.generate-mode-create,
        // totp.validate-false), closing the appendix-C gap DR-0013 slice c's grounding pass found
        // for section 11 (totp.wrong-mode already existed). 241 = 238 + DR-0013 slice b's three
        // newly authored transit.* fixtures (transit.encrypt-decrypt, transit.below-min-decryption,
        // transit.random-cap), closing Appendix C's transit.* list
        // (transit.ciphertext-format-client-side and transit.unknown-key-500-mapped already
        // existed). 245 = 241 + DR-0013 slice d's three newly authored efficiency.* fixtures
        // (efficiency.rategate.fifo-throughput, efficiency.rategate.pause-on-429,
        // efficiency.batch.per-op-errors) and kv.read-many-fallback-on-unsupported, the 21st
        // kv.* fixture D-M4-8 recorded as unauthored and booked to M8. 247 = 245 + slice e's two
        // newly authored efficiency.* fixtures (efficiency.pagination.cursor-passthrough,
        // efficiency.cache-version.topics-limit), closing Appendix C's efficiency.* list.
        // 250 = 247 + M9 slice a's three newly authored pki.* fixtures (pki.issue,
        // pki.certs-info-page, pki.role-not-found). 253 = 250 + M9 slice d's three newly
        // authored ssh.* fixtures (ssh.sign, ssh.creds-ip-not-allowed, ssh.verify-invalid-otp);
        // sshbroker.effective-v2-pinned was already on disk and already counted, and slice d
        // drives it for the first time rather than adding it to the corpus. 254 = 253 +
        // the D-M12-5 follow-up's identity.self, captured against a live bvault 0.44.5 and
        // closing R-35 (.NET only, per this slice's brief; R-25 stands for rust/ and python/).
        Assert.Equal(254, fixtures.Length);
        Assert.All(fixtures, fixture =>
        {
            string relativePath = Path.GetRelativePath(repository.RepositoryRoot, fixture.Path);
            Assert.StartsWith(Path.Combine("specifications", "fixtures") + Path.DirectorySeparatorChar, relativePath, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    [Requirement("TST-010")]
    [Requirement("TST-012")]
    [Trait("Requirement", "TST-010")]
    public void Repository_filters_by_level_and_sections_and_loads_by_id()
    {
        FixtureRepository repository = new();
        FixtureDocument fixture = repository.LoadById("transport.method.list-verb");

        Assert.Equal("core", fixture.Level);
        Assert.Contains("03", fixture.Sections);
        Assert.Equal(fixture.Id, repository.FilterByLevel("core").Single(candidate => candidate.Id == fixture.Id).Id);
        Assert.Contains(repository.FilterBySections("03"), candidate => candidate.Id == fixture.Id);
    }

    [Fact]
    [Requirement("TST-010")]
    [Trait("Requirement", "TST-010")]
    public void Repository_reports_a_clear_error_when_root_marker_is_missing()
    {
        string temporaryDirectory = Directory.CreateTempSubdirectory("bastionvault-no-repository-").FullName;
        try
        {
            DirectoryNotFoundException exception = Assert.Throws<DirectoryNotFoundException>(
                () => FixtureRepository.FindRepositoryRoot(temporaryDirectory));
            Assert.Contains("fixture.schema.json", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Could not locate repository root", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    [Requirement("D-M0-2")]
    [Requirement("TST-011")]
    [Trait("Requirement", "D-M0-2")]
    public void Empty_registry_reports_unregistered_fixture_as_pending_and_emits_count()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = new();
        FixtureDocument[] fixtures = repository.EnumerateAll().ToArray();

        FixtureRunResult[] results = fixtures.Select(driver.Run).ToArray();
        Console.WriteLine($"Pending fixtures: {driver.PendingCount}");

        Assert.Equal(254, driver.PendingCount);
        Assert.All(results, result => Assert.Equal(FixtureRunStatus.Pending, result.Status));
        Assert.Equal(0, new OperationRegistry().Count);
    }

    [Fact]
    [Requirement("TST-011")]
    [Requirement("TST-013")]
    [Requirement("FIX-002")]
    [Requirement("FIX-003")]
    [Trait("Requirement", "TST-011")]
    public void Driver_configures_from_fixture_scripts_transport_and_compares_request_and_result()
    {
        FixtureDocument fixture = SyntheticFixture(
            """
            {
              "id": "synthetic.driver",
              "title": "Driver harness fixture",
              "requirements": ["TST-011"],
              "level": "core",
              "sections": ["03"],
              "client": {
                "address": "https://fixture.invalid:8200",
                "token": null,
                "namespace": "eng",
                "apiPrefix": "v2",
                "settings": {"MaxAttempts": 2}
              },
              "environment": {"BvEnvironment": "test"},
              "operation": {"name": "Synthetic.Echo", "args": {"value": "argument"}, "options": {"trace": true}},
              "exchanges": [{
                "expectRequest": {
                  "method": "LIST",
                  "url": "https://fixture.invalid:8200/v2/secret/",
                  "headers": {"x-fixture-header": "expected"},
                  "absentHeaders": ["X-Forbidden"],
                  "body": {"z": 1, "a": 2}
                },
                "respond": {"status": 200, "body": {"ok": true}}
              }],
              "expect": {
                "result": {
                  "value": "ok",
                  "nested": {"number": 2},
                  "optional": "$any",
                  "missing": "$absent",
                  "nullish": "$absent",
                  "redacted": "$redacted",
                  "items": [1, {"value": true}]
                }
              }
            }
            """);
        OperationRegistry registry = new();
        registry.Register("Synthetic.Echo", invocation =>
        {
            Assert.Equal("https://fixture.invalid:8200", invocation.Configuration.Address);
            Assert.Equal("eng", invocation.Configuration.Namespace);
            Assert.Equal("v2", invocation.Configuration.ApiPrefix);
            Assert.Equal("test", invocation.Configuration.Environment["BvEnvironment"]);
            Assert.Equal("argument", invocation.Arguments.GetProperty("value").GetString());
            Assert.True(invocation.Options.GetProperty("trace").GetBoolean());

            JsonElement body = ParseJson("""{"a":2,"z":1}""");
            FixtureTransportResponse response = invocation.Transport.SendAsync(
                FixtureRequest.Create(
                    "LIST",
                    "https://fixture.invalid:8200/v2/secret/",
                    new[] { new KeyValuePair<string, string>("X-Fixture-Header", "expected"), new KeyValuePair<string, string>("X-Unlisted", "unchecked") },
                    body)).GetAwaiter().GetResult();
            Assert.Equal(200, response.Status);

            Dictionary<string, object?> result = new(StringComparer.Ordinal)
            {
                ["value"] = "ok",
                ["nested"] = new Dictionary<string, object?> { ["number"] = 2 },
                ["optional"] = null,
                ["nullish"] = null,
                ["redacted"] = new RedactedValue("fixture-redacted-value"),
                ["items"] = new object?[] { 1, new Dictionary<string, object?> { ["value"] = true } },
                ["extra"] = "ignored",
            };
            return ValueTask.FromResult(new FixtureOperationResult(result));
        });

        FixtureRunResult run = new FixtureDriver(registry).Run(fixture);

        Assert.Equal(FixtureRunStatus.Passed, run.Status);
    }

    [Fact]
    [Requirement("FIX-002")]
    [Trait("Requirement", "FIX-002")]
    public void Request_comparison_accepts_canonical_body_reordering_case_insensitive_headers_and_unlisted_headers_when_non_strict()
    {
        JsonElement expected = ParseJson(
            """
            {
              "method": "LIST",
              "url": "https://fixture.invalid/v1/x",
              "headers": {"X-Token": "fake-token"},
              "absentHeaders": ["X-Missing"],
              "body": {"first": 1, "second": {"left": true, "right": false}}
            }
            """);
        FixtureRequest actual = FixtureRequest.Create(
            "LIST",
            "https://fixture.invalid/v1/x",
            new[]
            {
                new KeyValuePair<string, string>("x-token", "fake-token"),
                new KeyValuePair<string, string>("X-Unlisted", "allowed"),
            },
            ParseJson("""{"second":{"right":false,"left":true},"first":1}"""));

        FixtureComparisons.AssertRequest(expected, actual);
    }

    [Fact]
    [Requirement("FIX-002")]
    [Trait("Requirement", "FIX-002")]
    public void Request_comparison_reports_method_url_header_absence_body_and_strict_header_failures()
    {
        JsonElement expected = ParseJson(
            """
            {
              "method": "GET",
              "url": "https://fixture.invalid/right",
              "headers": {"X-Expected": "right"},
              "absentHeaders": ["X-Forbidden"],
              "body": {"expected": true}
            }
            """);
        FixtureRequest actual = FixtureRequest.Create(
            "POST",
            "https://fixture.invalid/wrong",
            new[]
            {
                new KeyValuePair<string, string>("X-Expected", "wrong"),
                new KeyValuePair<string, string>("X-Forbidden", "present"),
                new KeyValuePair<string, string>("X-Extra", "present"),
            },
            ParseJson("""{"actual":false}"""));

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => FixtureComparisons.AssertRequest(expected, actual, strictHeaders: true));

        Assert.Contains("method", exception.Message, StringComparison.Ordinal);
        Assert.Contains("url", exception.Message, StringComparison.Ordinal);
        Assert.Contains("X-Expected", exception.Message, StringComparison.Ordinal);
        Assert.Contains("X-Forbidden", exception.Message, StringComparison.Ordinal);
        Assert.Contains("body", exception.Message, StringComparison.Ordinal);
        Assert.Contains("X-Extra", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("FIX-002")]
    [Trait("Requirement", "FIX-002")]
    public void Scripted_transport_honours_all_six_failure_modes_in_order()
    {
        foreach (FixtureTransportFailureMode mode in Enum.GetValues<FixtureTransportFailureMode>())
        {
            FixtureDocument fixture = SyntheticFixture(
                $$"""
                {
                  "id": "synthetic.fail-{{mode}}",
                  "title": "Transport failure",
                  "requirements": ["FIX-002"],
                  "level": "core",
                  "sections": ["03"],
                  "operation": {"name": "Synthetic.Fail"},
                  "exchanges": [{"fail": "{{mode}}"}],
                  "expect": {"result": "$any"}
                }
                """);
            ScriptedTransport transport = ScriptedTransport.From(fixture);

            FixtureTransportFailureException exception = Assert.Throws<FixtureTransportFailureException>(
                () => transport.SendAsync(FixtureRequest.Create("GET", "https://fixture.invalid/")).GetAwaiter().GetResult());

            Assert.Equal(mode, exception.Mode);
        }
    }

    [Fact]
    [Requirement("FIX-003")]
    [Trait("Requirement", "FIX-003")]
    public void Result_comparison_honours_absent_any_redacted_recursive_and_ignores_extra_fields()
    {
        JsonElement expected = ParseJson(
            """
            {
              "redacted": "$redacted",
              "any": "$any",
              "missing": "$absent",
              "nullValue": "$absent",
              "nested": {"number": 2, "flag": true},
              "array": [1, {"name": "ok"}]
            }
            """);
        Dictionary<string, object?> actual = new(StringComparer.Ordinal)
        {
            ["redacted"] = new RedactedValue("fixture-redacted-value"),
            ["any"] = null,
            ["nullValue"] = null,
            ["nested"] = new Dictionary<string, object?> { ["number"] = 2, ["flag"] = true, ["extra"] = "ignored" },
            ["array"] = new object?[] { 1, new Dictionary<string, object?> { ["name"] = "ok", ["extra"] = "ignored" } },
            ["extra"] = "ignored",
        };

        FixtureComparisons.AssertResult(expected, actual);
        Assert.DoesNotContain("fixture-redacted-value", actual["redacted"]!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("FIX-003")]
    [Trait("Requirement", "FIX-003")]
    public void Result_comparison_rejects_each_sentinel_and_recursive_mismatch()
    {
        (string Expected, object? Actual, string Message)[] cases =
        {
            ("""{"value":"$any"}""", new Dictionary<string, object?>(), "present value"),
            ("""{"value":"$absent"}""", new Dictionary<string, object?> { ["value"] = "not-null" }, "absent/null"),
            ("""{"value":"$redacted"}""", new Dictionary<string, object?> { ["value"] = "not-redacted" }, "redacting"),
            ("""{"nested":{"value":2}}""", new Dictionary<string, object?> { ["nested"] = new Dictionary<string, object?> { ["value"] = 3 } }, "$.nested.value"),
            ("""{"items":[1]}""", new Dictionary<string, object?> { ["items"] = new object?[] { 1, 2 } }, "array item"),
        };

        foreach ((string expectedJson, object? actual, string message) in cases)
        {
            FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
                () => FixtureComparisons.AssertResult(ParseJson(expectedJson), actual));
            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Requirement("FIX-004")]
    [Trait("Requirement", "FIX-004")]
    public void Error_comparison_checks_all_optional_fields_detail_keys_hints_and_server_message()
    {
        JsonElement expected = ParseJson(
            """
            {
              "code": "BV-RATE-001",
              "statusCode": 429,
              "retryable": false,
              "attempts": 1,
              "retryAfter": 17,
              "detailsKeys": ["requestId", "limit"],
              "hintContains": ["sys.batch", "WAIT"],
              "serverMessage": "server says no"
            }
            """);
        FixtureError actual = new(
            "BV-RATE-001",
            429,
            false,
            1,
            17,
            new Dictionary<string, object?> { ["requestId"] = "id", ["limit"] = 5 },
            "Use Sys.Batch and wait before retrying.",
            "server says no");

        FixtureComparisons.AssertError(expected, actual);
    }

    [Fact]
    [Requirement("FIX-004")]
    [Trait("Requirement", "FIX-004")]
    public void Error_comparison_reports_exact_and_contains_mismatches()
    {
        JsonElement expected = ParseJson(
            """
            {
              "code": "BV-RATE-001",
              "statusCode": 429,
              "retryable": false,
              "attempts": 1,
              "retryAfter": 17,
              "detailsKeys": ["requestId"],
              "hintContains": ["required-hint"],
              "serverMessage": "expected message"
            }
            """);
        FixtureError actual = new("BV-RATE-002", 500, true, 2, 3, new Dictionary<string, object?>(), "other hint", "other message");

        FixtureAssertionException exception = Assert.Throws<FixtureAssertionException>(
            () => FixtureComparisons.AssertError(expected, actual));

        Assert.Contains("code", exception.Message, StringComparison.Ordinal);
        Assert.Contains("statusCode", exception.Message, StringComparison.Ordinal);
        Assert.Contains("retryable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempts", exception.Message, StringComparison.Ordinal);
        Assert.Contains("retryAfter", exception.Message, StringComparison.Ordinal);
        Assert.Contains("requestId", exception.Message, StringComparison.Ordinal);
        Assert.Contains("required-hint", exception.Message, StringComparison.Ordinal);
        Assert.Contains("serverMessage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-020")]
    [Requirement("TST-021")]
    [Trait("Requirement", "TST-020")]
    public async Task Https_server_supports_list_and_all_tst021_simulations()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        using HttpClient client = CreatePinnedClient(server);

        using HttpRequestMessage list = new(new HttpMethod("LIST"), new Uri(server.BaseAddress, "/v1/secret/"));
        using HttpResponseMessage listResponse = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal("LIST", server.Requests.Single().Method);

        (MockVaultScenario Scenario, HttpStatusCode Status, string Body, string? RetryAfter)[] cases =
        {
            (MockVaultScenario.Sealed, HttpStatusCode.ServiceUnavailable, """{"error":"BastionVault is sealed."}""", null),
            (MockVaultScenario.StandbyHealth, (HttpStatusCode)429, """{"initialized":true,"sealed":false,"standby":true,"cluster_healthy":true}""", null),
            (MockVaultScenario.Uninitialized, HttpStatusCode.NotImplemented, """{"initialized":false,"sealed":false,"standby":false,"cluster_healthy":true}""", null),
            (MockVaultScenario.DosBan, (HttpStatusCode)429, """{"errors":["request temporarily blocked by DoS protection: request rate exceeded: >200 req/10s"]}""", "17"),
            (MockVaultScenario.NamespaceQuota, (HttpStatusCode)429, """{"error":"namespace request-rate quota exceeded: 5 req/s for \"eng\""}""", null),
            (MockVaultScenario.NotFound, HttpStatusCode.NotFound, string.Empty, null),
            (MockVaultScenario.MethodNotAllowed, HttpStatusCode.MethodNotAllowed, string.Empty, null),
            (MockVaultScenario.NoContent, HttpStatusCode.NoContent, string.Empty, null),
            (MockVaultScenario.LoginFailure, HttpStatusCode.OK, """{"renewable":false,"lease_id":"","lease_duration":0,"auth":null,"data":{"error":"invalid username or password"}}""", null),
        };

        foreach ((MockVaultScenario selectedScenario, HttpStatusCode status, string body, string? retryAfter) in cases)
        {
            server.Scenario = selectedScenario;
            using HttpResponseMessage response = await client.GetAsync("/scenario");
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(body, await response.Content.ReadAsStringAsync());
            if (retryAfter is null)
            {
                Assert.False(response.Headers.Contains("Retry-After"));
            }
            else
            {
                Assert.Equal(retryAfter, response.Headers.GetValues("Retry-After").Single());
            }
        }
    }

    [Fact]
    [Requirement("TST-020")]
    [Trait("Requirement", "TST-020")]
    public async Task Https_server_requires_ca_pinning_for_tls()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        Assert.True(server.ServerCertificateHasPrivateKey);
        using (HttpClient unpinned = new(new HttpClientHandler { UseProxy = false }))
        {
            _ = await Assert.ThrowsAsync<HttpRequestException>(() => unpinned.GetAsync(server.BaseAddress));
        }

        using HttpClient pinned = CreatePinnedClient(server);
        using HttpResponseMessage response = await pinned.GetAsync(server.BaseAddress);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", server.CaCertPem, StringComparison.Ordinal);
    }

    [Fact]
    [Requirement("TST-020")]
    [Trait("Requirement", "TST-020")]
    public async Task Https_server_exposes_hostname_mismatch_and_mtls_behaviour()
    {
        await using InProcessHttpsMockServer mismatch = await InProcessHttpsMockServer.StartAsync(
            new MockServerOptions(CertificateHostName: "wronghost.invalid"));
        using HttpClient mismatchClient = CreatePinnedClient(mismatch);
        _ = await Assert.ThrowsAsync<HttpRequestException>(() => mismatchClient.GetAsync(mismatch.BaseAddress));

        await using InProcessHttpsMockServer mtls = await InProcessHttpsMockServer.StartAsync(
            new MockServerOptions(RequireClientCertificate: true));
        using HttpClient noCertificate = CreatePinnedClient(mtls);
        _ = await Assert.ThrowsAsync<HttpRequestException>(() => noCertificate.GetAsync(mtls.BaseAddress));

        using HttpClient withCertificate = CreatePinnedClient(mtls, includeClientCertificate: true);
        using HttpResponseMessage response = await withCertificate.GetAsync(mtls.BaseAddress);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Requirement("TST-020")]
    [Trait("Requirement", "TST-020")]
    public async Task Https_server_reports_keep_alive_connection_reuse()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        using HttpClient client = CreatePinnedClient(server);

        for (int index = 0; index < 3; index++)
        {
            using HttpResponseMessage response = await client.GetAsync($"/keep-alive/{index}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _ = await response.Content.ReadAsStringAsync();
        }

        Assert.Equal(1, server.AcceptedConnectionCount);
    }

    [Fact]
    [Requirement("TST-020")]
    [Trait("Requirement", "TST-020")]
    public async Task Https_server_supports_response_delay_configuration_and_oversized_responses()
    {
        await using InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync(
            new MockServerOptions(ResponseDelay: TimeSpan.Zero, OversizedResponseBytes: 4096));
        using HttpClient client = CreatePinnedClient(server);

        using HttpResponseMessage response = await client.GetAsync("/oversized");
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4096, body.Length);
    }

    [Fact]
    [Requirement("TST-020")]
    [Requirement("TST-050")]
    [Trait("Requirement", "TST-020")]
    public async Task Https_server_removes_generated_certificate_material_on_dispose()
    {
        InProcessHttpsMockServer server = await InProcessHttpsMockServer.StartAsync();
        string temporaryDirectory = server.TempDirectoryPath;
        Assert.NotEmpty(Directory.EnumerateFiles(temporaryDirectory));

        await server.DisposeAsync();

        Assert.False(Directory.Exists(temporaryDirectory));
    }

    [Fact]
    [Requirement("CNF-020")]
    [Requirement("CNF-022")]
    [Requirement("CNF-023")]
    [Requirement("CNF-027")]
    [Requirement("TST-030")]
    [Requirement("TST-031")]
    [Requirement("TST-040")]
    [Trait("Requirement", "TST-030")]
    public void Quality_gates_and_requirement_markers_are_committed()
    {
        FixtureRepository repository = new();
        string dotnetDirectory = Path.Combine(repository.RepositoryRoot, "dotnet");
        string libraryProject = File.ReadAllText(Path.Combine(
            dotnetDirectory,
            "BastionVault.IntegrationSdk",
            "BastionVault.IntegrationSdk.csproj"));
        string testProject = File.ReadAllText(Path.Combine(
            dotnetDirectory,
            "BastionVault.IntegrationSdk.Tests",
            "BastionVault.IntegrationSdk.Tests.csproj"));
        string editorConfig = File.ReadAllText(Path.Combine(dotnetDirectory, ".editorconfig"));

        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", libraryProject, StringComparison.Ordinal);
        Assert.Contains("<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>", libraryProject, StringComparison.Ordinal);
        Assert.Contains("<AnalysisLevel>latest-all</AnalysisLevel>", libraryProject, StringComparison.Ordinal);
        // D-M1b-19: Microsoft.CodeAnalysis.PublicApiAnalyzers (RS0016/RS0017) was found to be
        // silently inert under AnalysisLevel=latest-all (NetAnalyzers.props reassigns
        // $(CodeAnalysisRuleIds), dropping the RS00xx set) and was removed rather than kept as a
        // gate that looks green and is not. CNF-027 is now proven by
        // ApiSurface/PublicApiSurfaceTests.cs, a reflection-based diff against the committed
        // PublicApiSurface.txt, which runs inside plain `dotnet test`.
        Assert.DoesNotContain("<PackageReference Include=\"Microsoft.CodeAnalysis.PublicApiAnalyzers\"", libraryProject, StringComparison.Ordinal);
        // Coverage is measured by exactly one mechanism: coverlet.msbuild (D-M0-12a follow-up).
        // coverlet.collector, driven by coverlet.runsettings' "XPlat Code Coverage" data collector,
        // was tried instead first (it is the reference command in
        // specifications/15-testing-requirements.md) but does not fail "dotnet test" on a seeded,
        // deliberately uncovered branch - it just reports a low percentage and still exits 0.
        // coverlet.msbuild does fail the build on the same seeded violation, so it is the one
        // mechanism kept. Never wire both up at once: they instrument the same assembly and
        // produce two different Cobertura reports, and which one answers this gate then depends on
        // how "dotnet test" is invoked.
        Assert.Contains("<PackageReference Include=\"coverlet.msbuild\"", testProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageReference Include=\"coverlet.collector\"", testProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<RunSettingsFilePath>", testProject, StringComparison.Ordinal);
        Assert.Contains("<CollectCoverage>true</CollectCoverage>", testProject, StringComparison.Ordinal);
        Assert.Contains("GenerateHtmlCoverageReport", testProject, StringComparison.Ordinal);
        Assert.Contains("<Threshold>95</Threshold>", testProject, StringComparison.Ordinal);
        Assert.Contains("<ThresholdType>line,branch</ThresholdType>", testProject, StringComparison.Ordinal);
        Assert.Contains("<Include>[BastionVault.IntegrationSdk]*</Include>", testProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<Exclude", testProject, StringComparison.Ordinal);
        Assert.Contains("dotnet_style_qualification_for_method", editorConfig, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(dotnetDirectory, "BastionVault.IntegrationSdk", "PublicAPI.Shipped.txt")));
        Assert.False(File.Exists(Path.Combine(dotnetDirectory, "BastionVault.IntegrationSdk", "PublicAPI.Unshipped.txt")));
        Assert.True(File.Exists(Path.Combine(dotnetDirectory, "BastionVault.IntegrationSdk", "PublicApiSurface.txt")));

        System.Reflection.MethodInfo[] testMethods = typeof(HarnessTests)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(method => method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0)
            .ToArray();
        Assert.NotEmpty(testMethods);
        Assert.All(testMethods, method =>
            Assert.NotEmpty(method.GetCustomAttributes(typeof(RequirementAttribute), inherit: true)));
    }

    private static HttpClient CreatePinnedClient(InProcessHttpsMockServer server, bool includeClientCertificate = false)
    {
        X509Certificate2 ca = X509Certificate2.CreateFromPem(server.CaCertPem);
        SocketsHttpHandler handler = new()
        {
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.None,
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    if (certificate is not X509Certificate2 leaf || (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                    {
                        return false;
                    }

                    using X509Chain chain = new();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    _ = chain.ChainPolicy.CustomTrustStore.Add(ca);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build(leaf);
                },
            },
        };
        if (includeClientCertificate)
        {
            handler.SslOptions.ClientCertificates = [server.ClientCertificate];
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = server.BaseAddress,
        };
    }

    private static FixtureDocument SyntheticFixture(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new FixtureDocument("synthetic.fixture.json", document.RootElement.Clone());
    }

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    [Requirement("CFG-017")]
    [Requirement("TRN-012")]
    [Trait("Requirement", "CFG-017")]
    public void Fixture_transport_headers_reserved_rejected_resolves_to_real_client_construction()
    {
        FixtureRepository repository = new();
        FixtureDocument fixture = repository.LoadById("transport.headers.reserved-rejected");
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        FixtureDriver driver = new(registry);

        FixtureRunResult result = driver.Run(fixture);

        Assert.Equal(FixtureRunStatus.Passed, result.Status);
        Assert.Equal("Client.Construct", result.OperationName);
    }
}
