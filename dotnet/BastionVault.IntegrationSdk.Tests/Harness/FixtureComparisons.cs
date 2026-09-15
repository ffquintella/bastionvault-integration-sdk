using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BastionVault.IntegrationSdk.Tests.Harness;

public sealed class FixtureAssertionException : Xunit.Sdk.XunitException
{
    public FixtureAssertionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class RedactedValue
{
    private readonly string originalValue;

    public RedactedValue(string originalValue)
    {
        this.originalValue = originalValue;
    }

    internal bool DoesNotRevealOriginalValue => !ToString().Contains(originalValue, StringComparison.Ordinal);

    public override string ToString()
    {
        return "[REDACTED]";
    }
}

public static class FixtureComparisons
{
    public static void AssertRequest(JsonElement expected, FixtureRequest actual, bool strictHeaders = false)
    {
        IReadOnlyList<string> failures = CompareRequest(expected, actual, strictHeaders);
        if (failures.Count > 0)
        {
            throw new FixtureAssertionException(string.Join("; ", failures));
        }
    }

    public static IReadOnlyList<string> CompareRequest(JsonElement expected, FixtureRequest actual, bool strictHeaders = false)
    {
        List<string> failures = [];
        if (expected.TryGetProperty("method", out JsonElement method) && !string.Equals(method.GetString(), actual.Method, StringComparison.Ordinal))
        {
            failures.Add($"method expected '{method.GetString()}' but was '{actual.Method}'");
        }

        if (expected.TryGetProperty("url", out JsonElement url) && !string.Equals(url.GetString(), actual.Url, StringComparison.Ordinal))
        {
            failures.Add($"url expected '{url.GetString()}' but was '{actual.Url}'");
        }

        if (expected.TryGetProperty("headers", out JsonElement expectedHeaders))
        {
            foreach (JsonProperty property in expectedHeaders.EnumerateObject())
            {
                if (!TryGetHeader(actual.Headers, property.Name, out string? actualValue))
                {
                    failures.Add($"header '{property.Name}' is missing");
                }
                else if (!string.Equals(property.Value.GetString(), actualValue, StringComparison.Ordinal))
                {
                    failures.Add($"header '{property.Name}' expected '{property.Value.GetString()}' but was '{actualValue}'");
                }
            }
        }

        if (expected.TryGetProperty("absentHeaders", out JsonElement absentHeaders))
        {
            foreach (JsonElement header in absentHeaders.EnumerateArray())
            {
                string name = header.GetString()!;
                if (TryGetHeader(actual.Headers, name, out _))
                {
                    failures.Add($"header '{name}' must be absent");
                }
            }
        }

        if (strictHeaders)
        {
            HashSet<string> expectedNames = expected.TryGetProperty("headers", out JsonElement headers)
                ? headers.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string actualName in actual.Headers.Keys)
            {
                if (!expectedNames.Contains(actualName))
                {
                    failures.Add($"header '{actualName}' is not listed while strictHeaders is enabled");
                }
            }
        }

        if (expected.TryGetProperty("body", out JsonElement expectedBody) && !JsonMatches(expectedBody, actual.Body))
        {
            failures.Add($"body expected canonical JSON {expectedBody.GetRawText()} but was {FormatJson(actual.Body)}");
        }

        return failures;
    }

    public static void AssertResult(JsonElement expected, object? actual)
    {
        List<string> failures = [];
        CompareResult(expected, actual, present: true, "$", failures);
        if (failures.Count > 0)
        {
            throw new FixtureAssertionException(string.Join("; ", failures));
        }
    }

