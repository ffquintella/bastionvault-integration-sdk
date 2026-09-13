using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BastionVault.IntegrationSdk;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Requirement-marked tests for milestone M1a client configuration
/// (<c>decisions/0003-m1a-configuration.md</c>). One test may exercise more than one ID when a
/// single scenario genuinely covers both.
/// </summary>
public sealed class ClientConfigurationTests
{
    [Fact]
    [Requirement("CFG-001")]
    [Trait("Requirement", "CFG-001")]
    public void Address_resolves_explicit_over_bastionvault_env_over_vault_env_over_default()
    {
        EnvironmentSource both = EnvironmentSource.FromMap(new Dictionary<string, string>
        {
            ["BASTIONVAULT_ADDR"] = "https://bv.example.com:8200",
            ["VAULT_ADDR"] = "https://vault.example.com:8200",
        });
        BastionVaultClient explicitWins = new(new BastionVaultClientOptions { Address = "https://explicit.example.com:8200" }, both);
        Assert.Equal("https://explicit.example.com:8200", explicitWins.Config.Address);

        BastionVaultClient bastionVaultWins = new(new BastionVaultClientOptions(), both);
        Assert.Equal("https://bv.example.com:8200", bastionVaultWins.Config.Address);

        EnvironmentSource vaultOnly = EnvironmentSource.FromMap(new Dictionary<string, string>
        {
            ["VAULT_ADDR"] = "https://vault.example.com:8200",
        });
        BastionVaultClient vaultWins = new(new BastionVaultClientOptions(), vaultOnly);
        Assert.Equal("https://vault.example.com:8200", vaultWins.Config.Address);

        BastionVaultClient defaulted = new(new BastionVaultClientOptions(), EnvironmentSource.None);
        Assert.Equal("https://127.0.0.1:8200", defaulted.Config.Address);
    }

    [Fact]
    [Requirement("CFG-002")]
    [Trait("Requirement", "CFG-002")]
    public void Environment_is_read_once_at_construction_not_on_every_access()
    {
        Dictionary<string, string> map = new() { ["BASTIONVAULT_ADDR"] = "https://first.example.com:8200" };
        BastionVaultClient client = new(new BastionVaultClientOptions(), EnvironmentSource.FromMap(map));
        Assert.Equal("https://first.example.com:8200", client.Config.Address);

        map["BASTIONVAULT_ADDR"] = "https://second.example.com:8200";

        Assert.Equal("https://first.example.com:8200", client.Config.Address);
    }

