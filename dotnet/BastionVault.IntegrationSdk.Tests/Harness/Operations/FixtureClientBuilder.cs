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
    /// <summary>
    /// Builds the client options a fixture describes. <paramref name="instruments"/> carries the
    /// two D-M2-7 instruments; passing them here rather than at each handler is what makes them
    /// unconditional for every operation, present and future.
    /// </summary>
    public static BastionVaultClientOptions BuildOptions(
        FixtureConfiguration configuration,
        ITransport transport,
        FixtureInstruments instruments,
        AutoRenewPolicy? autoRenew = null,
        ISrvResolver? srvResolver = null)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        BastionVaultClientOptions options = new()
        {
            Address = configuration.Address,
            Token = configuration.Token,
            Namespace = configuration.Namespace,
            ApiPrefix = configuration.ApiPrefix,
            ClusterDiscovery = configuration.ClusterDiscovery,
            SrvResolver = srvResolver,
            Transport = transport,
            Clock = instruments.Clock,
            JitterSource = ReadJitter(configuration.Settings),
            Logger = instruments.Logger,
            Observer = instruments.Observer,
        };

        ApplySettings(options, configuration.Settings);
        // Applied after the settings block so an AutoRenew a handler builds (with its observer
        // callbacks attached, which JSON cannot express) wins over one the fixture declares.
        options.AutoRenew = autoRenew ?? ReadAutoRenew(configuration.Settings);
        return options;
    }

    /// <summary>
    /// The fixture <c>settings.__authInfo</c> instrument (D-M4-7): makes the client behave as
    /// though it had already logged in and received the credential the fixture describes, so
    /// KV2-022's client-side fail-fast can be driven without a login exchange.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>__</c> prefix marks it as an instrument, in the same sense as the M2a fixture
    /// <c>clock</c>: it configures the <i>harness</i>, not the SDK. It adds <b>no</b> production
    /// seam — it goes through <see cref="BastionVaultClient.Context"/> and
    /// <c>ClientContext.RecordLogin</c>, the same internal path a real login takes, both of which
    /// existed before M4 and neither of which exists for the tests' benefit.
    /// </para>
    /// <para>
    /// The fixture states the <i>projection</i> (<c>EnvironmentScope.Scoped</c>,
    /// <c>SecretGlobs</c>, <c>MachineGlobs</c>), and this reverses it into the AUT-044
    /// <c>approle_env_*</c> metadata a login would actually carry. Building it the other way round
    /// — setting an <see cref="EnvironmentScope"/> directly — is impossible by design:
    /// <see cref="AuthInfo.EnvironmentScope"/> is derived precisely so it can never disagree with
    /// the metadata behind it (D-M4-7), and an instrument that bypassed that would be testing a
    /// path production has not got.
    /// </para>
    /// </remarks>
    public static void ApplyAuthInfoInstrument(BastionVaultClient client, JsonElement settings)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (settings.ValueKind != JsonValueKind.Object
            || !settings.TryGetProperty("__authInfo", out JsonElement authInfo)
            || authInfo.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        Dictionary<string, string> metadata = new(StringComparer.Ordinal);
        if (authInfo.TryGetProperty("EnvironmentScope", out JsonElement scope) && scope.ValueKind == JsonValueKind.Object)
        {
            if (TryGetBool(scope, "Scoped", out bool scoped))
            {
                metadata[EnvironmentScope.ScopedKey] = scoped ? "true" : "false";
            }

            if (GetStringArray(scope, "SecretGlobs") is { } secretGlobs)
            {
                metadata[EnvironmentScope.SecretGlobsKey] = string.Join(',', secretGlobs);
            }

            if (GetStringArray(scope, "MachineGlobs") is { } machineGlobs)
            {
                metadata[EnvironmentScope.MachineGlobsKey] = string.Join(',', machineGlobs);
            }
        }

        // `install: false`: the fixture's own `client.token` stays the token on the wire, exactly
        // as it would after a `Login` token source resolved itself. The credential is recorded,
        // not swapped in.
        client.Context.RecordLogin(
            new AuthInfo
            {
                ClientToken = client.Auth.CurrentToken ?? new SecretString(null),
                Metadata = metadata,
                // The fixture clock's own default start, so the recorded credential is no newer
                // than the clock the client was built with.
                IssuedAt = DateTimeOffset.UnixEpoch,
            },
            install: false);
    }

    /// <summary>
    /// The <c>settings.__pinned</c> / <c>settings.__candidates</c> instruments (D-M5-15): seeds a
    /// discovery-mode client with an already-pinned node and a cached candidate set, so a failover
    /// fixture need not re-script the initial discovery it is not testing.
    /// </summary>
    /// <remarks>
    /// The <c>__</c> prefix marks it as a harness instrument, like <c>__authInfo</c>: it configures
    /// the fixture's starting state, not the SDK. It writes through
    /// <c>DiscoveryEngine.Seed</c>, which sets exactly the three fields real discovery sets, so the
    /// seeded client is indistinguishable from one that probed — and adds no public surface.
    /// </remarks>
    public static void ApplyDiscoveryInstruments(BastionVaultClient client, JsonElement settings)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (settings.ValueKind != JsonValueKind.Object
            || !settings.TryGetProperty("__pinned", out JsonElement pinned)
            || pinned.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string pinnedUrl = pinned.GetString()!;
        IReadOnlyList<string> urls = GetStringArray(settings, "__candidates") ?? [pinnedUrl];
        Candidate[] cached = urls.Select(ToCandidate).ToArray();
        // The pinned node's state is what a prior discovery would have recorded to pick it: the
        // fixtures that use this instrument assert the *post-failover* pin, never this one.
        client.Context.Discovery.Seed(
            new NodeSelection(pinnedUrl, NodeState.ActiveLeader, null),
            cached);
    }

    /// <summary>
    /// A candidate from its URL alone. <c>Priority</c> and <c>Weight</c> are <see langword="null"/>
    /// because a <c>__candidates</c> entry carries no SRV record, which is the same shape DSC-012's
    /// synthesised candidate has (D-M5-8).
    /// </summary>
    private static Candidate ToCandidate(string url)
    {
        Uri uri = new(url, UriKind.Absolute);
        return new Candidate(url, uri.Host, uri.Port, null, null);
    }

    /// <summary>
    /// The <c>settings.__jitter</c> instrument (D-M5-14): <c>{ "values": [d, d, …] }</c>, consumed
    /// in order by the injected <see cref="IJitterSource"/>. Deliberately <b>not</b> a PRNG seed —
    /// a seed produces different sequences in .NET, Rust and Python, so a seeded fixture would be
    /// unportable and silently non-parity, which is the one thing a shared fixture exists to
    /// prevent.
    /// </summary>
    private static IJitterSource ReadJitter(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object
            || !settings.TryGetProperty("__jitter", out JsonElement jitter)
            || jitter.ValueKind != JsonValueKind.Object
            || !jitter.TryGetProperty("values", out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return FixtureJitterSource.Instance;
        }

        return new SequenceJitterSource(values.EnumerateArray().Select(value => value.GetDouble()).ToArray());
    }

    /// <summary>
    /// The <c>AutoRenew</c> settings a fixture declares (AUT-090…AUT-095). Callbacks are not
    /// expressible in a fixture, so a handler that wants them composes the policy itself and passes
    /// it to <see cref="BuildOptions"/>.
    /// </summary>
    public static AutoRenewPolicy? ReadAutoRenew(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object
            || !settings.TryGetProperty("AutoRenew", out JsonElement autoRenew)
            || autoRenew.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        AutoRenewPolicy defaults = new();
        return new AutoRenewPolicy
        {
            Enabled = GetBoolOrDefault(autoRenew, "Enabled", defaults.Enabled),
            RenewAtFraction = GetDouble(autoRenew, "RenewAtFraction", defaults.RenewAtFraction),
            MinInterval = GetDuration(autoRenew, "MinInterval", defaults.MinInterval),
            Increment = autoRenew.TryGetProperty("Increment", out JsonElement increment) && increment.ValueKind == JsonValueKind.String
                ? XmlConvert.ToTimeSpan(increment.GetString()!)
                : defaults.Increment,
            MaxConsecutiveFailures = GetInt(autoRenew, "MaxConsecutiveFailures", defaults.MaxConsecutiveFailures),
        };
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
    {
        return TryGetBool(parent, name, out bool value) ? value : defaultValue;
    }

    private static int GetInt(JsonElement parent, string name, int defaultValue)
    {
        return parent.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number ? element.GetInt32() : defaultValue;
    }

    private static double GetDouble(JsonElement parent, string name, double defaultValue)
    {
        return parent.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number ? element.GetDouble() : defaultValue;
    }

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

/// <summary>
/// D-M5-14's instrument: the declared values, in order, then the midpoint once they run out — so a
/// fixture that declares fewer values than the retries it drives degrades to the default rather
/// than throwing an error that would describe the wrong problem.
/// </summary>
internal sealed class SequenceJitterSource : IJitterSource
{
    private readonly IReadOnlyList<double> values;
    private int next;

    public SequenceJitterSource(IReadOnlyList<double> values)
    {
        this.values = values;
    }

    public double NextDouble()
    {
        return next < values.Count ? values[next++] : 0.5;
    }
}

/// <summary>A deterministic <see cref="IJitterSource"/> for fixtures: always the midpoint, so backoff math is reproducible.</summary>
internal sealed class FixtureJitterSource : IJitterSource
{
    public static FixtureJitterSource Instance { get; } = new();

    public double NextDouble()
    {
        return 0.5;
    }
}
