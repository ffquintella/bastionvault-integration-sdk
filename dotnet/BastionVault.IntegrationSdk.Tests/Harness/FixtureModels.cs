using System.Collections.ObjectModel;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Tests.Harness;

public sealed record FixtureConfiguration(
    string? Address,
    string? Token,
    string? Namespace,
    string? ApiPrefix,
    JsonElement Settings,
    IReadOnlyDictionary<string, string> Environment)
{
    public static FixtureConfiguration From(FixtureDocument fixture)
    {
        JsonElement client = fixture.TryGet("client", out JsonElement clientValue) ? clientValue : default;
        string? address = GetString(client, "address");
        string? token = GetNullableString(client, "token");
        string? @namespace = GetString(client, "namespace");
        string? apiPrefix = GetString(client, "apiPrefix");
        JsonElement settings = client.ValueKind == JsonValueKind.Object && client.TryGetProperty("settings", out JsonElement settingsValue)
            ? settingsValue.Clone()
            : default;
        Dictionary<string, string> environment = fixture.TryGet("environment", out JsonElement environmentValue)
            ? environmentValue.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        return new FixtureConfiguration(address, token, @namespace, apiPrefix, settings, new ReadOnlyDictionary<string, string>(environment));
    }

    private static string? GetString(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    private static string? GetNullableString(JsonElement parent, string name) => GetString(parent, name);
}

public sealed record FixtureExchange(
    JsonElement? ExpectedRequest,
    JsonElement? Response,
    FixtureTransportFailureMode? Failure)
{
    public static FixtureExchange From(JsonElement exchange)
    {
        JsonElement? expectedRequest = exchange.TryGetProperty("expectRequest", out JsonElement request) ? request.Clone() : null;
        JsonElement? response = exchange.TryGetProperty("respond", out JsonElement respond) ? respond.Clone() : null;
        FixtureTransportFailureMode? failure = exchange.TryGetProperty("fail", out JsonElement fail)
            ? Enum.Parse<FixtureTransportFailureMode>(fail.GetString()!, ignoreCase: false)
            : null;
        return new FixtureExchange(expectedRequest, response, failure);
    }
}

public sealed record FixtureRequest(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    JsonElement? Body = null)
{
    public static FixtureRequest Create(string method, string url, IEnumerable<KeyValuePair<string, string>>? headers = null, JsonElement? body = null)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach ((string name, string value) in headers)
            {
                values[name] = value;
            }
        }

        return new FixtureRequest(method, url, new ReadOnlyDictionary<string, string>(values), body?.Clone());
    }
}

public sealed record FixtureTransportResponse(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    JsonElement? Body,
    string? RawBody);

public enum FixtureTransportFailureMode
{
    connection_refused,
    timeout,
    tls_verify,
    tls_handshake,
    reset,
    dns,
}

public sealed class FixtureTransportFailureException : IOException
{
    public FixtureTransportFailureException(FixtureTransportFailureMode mode)
        : base($"Scripted transport failure: {mode}.")
    {
        Mode = mode;
    }

    public FixtureTransportFailureMode Mode { get; }
}

public sealed class ScriptedTransport
{
    private readonly IReadOnlyList<FixtureExchange> exchanges;
    private readonly FixtureClock? clock;
    private readonly List<FixtureRequest> requests = new();
    private readonly object gate = new();
    private int nextExchange;

    private ScriptedTransport(IReadOnlyList<FixtureExchange> exchanges, FixtureClock? clock)
    {
        this.exchanges = exchanges;
        this.clock = clock;
    }