    [Theory]
    [Requirement("CFG-003")]
    [Trait("Requirement", "CFG-003")]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("", false)]
    public void Boolean_environment_values_accept_the_documented_forms(string raw, bool expected)
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_SKIP_VERIFY"] = raw });
        BastionVaultClient client = new(new BastionVaultClientOptions(), environment);
        Assert.Equal(expected, client.Config.TlsSkipVerify);
    }

    [Fact]
    [Requirement("CFG-003")]
    [Trait("Requirement", "CFG-003")]
    public void Invalid_boolean_environment_value_raises_bv_config_003_naming_the_setting()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_SKIP_VERIFY"] = "maybe" });
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(new BastionVaultClientOptions(), environment));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, exception.Code);
        Assert.Equal("TlsSkipVerify", exception.Details["setting"]);
        Assert.False(exception.Retryable);
        Assert.Equal(0, exception.Attempts);
    }

    [Theory]
    [Requirement("CFG-004")]
    [Trait("Requirement", "CFG-004")]
    [InlineData("30s", 30)]
    [InlineData("500ms", 0.5)]
    [InlineData("2h", 7200)]
    [InlineData("45", 45)]
    public void Duration_environment_values_accept_go_style_strings_and_bare_integers(string raw, double expectedSeconds)
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_TIMEOUT"] = raw });
        BastionVaultClient client = new(new BastionVaultClientOptions(), environment);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), client.Config.Timeout);
    }

    [Fact]
    [Requirement("CFG-004")]
    [Trait("Requirement", "CFG-004")]
    public void Compound_duration_1m30s_sums_every_term()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_TIMEOUT"] = "1m30s" });
        BastionVaultClient client = new(new BastionVaultClientOptions(), environment);
        Assert.Equal(TimeSpan.FromSeconds(90), client.Config.Timeout);
    }

    [Fact]
    [Requirement("CFG-004")]
    [Trait("Requirement", "CFG-004")]
    public void Invalid_duration_environment_value_raises_bv_config_003()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_TIMEOUT"] = "sometime" });
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(new BastionVaultClientOptions(), environment));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, exception.Code);
        Assert.Equal("Timeout", exception.Details["setting"]);
    }

    [Fact]
    [Requirement("CFG-005")]
    [Trait("Requirement", "CFG-005")]
    public void EnvironmentSource_None_gives_an_environment_free_construction_while_Map_is_honoured()
    {
        EnvironmentSource map = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_NAMESPACE"] = "from-env" });
        BastionVaultClient withEnv = new(new BastionVaultClientOptions(), map);
        Assert.Equal("from-env", withEnv.Config.Namespace);

        BastionVaultClient withoutEnv = new(new BastionVaultClientOptions(), EnvironmentSource.None);
        Assert.Equal(string.Empty, withoutEnv.Config.Namespace);

        // The default (parameterless-environment) constructor overload is the same seam as EnvironmentSource.Process.
        BastionVaultClient defaultCtor = new(new BastionVaultClientOptions { Address = "https://127.0.0.1:8200" });
        Assert.Equal("https://127.0.0.1:8200", defaultCtor.Config.Address);
    }

    [Theory]
    [Requirement("CFG-010")]
    [Trait("Requirement", "CFG-010")]
    [InlineData("")]
    [InlineData("ftp://example.com")]
    [InlineData("not a valid address")]
    public void Invalid_address_raises_bv_config_001(string address)
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(new BastionVaultClientOptions { Address = address }, EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigInvalidAddress, exception.Code);
        Assert.False(exception.Retryable);
        Assert.Equal(0, exception.Attempts);
        Assert.Equal(ErrorCategory.Configuration, exception.Category);
    }

    [Fact]
    [Requirement("CFG-010")]
    [Trait("Requirement", "CFG-010")]
    public void Bare_dns_name_is_accepted_as_a_cluster_name()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions { Address = "vault.internal" }, EnvironmentSource.None);
        Assert.True(client.Config.AddressIsClusterName);
        Assert.Null(client.Config.AddressUri);
    }

    [Fact]
    [Requirement("CFG-011")]
    [Requirement("CNF-035")]
    [Trait("Requirement", "CFG-011")]
    public void Http_to_non_loopback_host_is_rejected_unless_allowed()
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Address = "http://vault.example.com:8200" },
            EnvironmentSource.None));
        Assert.Equal(ErrorCodes.ConfigInsecureHttpNotAllowed, exception.Code);

        BastionVaultClient allowed = new(
            new BastionVaultClientOptions { Address = "http://vault.example.com:8200", AllowInsecureHttp = true },
            EnvironmentSource.None);
        Assert.Equal("http://vault.example.com:8200", allowed.Config.Address);
    }

    [Theory]
    [Requirement("CFG-011")]
    [Requirement("CNF-035")]
    [Trait("Requirement", "CFG-011")]
    [InlineData("http://127.0.0.1:8200")]
    [InlineData("http://localhost:8200")]
    [InlineData("http://[::1]:8200")]
    public void Http_to_loopback_is_allowed_without_the_flag(string address)
    {
        BastionVaultClient client = new(new BastionVaultClientOptions { Address = address }, EnvironmentSource.None);
        Assert.Equal(address, client.Config.Address);
    }

    [Fact]
    [Requirement("CFG-012")]
    [Trait("Requirement", "CFG-012")]
    public void Exactly_one_of_client_cert_and_key_raises_bv_config_004()
    {
        string certPath = Path.GetTempFileName();
        try
        {
            BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
                new BastionVaultClientOptions { ClientCertPath = certPath },
                EnvironmentSource.None));
            Assert.Equal(ErrorCodes.ConfigClientCertIncomplete, exception.Code);
        }
        finally
        {
            File.Delete(certPath);
        }
    }

    [Fact]
    [Requirement("CFG-013")]
    [Trait("Requirement", "CFG-013")]
    public void Missing_ca_cert_file_raises_bv_config_005_with_path_detail()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"bastionvault-missing-{Guid.NewGuid():n}.pem");
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { CaCertPath = missingPath },
            EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigFileNotReadable, exception.Code);
        Assert.Equal(missingPath, exception.Details["path"]);
    }

    [Fact]
    [Requirement("CFG-013")]
    [Trait("Requirement", "CFG-013")]
    public void Missing_token_file_is_silently_treated_as_no_token()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"bastionvault-missing-token-{Guid.NewGuid():n}.txt");
        BastionVaultClient client = new(
            new BastionVaultClientOptions { UseTokenHelper = true, TokenFile = missingPath },
            EnvironmentSource.None);

        Assert.False(client.Config.Token.HasValue);
    }

    [Fact]
    [Requirement("CFG-014")]
    [Trait("Requirement", "CFG-014")]
    public void Invalid_pem_content_raises_bv_config_006()
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { CaCertPem = "not a certificate" },
            EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigInvalidPem, exception.Code);
        Assert.False(exception.Retryable);
    }

    [Fact]
    [Requirement("CFG-014")]
    [Trait("Requirement", "CFG-014")]
    public void Valid_ca_pem_parses_into_client_config()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=bastionvault-test-ca", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string pem = certificate.ExportCertificatePem();

        BastionVaultClient client = new(new BastionVaultClientOptions { CaCertPem = pem }, EnvironmentSource.None);

        Assert.NotNull(client.Config.CaCertificates);
        Assert.Single(client.Config.CaCertificates!);
    }

    [Fact]
    [Requirement("CFG-015")]
    [Trait("Requirement", "CFG-015")]
    public void Malformed_namespace_raises_bv_config_007_and_trailing_slash_is_stripped_silently()
    {
        BastionVaultException leadingSlash = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Namespace = "/eng" },
            EnvironmentSource.None));
        Assert.Equal(ErrorCodes.ConfigInvalidNamespace, leadingSlash.Code);

        BastionVaultException whitespace = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Namespace = "eng dti" },
            EnvironmentSource.None));
        Assert.Equal(ErrorCodes.ConfigInvalidNamespace, whitespace.Code);

        BastionVaultClient stripped = new(new BastionVaultClientOptions { Namespace = "eng/dti/" }, EnvironmentSource.None);
        Assert.Equal("eng/dti", stripped.Config.Namespace);
    }

    [Theory]
    [Requirement("CFG-016")]
    [Trait("Requirement", "CFG-016")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_timeout_raises_bv_config_003(int seconds)
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Timeout = TimeSpan.FromSeconds(seconds) },
            EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, exception.Code);
        Assert.Equal("Timeout", exception.Details["setting"]);
    }

    [Fact]
    [Requirement("CFG-017")]
    [Trait("Requirement", "CFG-017")]
    public void Reserved_header_raises_bv_config_008_with_hint_naming_it()
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { Headers = new Dictionary<string, string> { ["x-vault-token"] = "oops" } },
            EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigReservedHeader, exception.Code);
        Assert.Contains("X-Vault-Token", exception.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Requirement("CFG-017")]
    [Requirement("CFG-002")]
    [Trait("Requirement", "CFG-017")]
    public void ClientConfig_Headers_does_not_alias_the_callers_mutable_dictionary()
    {
        Dictionary<string, string> callerOwned = new(StringComparer.Ordinal) { ["X-Custom"] = "1" };
        BastionVaultClient client = new(new BastionVaultClientOptions { Headers = callerOwned }, EnvironmentSource.None);

        Assert.Equal("1", client.Config.Headers["X-Custom"]);

        // A caller who keeps a reference to the dictionary they passed in must not be able to
        // retroactively smuggle a reserved header (or anything else) into an already-validated,
        // already-constructed Client (CFG-002, CFG-017, D-M1a-2's "ClientConfig is immutable").
        callerOwned["X-Vault-Token"] = "s.injected";
        callerOwned["X-Custom"] = "mutated-after-construction";

        Assert.False(client.Config.Headers.ContainsKey("X-Vault-Token"));
        Assert.Equal("1", client.Config.Headers["X-Custom"]);
    }

    [Fact]
    [Requirement("CFG-013")]
    [Trait("Requirement", "CFG-013")]
    public void Unusable_path_characters_raise_bv_config_005_not_a_generic_exception()
    {
        string invalidPath = "bad\0path.pem";

        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { CaCertPath = invalidPath },
            EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigFileNotReadable, exception.Code);
        Assert.Equal(invalidPath, exception.Details["path"]);
        Assert.NotNull(exception.Cause);
    }

    [Fact]
    [Requirement("CFG-018")]
    [Requirement("CNF-030")]
    [Trait("Requirement", "CFG-018")]
    public void TlsSkipVerify_succeeds_but_warns_and_marks_the_client_insecure()
    {
        RecordingLogger logger = new();
        BastionVaultClient client = new(
            new BastionVaultClientOptions { TlsSkipVerify = true, Logger = logger },
            EnvironmentSource.None);

        Assert.True(client.IsInsecure);
        Assert.True(client.Config.IsInsecure);
        Assert.Single(logger.Warnings);
        Assert.DoesNotContain("token", logger.Warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Requirement("CFG-030")]
    [Trait("Requirement", "CFG-030")]
    public void Token_helper_file_is_read_when_no_explicit_or_env_token_is_present()
    {
        string tokenFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tokenFile, "  s.FAKEtoken0000000000000000  \n");
            BastionVaultClient client = new(
                new BastionVaultClientOptions { UseTokenHelper = true, TokenFile = tokenFile },
                EnvironmentSource.None);

            Assert.True(client.Config.Token.HasValue);
            Assert.Equal("s.FAKEtoken0000000000000000", client.Config.Token.Reveal());

            BastionVaultClient withoutHelper = new(
                new BastionVaultClientOptions { UseTokenHelper = false, TokenFile = tokenFile },
                EnvironmentSource.None);
            Assert.False(withoutHelper.Config.Token.HasValue);
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    [Requirement("CFG-040")]
    [Trait("Requirement", "CFG-040")]
    public void Ca_cert_is_added_alongside_system_roots_by_default_not_in_place_of_them()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions(), EnvironmentSource.None);
        Assert.False(client.Config.CaCertReplacesSystemRoots);

        BastionVaultClient replacing = new(
            new BastionVaultClientOptions { CaCertReplacesSystemRoots = true },
            EnvironmentSource.None);
        Assert.True(replacing.Config.CaCertReplacesSystemRoots);
    }

    [Fact]
    [Requirement("CFG-041")]
    [Trait("Requirement", "CFG-041")]
    public void Minimum_tls_protocol_is_fixed_at_tls_1_2()
    {
        Assert.Equal(SslProtocols.Tls12, ClientConfig.MinimumTlsProtocol);
    }

    [Fact]
    [Requirement("CFG-042")]
    [Trait("Requirement", "CFG-042")]
    public void TlsServerName_round_trips_from_options_and_environment()
    {
        BastionVaultClient explicitValue = new(
            new BastionVaultClientOptions { TlsServerName = "sni.example.com" },
            EnvironmentSource.None);
        Assert.Equal("sni.example.com", explicitValue.Config.TlsServerName);

        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_TLS_SERVER_NAME"] = "env.example.com" });
        BastionVaultClient fromEnv = new(new BastionVaultClientOptions(), environment);
        Assert.Equal("env.example.com", fromEnv.Config.TlsServerName);
    }

    [Fact]
    [Requirement("CFG-050")]
    [Trait("Requirement", "CFG-050")]
    public void Retry_policy_defaults_match_the_specification()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions(), EnvironmentSource.None);

        Assert.Equal(3, client.Config.RetryPolicy.MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(250), client.Config.RetryPolicy.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(5), client.Config.RetryPolicy.MaxBackoff);
        Assert.Equal(2.0, client.Config.RetryPolicy.BackoffMultiplier);
        Assert.Equal(0.2, client.Config.RetryPolicy.Jitter);
        Assert.True(client.Config.RetryPolicy.RespectRetryAfter);
        Assert.True(client.Config.RetryPolicy.RetryIdempotentOnly);
    }

    [Fact]
    [Requirement("CFG-050")]
    [Trait("Requirement", "CFG-050")]
    public void Max_retries_environment_variable_sets_max_attempts_to_value_plus_one()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_MAX_RETRIES"] = "5" });
        BastionVaultClient client = new(new BastionVaultClientOptions(), environment);
        Assert.Equal(6, client.Config.RetryPolicy.MaxAttempts);
    }

    [Fact]
    [Requirement("CFG-001")]
    [Trait("Requirement", "CFG-001")]
    public void RateGate_defaults_and_environment_variables_are_resolved()
    {
        BastionVaultClient defaulted = new(new BastionVaultClientOptions(), EnvironmentSource.None);
        Assert.Equal(8, defaulted.Config.RateGate.RatePerSecond);
        Assert.Equal(16, defaulted.Config.RateGate.Burst);
        Assert.False(defaulted.Config.RateGate.IsDisabled);

        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string>
        {
            ["BASTIONVAULT_RATE_PER_SEC"] = "0",
            ["BASTIONVAULT_RATE_BURST"] = "32",
        });
        BastionVaultClient fromEnv = new(new BastionVaultClientOptions(), environment);
        Assert.Equal(0, fromEnv.Config.RateGate.RatePerSecond);
        Assert.Equal(32, fromEnv.Config.RateGate.Burst);
        Assert.True(fromEnv.Config.RateGate.IsDisabled);

        RateGate explicitRateGate = new() { RatePerSecond = 100, Burst = 200 };
        BastionVaultClient explicitClient = new(new BastionVaultClientOptions { RateGate = explicitRateGate }, environment);
        Assert.Same(explicitRateGate, explicitClient.Config.RateGate);
    }

    [Theory]
    [Requirement("CFG-001")]
    [Trait("Requirement", "CFG-001")]
    [InlineData("BASTIONVAULT_RATE_PER_SEC", "-1", "RateGate.RatePerSecond")]
    [InlineData("BASTIONVAULT_RATE_BURST", "-1", "RateGate.Burst")]
    [InlineData("BASTIONVAULT_RATE_PER_SEC", "not-a-number", "RateGate.RatePerSecond")]
    public void RateGate_environment_values_that_are_negative_or_unparsable_raise_bv_config_003(string variable, string raw, string expectedSetting)
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { [variable] = raw });
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(new BastionVaultClientOptions(), environment));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, exception.Code);
        // D-M1a-18: Details.setting is developer-facing API and must use the spec's own canonical
        // field name (RatePerSecond, Burst — specifications/14-batch-and-request-efficiency.md:13),
        // never the environment variable's own vocabulary.
        Assert.Equal(expectedSetting, exception.Details["setting"]);
    }

    [Fact]
    [Requirement("CFG-001")]
    [Trait("Requirement", "CFG-001")]
    public void AutoRenew_is_materialised_disabled_by_default_and_has_no_environment_variable()
    {
        BastionVaultClient defaulted = new(new BastionVaultClientOptions(), EnvironmentSource.None);
        Assert.False(defaulted.Config.AutoRenew.Enabled);

        // No environment variable resolves AutoRenew (D-M1a-13): even a plausible-looking one must
        // be ignored, and the only way to change it is the explicit option.
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_AUTO_RENEW"] = "true" });
        BastionVaultClient stillDisabled = new(new BastionVaultClientOptions(), environment);
        Assert.False(stillDisabled.Config.AutoRenew.Enabled);

        BastionVaultClient explicitlyEnabled = new(
            new BastionVaultClientOptions { AutoRenew = new AutoRenewPolicy { Enabled = true } },
            EnvironmentSource.None);
        Assert.True(explicitlyEnabled.Config.AutoRenew.Enabled);
    }

    [Fact]
    [Requirement("CFG-060")]
    [Trait("Requirement", "CFG-060")]
    public void RequestOptions_default_instance_requires_no_arguments()
    {
        RequestOptions defaulted = new();
        Assert.Null(defaulted.Namespace);
        Assert.Null(defaulted.Headers);
        Assert.Null(defaulted.Timeout);
        // D-M1b-6: Idempotent is now tri-state; unset defers to the CFG-051 idempotency table
        // rather than defaulting to a fixed false.
        Assert.Null(defaulted.Idempotent);
        Assert.Null(defaulted.WrapTtl);
        Assert.Null(defaulted.Token);
    }

    [Fact]
    [Requirement("CFG-061")]
    [Requirement("OVR-005")]
    [Trait("Requirement", "CFG-061")]
    public void RequestOptions_instances_cannot_mutate_a_client()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions { Namespace = "eng" }, EnvironmentSource.None);
        RequestOptions perCall = new() { Namespace = "other-namespace" };

        // RequestOptions carries no reference to any Client; constructing one, however it is
        // populated, cannot reach back into a Client to change it (CFG-061).
        Assert.Equal("eng", client.Config.Namespace);
        Assert.Equal("other-namespace", perCall.Namespace);
        Assert.DoesNotContain(typeof(RequestOptions).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance), field => field.FieldType == typeof(BastionVaultClient));
    }

    [Fact]
    [Requirement("CFG-072")]
    [Trait("Requirement", "CFG-072")]
    public void SetAddress_does_not_exist_on_the_client()
    {
        Assert.Null(typeof(BastionVaultClient).GetMethod("SetAddress"));
    }

    [Fact]
    [Requirement("OVR-001")]
    [Trait("Requirement", "OVR-001")]
    public void Transport_is_replaceable_through_the_interface_seam()
    {
        FakeTransport fake = new();
        BastionVaultClient client = new(new BastionVaultClientOptions { Transport = fake }, EnvironmentSource.None);

        Assert.Same(fake, client.Transport);
        Assert.Equal(0, fake.SendCount);
    }

    [Fact]
    [Requirement("OVR-004")]
    [Trait("Requirement", "OVR-004")]
    public void Two_clients_constructed_in_parallel_are_fully_independent_and_touch_no_global_state()
    {
        BastionVaultClient first = new(
            new BastionVaultClientOptions { Address = "https://first.example.com:8200", Namespace = "eng", Token = "s.first" },
            EnvironmentSource.None);
        BastionVaultClient second = new(
            new BastionVaultClientOptions { Address = "https://second.example.com:8200", Namespace = "dti", Token = "s.second" },
            EnvironmentSource.None);

        Assert.Equal("https://first.example.com:8200", first.Config.Address);
        Assert.Equal("https://second.example.com:8200", second.Config.Address);
        Assert.Equal("eng", first.Config.Namespace);
        Assert.Equal("dti", second.Config.Namespace);
        Assert.Equal("s.first", first.Config.Token.Reveal());
        Assert.Equal("s.second", second.Config.Token.Reveal());

        string probeVariable = $"BASTIONVAULT_OVR004_PROBE_{Guid.NewGuid():n}";
        EnvironmentSource.FromMap(new Dictionary<string, string> { [probeVariable] = "should-not-leak" });
        Assert.Null(Environment.GetEnvironmentVariable(probeVariable));
    }

    [Fact]
    [Requirement("CNF-033")]
    [Trait("Requirement", "CNF-033")]
    public void Construction_never_writes_a_token_file_even_with_the_helper_enabled()
    {
        string tokenFile = Path.Combine(Path.GetTempPath(), $"bastionvault-cnf033-{Guid.NewGuid():n}.txt");
        Assert.False(File.Exists(tokenFile));
        try
        {
            _ = new BastionVaultClient(
                new BastionVaultClientOptions { UseTokenHelper = true, TokenFile = tokenFile, Token = "s.explicit" },
                EnvironmentSource.None);

            Assert.False(File.Exists(tokenFile));
        }
        finally
        {
            if (File.Exists(tokenFile))
            {
                File.Delete(tokenFile);
            }
        }
    }

    private sealed class RecordingLogger : IClientLogger
    {
        public List<string> Warnings { get; } = new();

        public void Warn(string message) => Warnings.Add(message);
    }

    private sealed class FakeTransport : ITransport
    {
        public int SendCount { get; private set; }

        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(new TransportResponse(200, new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty));
        }
    }
}
