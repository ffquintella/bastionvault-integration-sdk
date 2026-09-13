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
    private readonly List<FixtureRequest> requests = new();
    private int nextExchange;

    private ScriptedTransport(IReadOnlyList<FixtureExchange> exchanges)
    {
        this.exchanges = exchanges;
    }

    public IReadOnlyList<FixtureRequest> Requests => requests;

    public static ScriptedTransport From(FixtureDocument fixture)
    {
        JsonElement exchanges = fixture.GetRequired("exchanges");
        return new ScriptedTransport(exchanges.EnumerateArray().Select(FixtureExchange.From).ToArray());
    }

    public ValueTask<FixtureTransportResponse> SendAsync(FixtureRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (nextExchange >= exchanges.Count)
        {
            throw new FixtureAssertionException($"No scripted exchange remains for request {request.Method} {request.Url}.");
        }

        FixtureExchange exchange = exchanges[nextExchange++];
        requests.Add(request);
        if (exchange.Failure is FixtureTransportFailureMode failure)
        {
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
        return ValueTask.FromResult(new FixtureTransportResponse(status, new ReadOnlyDictionary<string, string>(headers), body, rawBody));
    }

    public void AssertFullyConsumed()
    {
        if (nextExchange != exchanges.Count)
        {
            throw new FixtureAssertionException($"Scripted transport has {exchanges.Count - nextExchange} unconsumed exchange(s).");
        }
    }
}

public sealed record FixtureError(
    string Code,
    int? StatusCode = null,
    bool? Retryable = null,
    int? Attempts = null,
    int? RetryAfter = null,
    IReadOnlyDictionary<string, object?>? Details = null,
    string? Hint = null,
    string? ServerMessage = null);

public sealed record FixtureOperationResult(
    object? Result = null,
    FixtureError? Error = null,
    IReadOnlyDictionary<string, object?>? ClientState = null);

public sealed record FixtureInvocation(
    FixtureDocument Fixture,
    FixtureConfiguration Configuration,
    ScriptedTransport Transport,
    JsonElement Arguments,
    JsonElement Options);

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
