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
    private readonly IReadOnlyList<TimeSpan> advances;
    private DateTimeOffset now;
    private int consumedAdvances;

    private FixtureClock(DateTimeOffset start, IReadOnlyList<TimeSpan> advances, bool isScripted)
    {
        now = start;
        this.advances = advances;
        IsScripted = isScripted;
    }

    /// <summary>Whether the fixture declared a <c>clock</c> block.</summary>
    public bool IsScripted { get; }

    /// <summary>How many times the code under test asked what time it is.</summary>
    public int Reads { get; private set; }

    /// <summary>Builds the clock a fixture declares, or the frozen default when it declares none.</summary>
    public static FixtureClock From(FixtureDocument fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        if (!fixture.TryGet("clock", out JsonElement clock) || clock.ValueKind != JsonValueKind.Object)
        {
            return new FixtureClock(DateTimeOffset.UnixEpoch, Array.Empty<TimeSpan>(), isScripted: false);
        }

        DateTimeOffset start = clock.TryGetProperty("start", out JsonElement startValue) && startValue.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(startValue.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : DateTimeOffset.UnixEpoch;

        // Fixtures spell durations as ISO 8601 ("PT2S"), per specifications/appendix-c.
        TimeSpan[] advances = clock.TryGetProperty("advance", out JsonElement advanceValue) && advanceValue.ValueKind == JsonValueKind.Array
            ? advanceValue.EnumerateArray().Select(item => XmlConvert.ToTimeSpan(item.GetString()!)).ToArray()
            : Array.Empty<TimeSpan>();

        return new FixtureClock(start, advances, isScripted: true);
    }

    /// <inheritdoc/>
    public DateTimeOffset NowUtc()
    {
        Reads++;
        return now;
    }

    /// <summary><c>Delay</c> never really sleeps (D-M1b-7).</summary>
    public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies the next <c>clock.advance</c> entry. Called by <see cref="ScriptedTransport"/> once
    /// per completed exchange, which is what "applied between exchanges" means: the first entry
    /// takes effect after the first exchange and before the second.
    /// </summary>
    public void AdvanceAfterExchange()
    {
        if (consumedAdvances < advances.Count)
        {
            now += advances[consumedAdvances++];
        }
    }
}

/// <summary>
/// D-M2-7, instrument two, half one (TST-051): captures every line the SDK writes through its
/// CNF-030 logger seam.
/// </summary>
public sealed class CapturingClientLogger : IClientLogger
{
    private readonly List<string> lines = new();

    /// <summary>Everything the SDK logged during the fixture run.</summary>
    public IReadOnlyList<string> Lines => lines;

    /// <inheritdoc/>
    public void Warn(string message) => lines.Add(message);
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
    private readonly List<RequestEvent> events = new();

    /// <summary>Every attempt the SDK reported during the fixture run.</summary>
    public IReadOnlyList<RequestEvent> Events => events;

    /// <inheritdoc/>
    public void OnRequestCompleted(RequestEvent requestEvent) => events.Add(requestEvent);
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
        List<string> found = new();
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
        List<string> failures = new();
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

        List<string> haystacks = new(logger.Lines);
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

    private static string Render(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        IEnumerable<string> items => string.Join(",", items),
        _ => value.ToString() ?? string.Empty,
    };

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

    private static string Mask(string secret) => secret.Length <= 6 ? "***" : secret[..6] + "***";

    private static string Mask(string haystack, string secret)
    {
        StringBuilder builder = new(haystack);
        builder.Replace(secret, Mask(secret));
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
