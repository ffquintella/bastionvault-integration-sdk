using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Additional tests filling out branch/line coverage for the M1a configuration model that the
/// requirement-marked tests in <see cref="ClientConfigurationTests"/> do not already reach
/// (CNF-010..012: every branch, including error-handling and defaulting, is in scope).
/// </summary>
public sealed class ClientConfigurationCoverageTests
{
    [Fact]
    public void Every_setting_not_explicitly_supplied_falls_back_to_its_documented_default()
    {
        BastionVaultClient client = new(new BastionVaultClientOptions(), EnvironmentSource.None);
        ClientConfig config = client.Config;

        Assert.False(config.UseTokenHelper);
        Assert.EndsWith(".vault-token", config.TokenFile, StringComparison.Ordinal);
        Assert.Null(config.CaCertPath);
        Assert.Null(config.CaCertPem);
        Assert.Null(config.ClientCertPath);
        Assert.Null(config.ClientKeyPath);
        Assert.Equal(TimeSpan.FromSeconds(10), config.ConnectTimeout);
        Assert.True(config.ClusterDiscovery);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), config.DiscoveryProbeTimeout);
        Assert.Equal($"bastionvault-sdk-dotnet/{SdkInfo.SdkVersion}", config.UserAgent);
        Assert.Equal("v1", config.ApiPrefix);
        Assert.Null(config.ClientCertificate);
        Assert.Empty(config.Headers);
    }

    [Fact]
    public void Explicit_options_override_string_and_bool_settings_that_have_no_dedicated_requirement_test()
    {
        BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                UseTokenHelper = true,
                TokenFile = "/tmp/does-not-need-to-exist-for-this-assertion.txt",
                ConnectTimeout = TimeSpan.FromSeconds(7),
                ClusterDiscovery = false,
                DiscoveryProbeTimeout = TimeSpan.FromMilliseconds(250),
                UserAgent = "custom-agent/1.0",
                ApiPrefix = "v2",
            },
            EnvironmentSource.None);

        Assert.True(client.Config.UseTokenHelper);
        Assert.Equal(TimeSpan.FromSeconds(7), client.Config.ConnectTimeout);
        Assert.False(client.Config.ClusterDiscovery);
        Assert.Equal(TimeSpan.FromMilliseconds(250), client.Config.DiscoveryProbeTimeout);
        Assert.Equal("custom-agent/1.0", client.Config.UserAgent);
        Assert.Equal("v2", client.Config.ApiPrefix);
    }

    [Fact]
    public void ConnectTimeout_zero_or_negative_raises_bv_config_003()
    {
        BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
            new BastionVaultClientOptions { ConnectTimeout = TimeSpan.Zero },
            EnvironmentSource.None));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, exception.Code);
        Assert.Equal("ConnectTimeout", exception.Details["setting"]);
    }

    [Fact]
    public void ClusterDiscovery_no_env_var_disables_discovery_and_explicit_option_wins_over_it()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["VAULT_NO_CLUSTER_DISCOVERY"] = "true" });
        BastionVaultClient disabled = new(new BastionVaultClientOptions(), environment);
        Assert.False(disabled.Config.ClusterDiscovery);

        BastionVaultClient explicitOverride = new(new BastionVaultClientOptions { ClusterDiscovery = true }, environment);
        Assert.True(explicitOverride.Config.ClusterDiscovery);
    }

    [Fact]
    public void DiscoveryProbeTimeout_is_read_from_its_environment_variable()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_DISCOVERY_PROBE_TIMEOUT"] = "250ms" });
        BastionVaultClient client = new(new BastionVaultClientOptions(), environment);
        Assert.Equal(TimeSpan.FromMilliseconds(250), client.Config.DiscoveryProbeTimeout);
    }

    [Fact]
    public void UseTokenHelper_and_client_cert_paths_are_read_from_their_environment_variables()
    {
        string certPath = Path.GetTempFileName();
        string keyPath = Path.GetTempFileName();
        try
        {
            (string certPem, string keyPem) = CreateSelfSignedPem();
            File.WriteAllText(certPath, certPem);
            File.WriteAllText(keyPath, keyPem);

            EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string>
            {
                ["BASTIONVAULT_USE_TOKEN_HELPER"] = "true",
                ["VAULT_CLIENT_CERT"] = certPath,
                ["VAULT_CLIENT_KEY"] = keyPath,
            });

            BastionVaultClient client = new(new BastionVaultClientOptions(), environment);

            Assert.True(client.Config.UseTokenHelper);
            Assert.Equal(certPath, client.Config.ClientCertPath);
            Assert.Equal(keyPath, client.Config.ClientKeyPath);
            Assert.NotNull(client.Config.ClientCertificate);
        }
        finally
        {
            File.Delete(certPath);
            File.Delete(keyPath);
        }
    }

    [Fact]
    public void Invalid_client_certificate_pem_raises_bv_config_006()
    {
        string certPath = Path.GetTempFileName();
        string keyPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(certPath, "not a certificate");
            File.WriteAllText(keyPath, "not a key");

            BastionVaultException exception = Assert.Throws<BastionVaultException>(() => new BastionVaultClient(
                new BastionVaultClientOptions { ClientCertPath = certPath, ClientKeyPath = keyPath },
                EnvironmentSource.None));

            Assert.Equal(ErrorCodes.ConfigInvalidPem, exception.Code);
        }
        finally
        {
            File.Delete(certPath);
            File.Delete(keyPath);
        }
    }

    [Fact]
    public void CaCertPath_file_is_read_and_parsed_when_no_inline_pem_is_given()
    {
        string caCertPath = Path.GetTempFileName();
        try
        {
            (string certPem, _) = CreateSelfSignedPem();
            File.WriteAllText(caCertPath, certPem);

            EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["VAULT_CACERT"] = caCertPath });
            BastionVaultClient client = new(new BastionVaultClientOptions(), environment);

            Assert.Equal(caCertPath, client.Config.CaCertPath);
            Assert.NotNull(client.Config.CaCertificates);
            Assert.Single(client.Config.CaCertificates!);
        }
        finally
        {
            File.Delete(caCertPath);
        }
    }

    [Fact]
    public void RetryPolicy_explicit_object_bypasses_the_environment_entirely()
    {
        RetryPolicy explicitPolicy = new() { MaxAttempts = 9 };
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_MAX_RETRIES"] = "2" });

        BastionVaultClient client = new(new BastionVaultClientOptions { RetryPolicy = explicitPolicy }, environment);

        Assert.Same(explicitPolicy, client.Config.RetryPolicy);
        Assert.Equal(9, client.Config.RetryPolicy.MaxAttempts);
    }

    [Fact]
    public void Invalid_max_retries_environment_value_raises_bv_config_003()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_MAX_RETRIES"] = "not-a-number" });
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(new BastionVaultClientOptions(), environment));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, exception.Code);
        Assert.Equal("RetryPolicy.MaxAttempts", exception.Details["setting"]);
    }

    [Fact]
    public void Empty_duration_environment_value_raises_bv_config_003()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_TIMEOUT"] = "" });
        BastionVaultException exception = Assert.Throws<BastionVaultException>(
            () => new BastionVaultClient(new BastionVaultClientOptions(), environment));

        Assert.Equal(ErrorCodes.ConfigInvalidSettingValue, exception.Code);
    }

    [Fact]
    public void Duration_supports_microseconds()
    {
        EnvironmentSource environment = EnvironmentSource.FromMap(new Dictionary<string, string> { ["BASTIONVAULT_DISCOVERY_PROBE_TIMEOUT"] = "500us" });
        BastionVaultClient client = new(new BastionVaultClientOptions(), environment);
        Assert.Equal(TimeSpan.FromTicks(5000), client.Config.DiscoveryProbeTimeout);
    }

    [Fact]
    public void Token_file_read_failure_is_treated_as_no_token_not_an_error()
    {
        string tokenFile = Path.GetTempFileName();
        try
        {
            using FileStream lockHandle = new(tokenFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            BastionVaultClient client = new(
                new BastionVaultClientOptions { UseTokenHelper = true, TokenFile = tokenFile },
                EnvironmentSource.None);

            Assert.False(client.Config.Token.HasValue);
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    public void BastionVaultException_exposes_every_request_scoped_field_and_formats_all_ToString_segments()
    {
        Dictionary<string, object?> details = new(StringComparer.Ordinal) { ["cas_expected"] = 3 };
        InvalidOperationException cause = new("inner failure");
        BastionVaultException exception = new(
            "BV-SERVER-002",
            ErrorCategory.ServerState,
            "The server is temporarily unavailable.",
            "Retry shortly.",
            retryable: true,
            attempts: 2,
            serverMessage: "upstream unhealthy",
            serverErrors: new[] { "upstream unhealthy" },
            statusCode: 503,
            retryAfter: TimeSpan.FromSeconds(1),
            method: "GET",
            path: "[ns=eng] secret/data/x",
            address: "vault.example.com:8200",
            details: details,
            cause: cause);

        Assert.Equal("BV-SERVER-002", exception.Code);
        Assert.Equal(ErrorCategory.ServerState, exception.Category);
        Assert.Equal("Retry shortly.", exception.Hint);
        Assert.Equal("upstream unhealthy", exception.ServerMessage);
        Assert.Equal(new[] { "upstream unhealthy" }, exception.ServerErrors);
        Assert.Equal(503, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), exception.RetryAfter);
        Assert.True(exception.Retryable);
        Assert.Equal("GET", exception.Method);
        Assert.Equal("[ns=eng] secret/data/x", exception.Path);
        Assert.Equal("vault.example.com:8200", exception.Address);
        Assert.Equal(2, exception.Attempts);
        Assert.Equal(3, exception.Details["cas_expected"]);
        Assert.Same(cause, exception.Cause);
        Assert.True((DateTimeOffset.UtcNow - exception.Timestamp) < TimeSpan.FromMinutes(1));

        string text = exception.ToString();
        Assert.StartsWith("BV-SERVER-002: The server is temporarily unavailable. — Retry shortly.", text, StringComparison.Ordinal);
        Assert.Contains("[HTTP 503 GET [ns=eng] secret/data/x]", text, StringComparison.Ordinal);
        Assert.Contains("(server: \"upstream unhealthy\")", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void SecretString_equality_and_default_redaction_behave_as_documented()
    {
        SecretString first = new("s.abc");
        SecretString second = new("s.abc");
        SecretString different = new("s.xyz");

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.False(first.Equals(different));
        Assert.False(first.Equals((object?)null));
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("[REDACTED]", first.ToString());
        Assert.DoesNotContain("s.abc", first.ToString(), StringComparison.Ordinal);
        Assert.False(SecretString.Empty.HasValue);
        Assert.Null(SecretString.Empty.Reveal());
    }

    [Fact]
    public void RequestOptions_carries_a_cancellation_token_and_transport_records_expose_their_fields()
    {
        using CancellationTokenSource source = new();
        RequestOptions options = new() { CancellationToken = source.Token };
        Assert.Equal(source.Token, options.CancellationToken);

        Uri uri = new("https://vault.example.com:8200/v1/secret/data/x");
        Dictionary<string, string> headers = new(StringComparer.Ordinal) { ["X-Test"] = "1" };
        ReadOnlyMemory<byte> body = new byte[] { 1, 2, 3 };
        TransportRequest request = new("GET", uri, headers, body);
        Assert.Equal("GET", request.Method);
        Assert.Equal(uri, request.Uri);
        Assert.Same(headers, request.Headers);
        Assert.Equal(body.ToArray(), request.Body.ToArray());

        TransportResponse response = new(200, headers, body);
        Assert.Equal(200, response.StatusCode);
        Assert.Same(headers, response.Headers);
        Assert.Equal(body.ToArray(), response.Body.ToArray());
    }

    private static (string CertPem, string KeyPem) CreateSelfSignedPem()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=bastionvault-test", key, HashAlgorithmName.SHA256);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string certPem = certificate.ExportCertificatePem();
        string keyPem = key.ExportECPrivateKeyPem();
        return (certPem, keyPem);
    }
}
