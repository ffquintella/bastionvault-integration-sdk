using System.Text.Json;
using System.Xml;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness.Operations;

/// <summary>
/// Shared construction of a real <see cref="BastionVaultClient"/> from a fixture's <c>client</c>
/// block, so every operation handler resolves settings identically (used by
/// <see cref="ClientConstructOperation"/> and the <c>Logical.*</c> operations alike).
/// </summary>
internal static class FixtureClientBuilder
{
    public static BastionVaultClientOptions BuildOptions(FixtureConfiguration configuration, ITransport transport)
    {
        BastionVaultClientOptions options = new()
        {
            Address = configuration.Address,
            Token = configuration.Token,
            Namespace = configuration.Namespace,
            ApiPrefix = configuration.ApiPrefix,
            Transport = transport,
            Clock = FixtureClock.Instance,
            JitterSource = FixtureJitterSource.Instance,
        };

        ApplySettings(options, configuration.Settings);
        return options;
    }

    private static void ApplySettings(BastionVaultClientOptions options, JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (settings.TryGetProperty("Headers", out JsonElement headers) && headers.ValueKind == JsonValueKind.Object)
        {
            options.Headers = headers.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        }

        if (TryGetBool(settings, "AllowInsecureHttp", out bool allowInsecureHttp))
        {
            options.AllowInsecureHttp = allowInsecureHttp;
        }

        if (TryGetBool(settings, "TlsSkipVerify", out bool tlsSkipVerify))
        {
            options.TlsSkipVerify = tlsSkipVerify;
        }

        if (settings.TryGetProperty("MaxResponseBytes", out JsonElement maxResponseBytes) && maxResponseBytes.ValueKind == JsonValueKind.Number)
        {
            options.MaxResponseBytes = maxResponseBytes.GetInt64();
        }

        if (TryGetBool(settings, "UseSystemProxy", out bool useSystemProxy))
        {
            options.UseSystemProxy = useSystemProxy;
        }

        if (settings.TryGetProperty("RetryPolicy", out JsonElement retry) && retry.ValueKind == JsonValueKind.Object)
        {
            RetryPolicy defaults = new();
            options.RetryPolicy = new RetryPolicy
            {
                MaxAttempts = GetInt(retry, "MaxAttempts", defaults.MaxAttempts),
                InitialBackoff = GetDuration(retry, "InitialBackoff", defaults.InitialBackoff),
                MaxBackoff = GetDuration(retry, "MaxBackoff", defaults.MaxBackoff),
                BackoffMultiplier = GetDouble(retry, "BackoffMultiplier", defaults.BackoffMultiplier),
                Jitter = GetDouble(retry, "Jitter", defaults.Jitter),
                RetryOn = GetStringArray(retry, "RetryOn") ?? defaults.RetryOn,
                RespectRetryAfter = GetBoolOrDefault(retry, "RespectRetryAfter", defaults.RespectRetryAfter),
                RetryIdempotentOnly = GetBoolOrDefault(retry, "RetryIdempotentOnly", defaults.RetryIdempotentOnly),
            };
        }

        if (settings.TryGetProperty("RateGate", out JsonElement rateGate) && rateGate.ValueKind == JsonValueKind.Object)
        {
            RateGate defaults = new();
            options.RateGate = new RateGate
            {
                RatePerSecond = GetInt(rateGate, "RatePerSecond", defaults.RatePerSecond),
                Burst = GetInt(rateGate, "Burst", defaults.Burst),
            };
        }
    }

    private static bool TryGetBool(JsonElement parent, string name, out bool value)
    {
        if (parent.TryGetProperty(name, out JsonElement element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool GetBoolOrDefault(JsonElement parent, string name, bool defaultValue)
        => TryGetBool(parent, name, out bool value) ? value : defaultValue;

    private static int GetInt(JsonElement parent, string name, int defaultValue)
        => parent.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number ? element.GetInt32() : defaultValue;

    private static double GetDouble(JsonElement parent, string name, double defaultValue)
        => parent.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number ? element.GetDouble() : defaultValue;

    private static TimeSpan GetDuration(JsonElement parent, string name, TimeSpan defaultValue)
    {
        if (!parent.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return defaultValue;
        }

        // Fixtures spell durations as ISO 8601 (e.g. "PT0S"), per specifications/appendix-c
        // ("clock": {"advance": ["PT2S"]}); XmlConvert already implements ISO 8601 duration parsing.
        return XmlConvert.ToTimeSpan(element.GetString()!);
    }

    private static IReadOnlyList<string>? GetStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
    }
}

/// <summary>A deterministic <see cref="IClock"/> for fixtures: <c>Delay</c> never really sleeps (D-M1b-7).</summary>
internal sealed class FixtureClock : IClock
{
    public static FixtureClock Instance { get; } = new();

    public DateTimeOffset Now() => DateTimeOffset.UnixEpoch;

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

/// <summary>A deterministic <see cref="IJitterSource"/> for fixtures: always the midpoint, so backoff math is reproducible.</summary>
internal sealed class FixtureJitterSource : IJitterSource
{
    public static FixtureJitterSource Instance { get; } = new();

    public double NextDouble() => 0.5;
}
