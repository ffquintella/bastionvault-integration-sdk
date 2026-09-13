using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Resolves a <see cref="BastionVaultClientOptions"/> plus an <see cref="EnvironmentSource"/> into an
/// immutable, fully validated <see cref="ClientConfig"/> (CFG-001..018). Resolution runs in the
/// settings-table declaration order from <c>specifications/02-client-configuration.md</c>; a
/// malformed value raises <c>BV-CONFIG-003</c> naming the setting before any validation runs.
/// Validation then runs in the fixed order from <c>decisions/0003-m1a-configuration.md</c> D-M1a-5,
/// stopping at the first failure.
/// </summary>
internal static class ConfigurationResolver
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-BastionVault-Token",
        "X-Vault-Token",
        "Authorization",
        "Cookie",
        "X-BastionVault-Namespace",
        "Host",
        "Content-Length",
    };

    private static readonly Regex DurationTermPattern = new(
        @"\G(\d+(?:\.\d+)?)(ns|us|µs|ms|s|m|h)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static ClientConfig Resolve(BastionVaultClientOptions options, EnvironmentSource environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        // ---- Resolution phase: settings-table declaration order (CFG-001, D-M1a-4). ----
        string rawAddress = ResolveString(options.Address, environment, "BASTIONVAULT_ADDR", "VAULT_ADDR") ?? "https://127.0.0.1:8200";
        string? explicitToken = options.Token;
        string? envToken = ResolveString(null, environment, "BASTIONVAULT_TOKEN", "VAULT_TOKEN");
        string tokenFile = ResolveString(options.TokenFile, environment, "BASTIONVAULT_TOKEN_FILE") ?? DefaultTokenFilePath();
        bool useTokenHelper = ResolveBool("UseTokenHelper", options.UseTokenHelper, environment, false, "BASTIONVAULT_USE_TOKEN_HELPER");
        string rawNamespace = ResolveString(options.Namespace, environment, "BASTIONVAULT_NAMESPACE", "VAULT_NAMESPACE") ?? string.Empty;
        string? caCertPath = ResolveString(options.CaCertPath, environment, "BASTIONVAULT_CACERT", "VAULT_CACERT");
        string? caCertPem = options.CaCertPem;
        bool caCertReplacesSystemRoots = options.CaCertReplacesSystemRoots ?? false;
        string? clientCertPath = ResolveString(options.ClientCertPath, environment, "BASTIONVAULT_CLIENT_CERT", "VAULT_CLIENT_CERT");
        string? clientKeyPath = ResolveString(options.ClientKeyPath, environment, "BASTIONVAULT_CLIENT_KEY", "VAULT_CLIENT_KEY");
        bool tlsSkipVerify = ResolveBool("TlsSkipVerify", options.TlsSkipVerify, environment, false, "BASTIONVAULT_SKIP_VERIFY", "VAULT_SKIP_VERIFY");
        string? tlsServerName = ResolveString(options.TlsServerName, environment, "BASTIONVAULT_TLS_SERVER_NAME", "VAULT_TLS_SERVER_NAME");
        bool allowInsecureHttp = ResolveBool("AllowInsecureHttp", options.AllowInsecureHttp, environment, false, "BASTIONVAULT_ALLOW_INSECURE_HTTP");
        TimeSpan timeout = ResolveDuration("Timeout", options.Timeout, environment, TimeSpan.FromSeconds(30), "BASTIONVAULT_TIMEOUT", "VAULT_CLIENT_TIMEOUT");
        TimeSpan connectTimeout = ResolveDuration("ConnectTimeout", options.ConnectTimeout, environment, TimeSpan.FromSeconds(10), "BASTIONVAULT_CONNECT_TIMEOUT");
        RetryPolicy retryPolicy = ResolveRetryPolicy(options.RetryPolicy, environment);
        RateGate rateGate = ResolveRateGate(options.RateGate, environment); // D-M1a-13
        bool clusterDiscoveryDisabled = ResolveBool("ClusterDiscovery", null, environment, false, "BASTIONVAULT_NO_CLUSTER_DISCOVERY", "VAULT_NO_CLUSTER_DISCOVERY");
        bool clusterDiscovery = options.ClusterDiscovery ?? !clusterDiscoveryDisabled;
        TimeSpan discoveryProbeTimeout = ResolveDuration("DiscoveryProbeTimeout", options.DiscoveryProbeTimeout, environment, TimeSpan.FromMilliseconds(1500), "BASTIONVAULT_DISCOVERY_PROBE_TIMEOUT");
        // Defensive copy (CFG-002, D-M1a-2): ClientConfig must never alias a caller-owned mutable
        // dictionary, or a caller could mutate a "reserved header rejected" fact out from under an
        // already-constructed, already-validated Client. A case-insensitive comparer on the stored
        // copy also matches the case-insensitive reserved-header check below and whatever M1b's
        // header-merge logic does later.
        IReadOnlyDictionary<string, string> headers = CopyHeaders(options.Headers);
        string userAgent = options.UserAgent ?? $"bastionvault-sdk-dotnet/{SdkInfo.SdkVersion}";
        string apiPrefix = options.ApiPrefix ?? "v1";
        // AutoRenew (D-M1a-13): no environment variable; materialised as a disabled value unless
        // the caller explicitly opts in via options — the renewal loop itself is M2.
        AutoRenewPolicy autoRenew = options.AutoRenew ?? new AutoRenewPolicy();
        // MaxResponseBytes, UseSystemProxy (D-M1b-13): no environment variable.
        long maxResponseBytes = options.MaxResponseBytes ?? 134217728L;
        bool useSystemProxy = options.UseSystemProxy ?? false;
        // Logger, Transport (D-M1a-13): no environment variable. Logger is used directly, below,
        // for the CNF-030 warning (default NoOpClientLogger); Transport has no M1a default and is
        // read straight from options by BastionVaultClient's constructor.

        // ---- Validation phase: fixed order, first failure wins (D-M1a-5). ----
        (Uri? addressUri, bool isClusterName) = ParseAddress(rawAddress); // 1: BV-CONFIG-001

        if (addressUri is not null
            && string.Equals(addressUri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !allowInsecureHttp
            && !IsLoopbackHost(addressUri.Host))
        {
            throw ConfigError(ErrorCodes.ConfigInsecureHttpNotAllowed, ConfigCatalogue.InsecureHttpNotAllowedMessage, ConfigCatalogue.InsecureHttpNotAllowedHint); // 2
        }

        string @namespace = ValidateNamespace(rawNamespace); // 3: BV-CONFIG-007

        ValidateHeaders(headers); // 4: BV-CONFIG-008

        if (timeout <= TimeSpan.Zero)
        {
            throw ConfigError(
                ErrorCodes.ConfigInvalidSettingValue,
                ConfigCatalogue.InvalidSettingValueMessage,
                ConfigCatalogue.InvalidSettingValueHint,
                Details("setting", "Timeout")); // 5
        }

        if (connectTimeout <= TimeSpan.Zero)
        {
            throw ConfigError(
                ErrorCodes.ConfigInvalidSettingValue,
                ConfigCatalogue.InvalidSettingValueMessage,
                ConfigCatalogue.InvalidSettingValueHint,
                Details("setting", "ConnectTimeout")); // 5
        }

        bool clientCertPathSet = clientCertPath is not null;
        bool clientKeyPathSet = clientKeyPath is not null;
        if (clientCertPathSet != clientKeyPathSet)
        {
            throw ConfigError(ErrorCodes.ConfigClientCertIncomplete, ConfigCatalogue.ClientCertIncompleteMessage, ConfigCatalogue.ClientCertIncompleteHint); // 6
        }

        if (caCertPem is null)
        {
            EnsureFileReadable(caCertPath); // 7, position 1
        }

        EnsureFileReadable(clientCertPath); // 7, position 2
        EnsureFileReadable(clientKeyPath); // 7, position 3

        X509Certificate2Collection? caCertificates = ParseCaCertificates(caCertPem, caCertPath); // 8, position 1
        X509Certificate2? clientCertificate = ParseClientCertificate(clientCertPath, clientKeyPath); // 8, position 2/3

        bool isInsecure = false;
        if (tlsSkipVerify)
        {
            (options.Logger ?? NoOpClientLogger.Instance).Warn(
                "TLS certificate verification is disabled (TlsSkipVerify=true); this connection is insecure."); // 9: CNF-030
            isInsecure = true;
        }

        SecretString token = ResolveToken(explicitToken, envToken, useTokenHelper, tokenFile);

        return new ClientConfig(
            rawAddress,
            addressUri,
            isClusterName,
            token,
            tokenFile,
            useTokenHelper,
            @namespace,
            caCertPath,
            caCertPem,
            caCertReplacesSystemRoots,
            clientCertPath,
            clientKeyPath,
            tlsSkipVerify,
            tlsServerName,
            allowInsecureHttp,
            timeout,
            connectTimeout,
            retryPolicy,
            rateGate,
            clusterDiscovery,
            discoveryProbeTimeout,
            headers,
            userAgent,
            apiPrefix,
            autoRenew,
            isInsecure,
            caCertificates,
            clientCertificate,
            maxResponseBytes,
            useSystemProxy);
    }

    private static string? ResolveString(string? explicitValue, EnvironmentSource environment, params string[] environmentVariables)
    {
        if (explicitValue is not null)
        {
            return explicitValue;
        }

        foreach (string name in environmentVariables)
        {
            if (environment.TryGetValue(name, out string? raw) && raw is not null)
            {
                return raw;
            }
        }

        return null;
    }

    private static bool ResolveBool(string settingName, bool? explicitValue, EnvironmentSource environment, bool defaultValue, params string[] environmentVariables)
    {
        if (explicitValue is not null)
        {
            return explicitValue.Value;
        }

        foreach (string name in environmentVariables)
        {
            if (environment.TryGetValue(name, out string? raw) && raw is not null)
            {
                return ParseBoolean(settingName, raw);
            }
        }

        return defaultValue;
    }

    private static TimeSpan ResolveDuration(string settingName, TimeSpan? explicitValue, EnvironmentSource environment, TimeSpan defaultValue, params string[] environmentVariables)
    {
        if (explicitValue is not null)
        {
            return explicitValue.Value;
        }

        foreach (string name in environmentVariables)
        {
            if (environment.TryGetValue(name, out string? raw) && raw is not null)
            {
                return ParseDuration(settingName, raw);
            }
        }

        return defaultValue;
    }

    private static RetryPolicy ResolveRetryPolicy(RetryPolicy? explicitValue, EnvironmentSource environment)
    {
        if (explicitValue is not null)
        {
            return explicitValue;
        }

        foreach (string name in new[] { "BASTIONVAULT_MAX_RETRIES", "VAULT_MAX_RETRIES" })
        {
            if (environment.TryGetValue(name, out string? raw) && raw is not null)
            {
                // D-M1a-18: Details.setting is developer-facing API and must carry the canonical
                // settings-table field name (RetryPolicy.MaxAttempts, per CFG-050), not the env
                // var's own vocabulary ("MaxRetries").
                int maxRetries = ParseInt("RetryPolicy.MaxAttempts", raw);

                // D-M1a-4: BASTIONVAULT_MAX_RETRIES/VAULT_MAX_RETRIES set MaxAttempts = value + 1.
                return new RetryPolicy { MaxAttempts = maxRetries + 1 };
            }
        }

        return new RetryPolicy();
    }

    private static RateGate ResolveRateGate(RateGate? explicitValue, EnvironmentSource environment)
    {
        if (explicitValue is not null)
        {
            return explicitValue;
        }

        // D-M1a-18/D-M1a-20: canonical field name is RatePerSecond (specifications/14-batch-and-
        // request-efficiency.md:13, Appendix C's settings example); the .NET property was renamed
        // to match so the Details.setting string and the public API agree.
        int ratePerSecond = ResolveRateGateComponent("RateGate.RatePerSecond", environment, 8, "BASTIONVAULT_RATE_PER_SEC");
        int burst = ResolveRateGateComponent("RateGate.Burst", environment, 16, "BASTIONVAULT_RATE_BURST");

        return new RateGate { RatePerSecond = ratePerSecond, Burst = burst };
    }

    private static int ResolveRateGateComponent(string settingName, EnvironmentSource environment, int defaultValue, string environmentVariable)
    {
        if (!environment.TryGetValue(environmentVariable, out string? raw) || raw is null)
        {
            return defaultValue;
        }

        int value = ParseInt(settingName, raw); // same BV-CONFIG-003 shape as BASTIONVAULT_MAX_RETRIES
        if (value < 0)
        {
            throw ConfigError(
                ErrorCodes.ConfigInvalidSettingValue,
                ConfigCatalogue.InvalidSettingValueMessage,
                ConfigCatalogue.InvalidSettingValueHint,
                Details("setting", settingName));
        }

        return value;
    }

    /// <summary>
    /// Parses an integer-valued environment setting, raising <c>BV-CONFIG-003</c> naming
    /// <paramref name="settingName"/> on failure. Shared by every integer setting
    /// (<c>BASTIONVAULT_MAX_RETRIES</c>/<c>VAULT_MAX_RETRIES</c>, <c>BASTIONVAULT_RATE_PER_SEC</c>,
    /// <c>BASTIONVAULT_RATE_BURST</c>) so they all fail identically.
    /// </summary>
    private static int ParseInt(string settingName, string raw)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw ConfigError(
                ErrorCodes.ConfigInvalidSettingValue,
                ConfigCatalogue.InvalidSettingValueMessage,
                ConfigCatalogue.InvalidSettingValueHint,
                Details("setting", settingName));
        }

        return value;
    }

    private static SecretString ResolveToken(string? explicitToken, string? envToken, bool useTokenHelper, string tokenFile)
    {
        string? value = explicitToken ?? envToken;
        if (value is null && useTokenHelper)
        {
            try
            {
                if (File.Exists(tokenFile))
                {
                    string fileValue = File.ReadAllText(tokenFile).Trim();
                    if (fileValue.Length > 0)
                    {
                        value = fileValue;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // CFG-013: an unreadable/absent token file is silently treated as "no token".
            }
        }

        return new SecretString(value);
    }

    internal static bool ParseBoolean(string settingName, string raw)
    {
        string trimmed = raw.Trim();
        switch (trimmed.ToUpperInvariant())
        {
            case "":
            case "0":
            case "FALSE":
            case "NO":
            case "OFF":
                return false;
            case "1":
            case "TRUE":
            case "YES":
            case "ON":
                return true;
            default:
                throw ConfigError(
                    ErrorCodes.ConfigInvalidSettingValue,
                    ConfigCatalogue.InvalidSettingValueMessage,
                    ConfigCatalogue.InvalidSettingValueHint,
                    Details("setting", settingName));
        }
    }

    internal static TimeSpan ParseDuration(string settingName, string raw)
    {
        if (raw.Length == 0)
        {
            throw InvalidDuration(settingName);
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        int position = 0;
        TimeSpan total = TimeSpan.Zero;
        while (position < raw.Length)
        {
            Match match = DurationTermPattern.Match(raw, position);
            if (!match.Success)
            {
                throw InvalidDuration(settingName);
            }

            double value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            total += match.Groups[2].Value switch
            {
                "ns" => TimeSpan.FromTicks((long)(value / 100)),
                "us" or "µs" => TimeSpan.FromTicks((long)(value * 10)),
                "ms" => TimeSpan.FromMilliseconds(value),
                "s" => TimeSpan.FromSeconds(value),
                "m" => TimeSpan.FromMinutes(value),
                _ => TimeSpan.FromHours(value), // "h": the only remaining alternative DurationTermPattern can capture.
            };
            position += match.Length;
        }

        return total;

        BastionVaultException InvalidDuration(string setting) => ConfigError(
            ErrorCodes.ConfigInvalidSettingValue,
            ConfigCatalogue.InvalidSettingValueMessage,
            ConfigCatalogue.InvalidSettingValueHint,
            Details("setting", setting));
    }

    private static (Uri? Uri, bool IsClusterName) ParseAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw ConfigError(ErrorCodes.ConfigInvalidAddress, ConfigCatalogue.InvalidAddressMessage, ConfigCatalogue.InvalidAddressHint);
        }

        if (raw.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
                || (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
            {
                throw ConfigError(ErrorCodes.ConfigInvalidAddress, ConfigCatalogue.InvalidAddressMessage, ConfigCatalogue.InvalidAddressHint);
            }

            return (uri, false);
        }

        if (raw.Any(char.IsWhiteSpace) || raw.Any(char.IsControl))
        {
            throw ConfigError(ErrorCodes.ConfigInvalidAddress, ConfigCatalogue.InvalidAddressMessage, ConfigCatalogue.InvalidAddressHint);
        }

        return (null, true);
    }

    private static bool IsLoopbackHost(string host)
    {
        string trimmed = host.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1];
        }

        return string.Equals(trimmed, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(trimmed, "::1", StringComparison.Ordinal)
            || string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateNamespace(string raw)
    {
        string trimmed = raw.TrimEnd('/');
        if (trimmed.StartsWith('/')
            || trimmed.Contains("//", StringComparison.Ordinal)
            || trimmed.Any(char.IsWhiteSpace)
            || trimmed.Any(char.IsControl))
        {
            throw ConfigError(ErrorCodes.ConfigInvalidNamespace, ConfigCatalogue.InvalidNamespaceMessage, ConfigCatalogue.InvalidNamespaceHint);
        }

        return trimmed;
    }

    private static Dictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string>? source)
    {
        Dictionary<string, string> copy = new(StringComparer.OrdinalIgnoreCase);
        if (source is not null)
        {
            foreach (KeyValuePair<string, string> pair in source)
            {
                copy[pair.Key] = pair.Value;
            }
        }

        return copy;
    }

    private static void ValidateHeaders(IReadOnlyDictionary<string, string> headers)
    {
        foreach (string name in headers.Keys)
        {
            if (ReservedHeaders.Contains(name))
            {
                throw ConfigError(ErrorCodes.ConfigReservedHeader, ConfigCatalogue.ReservedHeaderMessage, ConfigCatalogue.ReservedHeaderHint);
            }
        }
    }

    private static void EnsureFileReadable(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // CFG-013 requires a BV-CONFIG-* error, never a generic runtime exception, for any
            // unusable path: missing/unreadable (IOException/UnauthorizedAccessException) as well
            // as structurally invalid paths (ArgumentException for embedded NUL/invalid chars,
            // NotSupportedException for a bad ":" in the path on some platforms).
            throw ConfigError(
                ErrorCodes.ConfigFileNotReadable,
                ConfigCatalogue.FileNotReadableMessage,
                ConfigCatalogue.FileNotReadableHint,
                Details("path", path),
                exception);
        }
    }

    private static X509Certificate2Collection? ParseCaCertificates(string? pem, string? path)
    {
        string? content = pem;
        if (content is null && path is not null)
        {
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Reached only if EnsureFileReadable's earlier check raced with the file (e.g. it was
                // deleted between checks); still must surface as BV-CONFIG-005, not a raw exception.
                throw ConfigError(
                    ErrorCodes.ConfigFileNotReadable,
                    ConfigCatalogue.FileNotReadableMessage,
                    ConfigCatalogue.FileNotReadableHint,
                    Details("path", path),
                    exception);
            }
        }

        if (content is null)
        {
            return null;
        }

        try
        {
            X509Certificate2Collection collection = new();
            collection.ImportFromPem(content);
            if (collection.Count == 0)
            {
                // ImportFromPem silently imports nothing for content with no CERTIFICATE block,
                // rather than throwing; CFG-014 requires this to be a construction-time failure.
                throw ConfigError(ErrorCodes.ConfigInvalidPem, ConfigCatalogue.InvalidPemMessage, ConfigCatalogue.InvalidPemHint);
            }

            return collection;
        }
        catch (CryptographicException exception)
        {
            throw ConfigError(ErrorCodes.ConfigInvalidPem, ConfigCatalogue.InvalidPemMessage, ConfigCatalogue.InvalidPemHint, cause: exception);
        }
    }

    private static X509Certificate2? ParseClientCertificate(string? certPath, string? keyPath)
    {
        if (certPath is null || keyPath is null)
        {
            return null;
        }

        try
        {
            // Reloaded via a fresh PFX export (X509KeyStorageFlags.Exportable) rather than kept as
            // CreateFromPemFile's own result: on Windows, every RSA/ECDSA key produced while parsing
            // a PEM private key is backed by an ephemeral CNG key, and SChannel refuses to present an
            // ephemeral-key certificate as a TLS client credential ("the platform does not support
            // ephemeral keys"). Re-importing from a PFX byte export gives the certificate its own
            // non-ephemeral key copy, which both SChannel and OpenSSL (Linux) accept as a client
          // certificate (CFG-044). See the identical reasoning in InProcessHttpsMockServer's own
            // certificate generation.
            using X509Certificate2 parsed = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            return X509CertificateLoader.LoadPkcs12(parsed.Export(X509ContentType.Pfx), string.Empty, X509KeyStorageFlags.Exportable);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw ConfigError(ErrorCodes.ConfigInvalidPem, ConfigCatalogue.InvalidPemMessage, ConfigCatalogue.InvalidPemHint, cause: exception);
        }
    }

    private static string DefaultTokenFilePath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vault-token");

    private static Dictionary<string, object?> Details(string key, object? value)
        => new(StringComparer.Ordinal) { [key] = value };

    private static BastionVaultException ConfigError(
        string code,
        string message,
        string hint,
        IReadOnlyDictionary<string, object?>? details = null,
        Exception? cause = null)
        => BastionVaultException.Config(code, message, hint, details, cause);
}