    public static void AssertError(JsonElement expected, FixtureError? actual)
    {
        if (actual is null)
        {
            throw new FixtureAssertionException("expected an error but the operation returned no error.");
        }

        List<string> failures = [];
        if (!expected.TryGetProperty("code", out JsonElement code) || !string.Equals(code.GetString(), actual.Code, StringComparison.Ordinal))
        {
            failures.Add($"error code expected '{code.GetString()}' but was '{actual.Code}'");
        }

        CompareOptional(expected, "statusCode", actual.StatusCode, failures);
        CompareOptional(expected, "retryable", actual.Retryable, failures);
        CompareOptional(expected, "attempts", actual.Attempts, failures);
        CompareOptional(expected, "retryAfter", actual.RetryAfter, failures);

        if (expected.TryGetProperty("detailsKeys", out JsonElement detailsKeys))
        {
            foreach (JsonElement key in detailsKeys.EnumerateArray())
            {
                if (actual.Details is null || !actual.Details.Keys.Contains(key.GetString()!, StringComparer.Ordinal))
                {
                    failures.Add($"error details key '{key.GetString()}' is missing");
                }
            }
        }

        if (expected.TryGetProperty("hintContains", out JsonElement hints))
        {
            foreach (JsonElement hint in hints.EnumerateArray())
            {
                string expectedHint = hint.GetString()!;
                if (actual.Hint is null || actual.Hint.IndexOf(expectedHint, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    failures.Add($"error hint does not contain '{expectedHint}' case-insensitively");
                }
            }
        }

        if (expected.TryGetProperty("serverMessage", out JsonElement serverMessage) && !string.Equals(serverMessage.GetString(), actual.ServerMessage, StringComparison.Ordinal))
        {
            failures.Add($"serverMessage expected '{serverMessage.GetString()}' but was '{actual.ServerMessage}'");
        }

        if (failures.Count > 0)
        {
            throw new FixtureAssertionException(string.Join("; ", failures));
        }
    }

    private static void CompareOptional<T>(JsonElement expected, string name, T? actual, List<string> failures)
        where T : struct
    {
        if (!expected.TryGetProperty(name, out JsonElement expectedValue))
        {
            return;
        }

        object? expectedObject = expectedValue.ValueKind == JsonValueKind.Null ? null : expectedValue.Deserialize<T>();
        if (!Equals(expectedObject, actual))
        {
            failures.Add($"error {name} expected '{expectedObject}' but was '{actual}'");
        }
    }

    private static void CompareResult(JsonElement expected, object? actual, bool present, string path, List<string> failures)
    {
        if (expected.ValueKind == JsonValueKind.String)
        {
            string sentinel = expected.GetString()!;
            switch (sentinel)
            {
                case "$absent" when !present || actual is null:
                    return;
                case "$any" when present:
                    return;
                case "$redacted" when actual is RedactedValue redacted && redacted.DoesNotRevealOriginalValue:
                    return;
                case "$absent":
                    failures.Add($"{path} expected absent/null but was {FormatValue(actual)}");
                    return;
                case "$any":
                    failures.Add($"{path} expected a present value but was absent");
                    return;
                case "$redacted":
                    failures.Add($"{path} expected a redacting value");
                    return;
            }
        }

        if (expected.ValueKind == JsonValueKind.Null)
        {
            if (present && actual is not null)
            {
                failures.Add($"{path} expected null but was {FormatValue(actual)}");
            }

            return;
        }

        if (!present)
        {
            failures.Add($"{path} is missing");
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                if (!TryGetObject(actual, out IReadOnlyDictionary<string, object?> actualObject))
                {
                    failures.Add($"{path} expected an object but was {FormatValue(actual)}");
                    return;
                }

                foreach (JsonProperty property in expected.EnumerateObject())
                {
                    bool childPresent = actualObject.TryGetValue(property.Name, out object? child);
                    CompareResult(property.Value, child, childPresent, $"{path}.{property.Name}", failures);
                }

                return;
            case JsonValueKind.Array:
                if (!TryGetArray(actual, out IReadOnlyList<object?> actualArray))
                {
                    failures.Add($"{path} expected an array but was {FormatValue(actual)}");
                    return;
                }

                JsonElement[] expectedArray = expected.EnumerateArray().ToArray();
                if (expectedArray.Length != actualArray.Count)
                {
                    failures.Add($"{path} expected {expectedArray.Length} array item(s) but was {actualArray.Count}");
                }

                for (int index = 0; index < Math.Min(expectedArray.Length, actualArray.Count); index++)
                {
                    CompareResult(expectedArray[index], actualArray[index], present: true, $"{path}[{index}]", failures);
                }

                return;
            default:
                if (!ScalarMatches(expected, actual))
                {
                    failures.Add($"{path} expected {expected.GetRawText()} but was {FormatValue(actual)}");
                }

                return;
        }
    }

    private static bool ScalarMatches(JsonElement expected, object? actual)
    {
        if (actual is null)
        {
            return false;
        }

        if (expected.ValueKind == JsonValueKind.String)
        {
            return actual is string actualString && string.Equals(expected.GetString(), actualString, StringComparison.Ordinal);
        }

        if (expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return actual is bool actualBoolean && actualBoolean == expected.GetBoolean();
        }

        if (expected.ValueKind == JsonValueKind.Number)
        {
            if (actual is JsonElement actualElement && actualElement.ValueKind == JsonValueKind.Number)
            {
                return string.Equals(expected.GetRawText(), actualElement.GetRawText(), StringComparison.Ordinal)
                    || (decimal.TryParse(expected.GetRawText(), out decimal expectedDecimal) && decimal.TryParse(actualElement.GetRawText(), out decimal actualDecimal) && expectedDecimal == actualDecimal);
            }

            return decimal.TryParse(expected.GetRawText(), out decimal expectedValue)
                && actual is IConvertible convertible
                && decimal.TryParse(convertible.ToString(null), out decimal actualValue)
                && expectedValue == actualValue;
        }

        return false;
    }

    private static bool JsonMatches(JsonElement expected, JsonElement? actual)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            return actual is null || actual.Value.ValueKind == JsonValueKind.Null;
        }