    public IReadOnlyList<FixtureRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return requests.ToArray();
            }
        }
    }

    /// <summary>
    /// Raised once the <b>last</b> scripted exchange has been answered. D-M2-27 item 7's cut-off
    /// for a loop that would otherwise run forever: the operation cancels its own
    /// <see cref="CancellationTokenSource"/> from here, which for AUT-094 means disposing the
    /// client, so the loop exits through the real cancellation path rather than through a
    /// test-only escape hatch.
    /// </summary>
    public event Action? Exhausted;

    /// <summary>
    /// Builds the scripted transport. <paramref name="clock"/> is the fixture's controllable clock
    /// (D-M2-7); it is advanced by one <c>clock.advance</c> entry after each completed exchange,
    /// which is where "applied between exchanges" is actually implemented.
    /// </summary>
    public static ScriptedTransport From(FixtureDocument fixture, FixtureClock? clock = null)
    {
        JsonElement exchanges = fixture.GetRequired("exchanges");
        return new ScriptedTransport(exchanges.EnumerateArray().Select(FixtureExchange.From).ToArray(), clock);
    }

    public ValueTask<FixtureTransportResponse> SendAsync(FixtureRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FixtureExchange exchange;
        bool exhausted;
        // Locked because M2c's first fixture runs its operation on two threads: the renewal loop
        // AUT-094 puts on a background primitive, and the caller that started it. Every fixture
        // authored before M2c is single-threaded and sees an uncontended lock, so nothing about
        // their behaviour changes.
        lock (gate)
        {
            if (nextExchange >= exchanges.Count)
            {
                throw new FixtureAssertionException($"No scripted exchange remains for request {request.Method} {request.Url}.");
            }

            exchange = exchanges[nextExchange++];
            requests.Add(request);
            exhausted = nextExchange == exchanges.Count;
        }

        clock?.AdvanceAfterExchange();
        if (exchange.Failure is FixtureTransportFailureMode failure)
        {
            RaiseIfExhausted(exhausted);
            throw new FixtureTransportFailureException(failure);
        }

        if (exchange.Response is not JsonElement response)
        {
            throw new FixtureAssertionException("A scripted exchange must contain either respond or fail.");
        }

        Dictionary<string, string> headers = response.TryGetProperty("headers", out JsonElement headerObject)
            ? headerObject.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        JsonElement? body = response.TryGetProperty("body", out JsonElement bodyValue) ? bodyValue.Clone() : null;
        string? rawBody = response.TryGetProperty("rawBody", out JsonElement rawBodyValue) ? rawBodyValue.GetString() : null;
        int status = response.GetProperty("status").GetInt32();
        FixtureTransportResponse answer = new(status, new ReadOnlyDictionary<string, string>(headers), body, rawBody);
        // After the answer is built, so the request that consumed the last exchange still completes
        // normally: cancelling before this point would map the operation's own final call to
        // BV-TRANSPORT-005 instead of letting it succeed.
        RaiseIfExhausted(exhausted);
        return ValueTask.FromResult(answer);
    }

    public void AssertFullyConsumed()
    {
        lock (gate)
        {
            if (nextExchange != exchanges.Count)
            {
                throw new FixtureAssertionException($"Scripted transport has {exchanges.Count - nextExchange} unconsumed exchange(s).");
            }
        }
    }

    private void RaiseIfExhausted(bool exhausted)
    {
        if (exhausted)
        {
            Exhausted?.Invoke();
        }
    }
}

/// <param name="Rendered">
/// The ERR-002 one-line form as the caller would print it. Carried so TST-051 can search the
/// rendered error and not only its fields (D-M2-7).
/// </param>
public sealed record FixtureError(
    string Code,
    int? StatusCode = null,
    bool? Retryable = null,
    int? Attempts = null,
    int? RetryAfter = null,
    IReadOnlyDictionary<string, object?>? Details = null,
    string? Hint = null,
    string? ServerMessage = null,
    string? Message = null,
    string? Path = null,
    string? Rendered = null);

public sealed record FixtureOperationResult(
    object? Result = null,
    FixtureError? Error = null,
    IReadOnlyDictionary<string, object?>? ClientState = null);

/// <summary>
/// The two D-M2-7 instruments attached to every fixture run: the controllable clock, and the
/// capturing logger and observer TST-051 asserts against.
/// </summary>
public sealed record FixtureInstruments(
    FixtureClock Clock,
    CapturingClientLogger Logger,
    CapturingRequestObserver Observer)
{
    /// <summary>The instruments a fixture declares (or the inert defaults it does not).</summary>
    public static FixtureInstruments For(FixtureDocument fixture)
        => new(FixtureClock.From(fixture), new CapturingClientLogger(), new CapturingRequestObserver());
}

public sealed record FixtureInvocation(
    FixtureDocument Fixture,
    FixtureConfiguration Configuration,
    ScriptedTransport Transport,
    JsonElement Arguments,
    JsonElement Options,
    FixtureInstruments Instruments);

public delegate ValueTask<FixtureOperationResult> FixtureOperationHandler(FixtureInvocation invocation);

public sealed class OperationRegistry
{
    private readonly Dictionary<string, FixtureOperationHandler> handlers = new(StringComparer.Ordinal);

    public int Count => handlers.Count;

    public void Register(string operationName, FixtureOperationHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(handler);
        handlers.Add(operationName, handler);
    }

    public bool TryResolve(string operationName, out FixtureOperationHandler? handler) => handlers.TryGetValue(operationName, out handler);
}

public enum FixtureRunStatus
{
    Passed,
    Pending,
}

public sealed record FixtureRunResult(
    string FixtureId,
    string OperationName,
    FixtureRunStatus Status,
    FixtureConfiguration Configuration);
