namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>The three run-wide MUSTs: ITG-020, ITG-021, ITG-022. ITG-023 is a summary, not an assertion.</summary>
/// <remarks>
/// <see cref="Enforce"/> throws rather than setting a process exit code: a measured defect found
/// that <c>Environment.ExitCode</c> set from <c>AppDomain.ProcessExit</c> does not change what
/// <c>dotnet test</c> reports. Throwing from a scope xUnit already owns is what its pass/fail sees.
/// </remarks>
internal static class RunAssertions
{
    /// <summary>ITG-020: a response whose shape the SDK could not recognise.</summary>
    public static IReadOnlyList<string> ProtocolViolations(IEnumerable<RequestEvent> events)
    {
        return [.. events
            .Where(e => string.Equals(e.ErrorCode, ErrorCodes.ProtocolUnexpectedResponse, StringComparison.Ordinal))
            .Select(e => $"{e.Method} {e.Path} (attempt {e.Attempt})")];
    }

    /// <summary>ITG-022: an event missing one of the mandated non-empty fields.</summary>
    public static IReadOnlyList<string> ObservabilityDefects(IEnumerable<RequestEvent> events)
    {
        return [.. events.Where(IsMalformed).Select(e => $"{e.Method} {e.Path} (attempt {e.Attempt})")];
    }

    // Checks one scope's own calls against all three MUSTs, records the result into `report`
    // either way, and throws when any MUST was violated, so the scope that caused it fails.
    public static void Enforce(
        string scope,
        IReadOnlyList<RequestEvent> events,
        SecretWatch secrets,
        IReadOnlyList<string> logLines,
        RunReport report)
    {
        IReadOnlyList<string> protocolViolations = ProtocolViolations(events);
        IReadOnlyList<string> observabilityDefects = ObservabilityDefects(events);
        IReadOnlyList<string> secretLeaks = secrets.FindLeaks(logLines);

        report.RecordGlobalViolations(protocolViolations, observabilityDefects, secretLeaks);

        if (protocolViolations.Count == 0 && observabilityDefects.Count == 0 && secretLeaks.Count == 0)
        {
            return;
        }

        throw new GlobalAssertionViolationException(scope, protocolViolations, observabilityDefects, secretLeaks);
    }

    private static bool IsMalformed(RequestEvent e)
    {
        return string.IsNullOrWhiteSpace(e.Method)
            || string.IsNullOrWhiteSpace(e.Path)
            || e.Duration < TimeSpan.Zero
            || (e.StatusCode is null && string.IsNullOrEmpty(e.ErrorCode));
    }
}

/// <summary>Thrown by <see cref="RunAssertions.Enforce"/>. Never carries a secret value, only labels.</summary>
public sealed class GlobalAssertionViolationException : Exception
{
    internal GlobalAssertionViolationException(
        string scope,
        IReadOnlyList<string> protocolViolations,
        IReadOnlyList<string> observabilityDefects,
        IReadOnlyList<string> secretLeaks)
        : base(BuildMessage(scope, protocolViolations, observabilityDefects, secretLeaks))
    {
    }

    private static string BuildMessage(
        string scope,
        IReadOnlyList<string> protocolViolations,
        IReadOnlyList<string> observabilityDefects,
        IReadOnlyList<string> secretLeaks)
    {
        List<string> lines = [$"global assertions failed for '{scope}':"];
        if (protocolViolations.Count > 0)
        {
            lines.Add($"  ITG-020 ({protocolViolations.Count}): {string.Join("; ", protocolViolations)}");
        }

        if (secretLeaks.Count > 0)
        {
            lines.Add($"  ITG-021 ({secretLeaks.Count}): {string.Join("; ", secretLeaks)}");
        }

        if (observabilityDefects.Count > 0)
        {
            lines.Add($"  ITG-022 ({observabilityDefects.Count}): {string.Join("; ", observabilityDefects)}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