        if (actual is not JsonElement actualValue)
        {
            return false;
        }

        JsonNode? expectedNode = JsonNode.Parse(expected.GetRawText());
        JsonNode? actualNode = JsonNode.Parse(actualValue.GetRawText());
        return JsonNode.DeepEquals(expectedNode, actualNode);
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        foreach ((string actualName, string actualValue) in headers)
        {
            if (string.Equals(actualName, name, StringComparison.OrdinalIgnoreCase))
            {
                value = actualValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetObject(object? value, out IReadOnlyDictionary<string, object?> result)
    {
        if (value is IReadOnlyDictionary<string, object?> readOnly)
        {
            result = readOnly;
            return true;
        }

        if (value is IDictionary<string, object?> dictionary)
        {
            result = new ReadOnlyDictionary<string, object?>(dictionary);
            return true;
        }

        if (value is JsonObject jsonObject)
        {
            result = jsonObject.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            result = element.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal);
            return true;
        }

        result = new Dictionary<string, object?>(StringComparer.Ordinal);
        return false;
    }

    private static bool TryGetArray(object? value, out IReadOnlyList<object?> result)
    {
        if (value is JsonArray jsonArray)
        {
            result = jsonArray.Select(item => (object?)item).ToArray();
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            result = element.EnumerateArray().Select(item => (object?)item.Clone()).ToArray();
            return true;
        }

        if (value is IEnumerable enumerable and not string)
        {
            result = enumerable.Cast<object?>().ToArray();
            return true;
        }

        result = Array.Empty<object?>();
        return false;
    }

    private static string FormatJson(JsonElement? value)
    {
        return value is JsonElement element ? element.GetRawText() : "<absent>";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            JsonElement element => element.GetRawText(),
            JsonNode node => node.ToJsonString(),
            IEnumerable enumerable when value is not string => $"[{string.Join(", ", enumerable.Cast<object?>().Select(FormatValue))}]",
            _ => value.ToString() ?? "<unknown>",
        };
    }
}
