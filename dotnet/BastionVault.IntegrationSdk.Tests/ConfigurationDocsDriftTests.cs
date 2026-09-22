using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// D3's drift gate (<c>docs/dotnet/configuration.md</c>, DR-0018 D-M11-4): reflects over
/// <see cref="ClientConfig"/> and <see cref="ConfigurationResolver"/> so a setting, an
/// environment variable, a validation code or a default drifting from the document fails a test
/// run instead of an eyeball.
/// </summary>
public sealed class ConfigurationDocsDriftTests
{
    /// <summary>
    /// <see cref="ClientConfig"/> properties that are not one of
    /// <c>specifications/02-client-configuration.md</c>'s settings (CFG-001's table has no row
    /// for them) — mirrors the reasoning already on each property in <c>ClientConfig.cs</c>.
    /// </summary>
    private static readonly HashSet<string> NonSettingProperties = new(StringComparer.Ordinal)
    {
        "AddressUri", "AddressIsClusterName", "IsInsecure", "CaCertificates",
        "ClientCertificate", "BatchMaxOperations", "Discovery", "Health",
    };

    /// <summary>
    /// CFG-001 rows with no <see cref="ClientConfig"/> property to reflect over: <c>Logger</c> is
    /// used and discarded at construction, <c>Transport</c> is consumed by the client itself.
    /// </summary>
    private static readonly string[] OptionsOnlySettings = ["Logger", "Transport"];

    private static string RepositoryRoot { get; } = new FixtureRepository().RepositoryRoot;

