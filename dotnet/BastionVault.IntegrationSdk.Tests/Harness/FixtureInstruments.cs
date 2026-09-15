using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using BastionVault.IntegrationSdk;

namespace BastionVault.IntegrationSdk.Tests.Harness;

/// <summary>
/// D-M2-7, instrument one: the fixture <c>clock</c>. Reads <c>fixture.clock.start</c> (an absolute
/// timestamp) and <c>fixture.clock.advance</c> (durations applied <b>between</b> exchanges), and is
/// injected as the client's <see cref="IClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// The schema has defined <c>clock</c> since M0 and no driver in any language read it, which is why
/// <c>auth.token.lookup-self-remaining-ttl</c> has been silently unasserted since then: the fixture
/// declared a clock, the driver ignored it, and the fixture passed anyway. A fixture that declares
/// a clock a driver ignores must <b>fail</b>, not pass, so <see cref="Reads"/> counts resolutions
/// and <see cref="FixtureDriver"/> refuses a scripted-clock fixture that never read it.
/// </para>
/// <para>
/// With no <c>clock</c> block the behaviour is the pre-M2a one exactly — a clock frozen at the unix
/// epoch — so no existing fixture changes meaning.
/// </para>
/// </remarks>
public sealed class FixtureClock : IClock
{
    /// <summary>
    /// D-M2-27 item 2's spin guard, <b>hard-coded here and not fixture-declarable</b>: a cap a
    /// fixture could raise is a gate a fixture could weaken (CLA-004). The count bound is the one
    /// that actually catches a spin — a zero-duration wait advances virtual time by nothing and
    /// would never reach the cumulative bound, and
    /// <c>transport.retry.connection-refused-then-ok.json</c> already grants a legitimate
    /// <c>PT0S</c> today, so zero-duration grants are normal rather than suspicious.
    /// </summary>
    private const int MaxGrantedWaits = 64;

    /// <summary>
    /// The runaway backstop, <b>not</b> a scheduling limit. A fixture built on a multi-week
    /// <c>lease_duration</c> (<c>auth.userpass.login-ok.json</c>'s 2,764,800 s ≈ 32 days) computes
    /// a first scheduled wait of about 21 days and trips this on grant one, which is why every
    /// <c>auth.autorenew.*</c> fixture authors its own short lease (D-M2-27 item 2).
    /// </summary>
    private static readonly TimeSpan MaxCumulativeAdvance = TimeSpan.FromHours(24);

    private readonly IReadOnlyList<TimeSpan> advances;
    private readonly List<TimeSpan> grantedWaits = [];
    private readonly object gate = new();
    private readonly bool isVirtual;
    private DateTimeOffset now;
    private int consumedAdvances;
    private TimeSpan cumulativeAdvance;

    private FixtureClock(DateTimeOffset start, IReadOnlyList<TimeSpan> advances, bool isScripted, bool isVirtual)
    {
        now = start;
        this.advances = advances;
        this.isVirtual = isVirtual;
        IsScripted = isScripted;
    }

    /// <summary>Whether the fixture declared a <c>clock</c> block.</summary>
    public bool IsScripted { get; }

    /// <summary>Whether the fixture declared <c>clock.delay: "virtual"</c> (D-M2-27).</summary>
    public bool IsVirtual => isVirtual;

    /// <summary>
    /// Every wait the code under test asked for, in order, when this clock is in virtual mode.
    /// Empty in <c>"instant"</c> mode, where nothing is recorded and the pre-M2c behaviour is
    /// byte-identical.
    /// </summary>
    public IReadOnlyList<TimeSpan> GrantedWaits
    {
        get
        {
            lock (gate)
            {
                return grantedWaits.ToArray();
            }
        }
    }

    /// <summary>
    /// The latched harness-originated failure, if one happened (D-M2-27 item 2). Latched as well as
    /// thrown because AUT-092 requires the renewal loop to <i>absorb</i> failures: an exception
    /// raised inside <see cref="Delay"/> on a background renewal task can be caught by the very
    /// failure handling it is meant to catch and resurface as an ordinary
    /// <c>OnStopped(RenewalFailed)</c>, which a careless fixture would then assert as the intended
    /// behaviour. <see cref="FixtureDriver"/> checks this after the operation returns.
    /// </summary>
    public string? HarnessFailure { get; private set; }