    private static string ConfigurationDocText { get; } =
        File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "dotnet", "configuration.md"));

    private static string ConfigurationResolverSource { get; } =
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "dotnet", "BastionVault.IntegrationSdk", "Internal", "ConfigurationResolver.cs"));

    [Fact]
    public void Every_ClientConfig_setting_is_documented_and_vice_versa()
    {
        IReadOnlyList<string> code = ClientConfigSettingNames();
        IReadOnlyList<string> documented = DocumentedSettingNames();

        string[] missingFromDocs = [.. code.Except(documented, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)];
        string[] missingFromCode = [.. documented.Except(code, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)];

        Assert.True(
            missingFromDocs.Length == 0 && missingFromCode.Length == 0,
            "docs/dotnet/configuration.md's Settings table has drifted from ClientConfig.\n"
                + "In code but not documented: " + string.Join(", ", missingFromDocs) + "\n"
                + "Documented but not in code: " + string.Join(", ", missingFromCode));
    }

    [Fact]
    public void Every_environment_variable_the_resolver_reads_is_documented_and_vice_versa()
    {
        string[] code = [.. ResolverEnvironmentVariables().OrderBy(name => name, StringComparer.Ordinal)];
        string[] documented = [.. DocumentedEnvironmentVariables().OrderBy(name => name, StringComparer.Ordinal)];

        string[] missingFromDocs = [.. code.Except(documented, StringComparer.Ordinal)];
        string[] missingFromCode = [.. documented.Except(code, StringComparer.Ordinal)];

        Assert.True(
            missingFromDocs.Length == 0 && missingFromCode.Length == 0,
            "docs/dotnet/configuration.md's environment variables have drifted from ConfigurationResolver.cs.\n"
                + "Read by the resolver but not documented: " + string.Join(", ", missingFromDocs) + "\n"
                + "Documented but never read by the resolver: " + string.Join(", ", missingFromCode));
    }

    [Fact]
    public void Every_BV_CONFIG_code_is_documented_and_vice_versa()
    {
        string[] code = [.. ConfigErrorCodes().OrderBy(name => name, StringComparer.Ordinal)];
        string[] documented = [.. DocumentedValidationCodes().OrderBy(name => name, StringComparer.Ordinal)];

        string[] missingFromDocs = [.. code.Except(documented, StringComparer.Ordinal)];
        string[] missingFromCode = [.. documented.Except(code, StringComparer.Ordinal)];

        Assert.True(
            missingFromDocs.Length == 0 && missingFromCode.Length == 0,
            "docs/dotnet/configuration.md's Validation errors table has drifted from ErrorCodes.\n"
                + "Defined in ErrorCodes but not documented: " + string.Join(", ", missingFromDocs) + "\n"
                + "Documented but not in ErrorCodes: " + string.Join(", ", missingFromCode));
    }

    [Fact]
    public void Documented_defaults_match_ConfigurationResolvers_actual_defaults()
    {
        // CFG-005's environment-free form, every option left unset: exactly "every built-in
        // default at once", the same values `new BastionVaultClient()` resolves to in a process
        // with no BASTIONVAULT_*/VAULT_* variables set.
        ClientConfig defaults = ConfigurationResolver.Resolve(new BastionVaultClientOptions(), EnvironmentSource.None);
        IReadOnlyDictionary<string, string> rows = SettingsRows();

        Dictionary<string, string> expectedSubstring = new(StringComparer.Ordinal)
        {
            ["Address"] = defaults.Address,
            ["TokenFile"] = "~/.vault-token",
            ["UseTokenHelper"] = FormatBool(defaults.UseTokenHelper),
            ["Namespace"] = "\"\"",
            ["CaCertReplacesSystemRoots"] = FormatBool(defaults.CaCertReplacesSystemRoots),
            ["TlsSkipVerify"] = FormatBool(defaults.TlsSkipVerify),
            ["AllowInsecureHttp"] = FormatBool(defaults.AllowInsecureHttp),
            ["Timeout"] = FormatSeconds(defaults.Timeout),
            ["ConnectTimeout"] = FormatSeconds(defaults.ConnectTimeout),
            ["ClusterDiscovery"] = FormatBool(defaults.ClusterDiscovery),
            ["DiscoveryProbeTimeout"] = FormatMilliseconds(defaults.DiscoveryProbeTimeout),
            ["ApiPrefix"] = defaults.ApiPrefix,
            ["MaxResponseBytes"] = defaults.MaxResponseBytes.ToString(CultureInfo.InvariantCulture),
            ["UseSystemProxy"] = FormatBool(defaults.UseSystemProxy),
            ["RetryPolicy"] = $"MaxAttempts={defaults.RetryPolicy.MaxAttempts}",
            ["RateGate"] = $"RatePerSecond={defaults.RateGate.RatePerSecond}",
        };

        List<string> failures = [];
        foreach ((string setting, string expectedText) in expectedSubstring)
        {
            if (!rows.TryGetValue(setting, out string? row))
            {
                failures.Add($"{setting}: no row in the Settings table (see the name-symmetry test).");
                continue;
            }

            if (!row.Contains(expectedText, StringComparison.Ordinal))
            {
                failures.Add($"{setting}: ConfigurationResolver's actual default is `{expectedText}`; not found in the documented row: {row}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ------------------------------------------------------------------ //
    // code-side extraction
    // ------------------------------------------------------------------ //

    private static IReadOnlyList<string> ClientConfigSettingNames()
    {
        return
        [
            .. typeof(ClientConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Where(name => !NonSettingProperties.Contains(name))
                .Concat(OptionsOnlySettings)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];
    }

    private static readonly Regex CodeEnvironmentVariableLiteral =
        new(@"""((?:BASTIONVAULT|VAULT)_[A-Z_]+)""", RegexOptions.Compiled);

    private static HashSet<string> ResolverEnvironmentVariables()
    {
        return [.. CodeEnvironmentVariableLiteral
            .Matches(ConfigurationResolverSource)
            .Select(match => match.Groups[1].Value)];
    }

    private static HashSet<string> ConfigErrorCodes()
    {
        return
        [
            .. typeof(ErrorCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Where(field => field.Name.StartsWith("Config", StringComparison.Ordinal))
                .Select(field => (string)field.GetRawConstantValue()!),
        ];
    }

    // ------------------------------------------------------------------ //
    // docs-side extraction
    // ------------------------------------------------------------------ //

    private static readonly Regex DocEnvironmentVariableLiteral =
        new(@"`((?:BASTIONVAULT|VAULT)_[A-Z_]+)`", RegexOptions.Compiled);

    private static readonly Regex DocValidationCodeLiteral =
        new(@"`(BV-CONFIG-\d+)`", RegexOptions.Compiled);

    private static IReadOnlyList<string> DocumentedSettingNames()
    {
        return [.. ParseSettingsRows().Keys];
    }

    private static IReadOnlyDictionary<string, string> SettingsRows()
    {
        return ParseSettingsRows();
    }

    private static Dictionary<string, string> ParseSettingsRows()
    {
        string section = Section(ConfigurationDocText, "## Settings", "## Precedence");
        Dictionary<string, string> rows = new(StringComparer.Ordinal);
        foreach (string line in section.Split('\n'))
        {
            (string name, string text)? parsed = ParseRow(line);
            if (parsed is { } row)
            {
                rows[row.name] = row.text;
            }
        }

        return rows;
    }

    private static (string name, string text)? ParseRow(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '|' || trimmed[^1] != '|')
        {
            return null;
        }

        string[] cells = [.. trimmed[1..^1].Split('|').Select(cell => cell.Trim())];
        if (cells.Length == 0)
        {
            return null;
        }

        Match nameMatch = Regex.Match(cells[0], @"^`(\w+)`$");
        return nameMatch.Success ? (nameMatch.Groups[1].Value, trimmed) : null;
    }

    private static HashSet<string> DocumentedEnvironmentVariables()
    {
        return [.. DocEnvironmentVariableLiteral
            .Matches(ConfigurationDocText)
            .Select(match => match.Groups[1].Value)];
    }

    private static HashSet<string> DocumentedValidationCodes()
    {
        string section = Section(ConfigurationDocText, "## Validation errors", "## Sample");
        return [.. DocValidationCodeLiteral.Matches(section).Select(match => match.Groups[1].Value)];
    }

    private static string Section(string text, string heading, string? nextHeading)
    {
        int start = text.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("docs/dotnet/configuration.md has no '" + heading + "' heading");
        }

        start += heading.Length;
        int end = nextHeading is null ? text.Length : text.IndexOf(nextHeading, start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = text.Length;
        }

        return text[start..end];
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatSeconds(TimeSpan value)
    {
        return $"{(int)value.TotalSeconds}s";
    }

    private static string FormatMilliseconds(TimeSpan value)
    {
        return $"{(int)value.TotalMilliseconds}ms";
    }
}