    /// <summary>How many times the code under test asked what time it is.</summary>
    public int Reads { get; private set; }

    /// <summary>Builds the clock a fixture declares, or the frozen default when it declares none.</summary>
    public static FixtureClock From(FixtureDocument fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (!fixture.TryGet("clock", out JsonElement clock) || clock.ValueKind != JsonValueKind.Object)
        {
            return new FixtureClock(DateTimeOffset.UnixEpoch, Array.Empty<TimeSpan>(), isScripted: false, isVirtual: false);
        }

        DateTimeOffset start = clock.TryGetProperty("start", out JsonElement startValue) && startValue.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(startValue.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : DateTimeOffset.UnixEpoch;

        // Fixtures spell durations as ISO 8601 ("PT2S"), per specifications/appendix-c.
        TimeSpan[] advances = clock.TryGetProperty("advance", out JsonElement advanceValue) && advanceValue.ValueKind == JsonValueKind.Array
            ? advanceValue.EnumerateArray().Select(item => XmlConvert.ToTimeSpan(item.GetString()!)).ToArray()
            : Array.Empty<TimeSpan>();

        // D-M2-27: opt-in, per fixture, and "instant" by default — so every fixture authored before
        // M2c keeps the M2a behaviour exactly.
        bool isVirtual = clock.TryGetProperty("delay", out JsonElement delayValue)
            && delayValue.ValueKind == JsonValueKind.String
            && string.Equals(delayValue.GetString(), "virtual", StringComparison.Ordinal);

        return new FixtureClock(start, advances, isScripted: true, isVirtual);
    }

    /// <summary>The ISO-8601 durations a fixture's <c>clock.expectWaits</c> declares, if it declares any.</summary>
    public static IReadOnlyList<TimeSpan>? ExpectedWaits(FixtureDocument fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (!fixture.TryGet("clock", out JsonElement clock)
            || clock.ValueKind != JsonValueKind.Object
            || !clock.TryGetProperty("expectWaits", out JsonElement waits)
            || waits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return waits.EnumerateArray().Select(item => XmlConvert.ToTimeSpan(item.GetString()!)).ToArray();
    }

    /// <inheritdoc/>
    public DateTimeOffset NowUtc()
    {
        lock (gate)
        {
            Reads++;
            return now;
        }
    }

    /// <summary>
    /// <c>Delay</c> never really sleeps (D-M1b-7). In <c>"instant"</c> mode it completes without
    /// moving time, which is the pre-M2c body verbatim; in <c>"virtual"</c> mode (D-M2-27) it
    /// advances the clock by the requested duration and records the grant, so a schedule the code
    /// under test computes is both <i>reached</i> without waiting and <i>assertable</i> afterwards.
    /// </summary>
    public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!isVirtual)
        {
            return Task.CompletedTask;
        }

        lock (gate)
        {
            now += duration;
            grantedWaits.Add(duration);
            cumulativeAdvance += duration;
            if (grantedWaits.Count > MaxGrantedWaits || cumulativeAdvance > MaxCumulativeAdvance)
            {
                throw Latch(
                    $"the virtual clock granted {grantedWaits.Count} wait(s) totalling {XmlConvert.ToString(cumulativeAdvance)}, "
                        + $"past the harness spin guard of {MaxGrantedWaits} grants / {XmlConvert.ToString(MaxCumulativeAdvance)} cumulative. "
                        + "Either the code under test is spinning, or this fixture's lease is too long to schedule "
                        + "inside the backstop — an `auth.autorenew.*` fixture must author its own short `lease_duration` (D-M2-27).");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a harness-originated failure and returns the exception to throw for it, so the
    /// failure survives an operation that swallows the throw (D-M2-27 item 2).
    /// </summary>
    public FixtureAssertionException Latch(string message)
    {
        HarnessFailure ??= message;
        return new FixtureAssertionException(message);
    }

    /// <summary>
    /// Applies the next <c>clock.advance</c> entry. Called by <see cref="ScriptedTransport"/> once
    /// per completed exchange, which is what "applied between exchanges" means: the first entry
    /// takes effect after the first exchange and before the second.
    /// </summary>
    public void AdvanceAfterExchange()
    {
        lock (gate)
        {
            if (consumedAdvances < advances.Count)
            {
                now += advances[consumedAdvances++];
            }
        }
    }
}

/// <summary>
/// D-M2-7, instrument two, half one (TST-051): captures every line the SDK writes through its
/// CNF-030 logger seam.
/// </summary>
public sealed class CapturingClientLogger : IClientLogger
{
    private readonly List<string> lines = [];
    private readonly List<string> warnLines = [];
    private readonly List<string> infoLines = [];

    /// <summary>Everything the SDK logged during the fixture run, at any level (TST-051's scan surface).</summary>
    public IReadOnlyList<string> Lines => lines;

    /// <summary>Only what the SDK logged through <see cref="Warn"/>, so a test can tell the two levels apart (AUT-095).</summary>
    public IReadOnlyList<string> WarnLines => warnLines;

    /// <summary>Only what the SDK logged through <see cref="Info"/>, so a test can tell the two levels apart (AUT-095).</summary>
    public IReadOnlyList<string> InfoLines => infoLines;

    /// <inheritdoc/>
    public void Warn(string message)
    {
        lines.Add(message);
        warnLines.Add(message);
    }

    /// <inheritdoc/>
    public void Info(string message)
    {
        lines.Add(message);
        infoLines.Add(message);
    }
}

/// <summary>
/// D-M2-7, instrument two, half two (TST-051, CFG-080): captures every observer event.
/// </summary>
/// <remarks>
/// Named explicitly by D-M2-7 because this — not the log — is where the leak this milestone was
/// most likely to ship actually surfaces: <see cref="RequestEvent.Path"/> carries the request path,
/// and AUT-080's <c>auth/token/renew/{currentToken}</c> and <c>Auth.Token.Lookup</c>'s
/// <c>auth/token/lookup/{token}</c> put a live token in it. ERR-003 already required the path
/// redacted in <i>errors</i>; the observer is the second consumer of the same string.
/// </remarks>
public sealed class CapturingRequestObserver : IRequestObserver
{
    private readonly List<RequestEvent> events = [];

    /// <summary>Every attempt the SDK reported during the fixture run.</summary>
    public IReadOnlyList<RequestEvent> Events => events;

    /// <inheritdoc/>
    public void OnRequestCompleted(RequestEvent requestEvent)
    {
        events.Add(requestEvent);
    }
}

/// <summary>
/// TST-051's assertion: no fixture secret literal appears in any captured log line, observer event,
/// exception message or rendered error.
/// </summary>
/// <remarks>
/// It works only because TST-050 forces distinctive fixture secrets (<c>s.FAKE…</c>,
/// <c>password-fixture</c>), which is what makes a substring search a valid test rather than a
/// gesture — so <see cref="Harvest"/> is itself asserted to be non-empty for every fixture that
/// carries a credential (D-M2-7).
/// </remarks>
public static class FixtureSecrets
{
    /// <summary>
    /// The JSON property names whose value is credential material regardless of its spelling. The
    /// <c>s.FAKE…</c> / <c>password-fixture</c> conventions catch most of it; these catch a
    /// fixture that spells a secret some other way.
    /// </summary>
    private static readonly string[] SecretBearingProperties =
    [
        "token", "password", "secret_id", "machine_token", "totp_code", "client_token",
        "child_token", "user_token", "X-BastionVault-Token",
    ];

    /// <summary>Every secret literal a fixture carries, in declaration order and de-duplicated.</summary>
    public static IReadOnlyList<string> Harvest(FixtureDocument fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        List<string> found = [];
        Walk(fixture.Json, propertyName: null, found);
        return found.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Fails when any harvested literal appears in anything the SDK <b>surfaced</b>. The recorded
    /// requests are deliberately not searched: the wire legitimately carries the token, and TRN-015
    /// is what says where.
    /// </summary>
    public static void AssertNoLeak(FixtureDocument fixture, IReadOnlyList<string> haystacks)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(haystacks);
        IReadOnlyList<string> secrets = Harvest(fixture);
        List<string> failures = [];
        foreach (string secret in secrets)
        {
            foreach (string haystack in haystacks)
            {
                if (haystack.Contains(secret, StringComparison.Ordinal))
                {
                    failures.Add($"secret '{Mask(secret)}' appears in a surfaced string: {Mask(haystack, secret)}");
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new FixtureAssertionException(
                $"TST-051: fixture '{fixture.Id}' leaked secret material. " + string.Join("; ", failures));
        }
    }

    /// <summary>
    /// Everything one fixture run surfaced: log lines, observer events, and the error as the caller
    /// would see it (message, hint, server message, path, details, and the ERR-002 one-line form).
    /// </summary>
    public static IReadOnlyList<string> Surfaced(
        CapturingClientLogger logger,
        CapturingRequestObserver observer,
        FixtureOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(result);

        List<string> haystacks = [.. logger.Lines];
        foreach (RequestEvent captured in observer.Events)
        {
            // The record's own ToString() renders every member, so a member added later is
            // searched without this list being updated.
            haystacks.Add(captured.ToString());
        }

        if (result.Error is { } error)
        {
            haystacks.Add(error.Code);
            haystacks.Add(error.Message ?? string.Empty);
            haystacks.Add(error.Hint ?? string.Empty);
            haystacks.Add(error.ServerMessage ?? string.Empty);
            haystacks.Add(error.Path ?? string.Empty);
            haystacks.Add(error.Rendered ?? string.Empty);
            if (error.Details is { } details)
            {
                foreach ((string key, object? value) in details)
                {
                    haystacks.Add(key);
                    haystacks.Add(Render(value));
                }
            }
        }

        return haystacks;
    }

    private static string Render(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            IEnumerable<string> items => string.Join(",", items),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static void Walk(JsonElement element, string? propertyName, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Walk(property.Value, property.Name, found);
                }

                return;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(item, propertyName, found);
                }

                return;
            case JsonValueKind.String:
                string value = element.GetString()!;
                if (IsSecret(value, propertyName))
                {
                    found.Add(value);
                }

                return;
            default:
                return;
        }
    }

    private static bool IsSecret(string value, string? propertyName)
    {
        if (value.Length == 0)
        {
            return false;
        }

        // TST-050's two conventions.
        if (value.Contains("s.FAKE", StringComparison.Ordinal) || value.Contains("password-fixture", StringComparison.Ordinal))
        {
            return true;
        }

        return propertyName is not null
            && SecretBearingProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
    }

    private static string Mask(string secret)
    {
        return secret.Length <= 6 ? "***" : secret[..6] + "***";
    }

    private static string Mask(string haystack, string secret)
    {
        StringBuilder builder = new(haystack);
        _ = builder.Replace(secret, Mask(secret));
        return builder.ToString();
    }
}

/// <summary>
/// The one conversion from a <see cref="BastionVaultException"/> to the harness's
/// <see cref="FixtureError"/>, so every operation family surfaces the same fields — including the
/// three TST-051 searches (<c>Message</c>, <c>Path</c>, the ERR-002 one-line form) that a per-family
/// copy would be free to forget.
/// </summary>
public static class FixtureErrors
{
    /// <summary>Projects <paramref name="exception"/> onto the harness's error shape.</summary>
    public static FixtureError From(BastionVaultException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new FixtureError(
            Code: exception.Code,
            StatusCode: exception.StatusCode,
            Retryable: exception.Retryable,
            Attempts: exception.Attempts,
            RetryAfter: exception.RetryAfter is { } retryAfter ? (int)retryAfter.TotalSeconds : null,
            Details: exception.Details,
            Hint: exception.Hint,
            ServerMessage: exception.ServerMessage,
            Message: exception.Message,
            Path: exception.Path,
            Rendered: exception.ToString());
    }
}
