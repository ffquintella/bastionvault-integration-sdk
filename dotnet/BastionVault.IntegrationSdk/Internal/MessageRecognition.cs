using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Step 4 of the ERR-020 mapping algorithm: the ordered Appendix B §2 rule list, applied to the
/// normalised server message before the status table (D-M1c-3). The rules themselves are generated
/// (<see cref="ErrorCatalogData.Rules"/>); this file is only the matcher and the D-M1c-4 capture
/// interpreter, and it is deliberately regex-free apart from the one normalisation suffix so the
/// three SDKs implement the same four capture shapes rather than three regex dialects.
/// </summary>
internal static class MessageRecognition
{
    private static readonly Regex RetryAfterSuffix = new(
        @"\s*\(\s*retry after\s+(\d+)\s*s\s*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The second spelling of the same fact. Appendix B §2's normalisation rule is written for a
    /// trailing <c>(retry after Ns)</c>, but AUT-011 spells the <c>BV-AUTH-006</c> message
    /// <c>account temporarily locked; try again in N seconds</c> — and that is the message the
    /// conformance fixture carries, because it is the one the server sends.
    /// </summary>
    /// <remarks>
    /// <c>DetailsCaptureKind.RetryAfterSecs</c> therefore means "the retry delay in seconds,
    /// however the server spelled it", not "the parenthesised suffix". Widened here rather than by
    /// adding a fifth capture kind: the closed set exists so the three SDKs implement the same
    /// shapes, and two spellings of one number is one shape. <b>Parity item:</b> the Rust and
    /// Python passes must widen their matcher too, or <c>Details.retry_after_secs</c> is absent in
    /// two of three languages for the message the server actually sends.
    /// </remarks>
    private static readonly Regex TryAgainInSeconds = new(
        @"try again in\s+(\d+)\s*second",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The outcome of step 4: a code, plus whatever ERR-035 could capture.</summary>
    internal readonly record struct Recognised(string Code, IReadOnlyDictionary<string, object?> Details);

    /// <summary>
    /// D-M1c-3's matching-time normalisation: trim; strip a single trailing <c>.</c>; strip a
    /// trailing <c>(retry after Ns)</c>; lower-case. The original server message is never modified —
    /// it stays on <see cref="BastionVaultException.ServerMessage"/> exactly as the server sent it.
    /// </summary>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "D-M1c-3 and Appendix B §2 define recognition against the lower-cased message, and every rule literal is generated lower-cased; upper-casing would invert the comparison, not secure it.")]
    public static string Normalise(string message)
    {
        string text = message.Trim();
        if (text.EndsWith('.'))
        {
            text = text[..^1];
        }

        text = RetryAfterSuffix.Replace(text, string.Empty);
        return text.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// The first rule in Appendix B §2 table order whose text and status guard both hold, or
    /// <see langword="null"/> to fall through to the D-M1b-4 status table unchanged (D-M1c-3).
    /// </summary>
    public static Recognised? Recognise(string? serverMessage, int statusCode, string? path)
    {
        if (string.IsNullOrWhiteSpace(serverMessage))
        {
            return null;
        }

        string original = serverMessage.Trim();
        string normalised = Normalise(serverMessage);
        if (normalised.Length == 0)
        {
            return null;
        }

        foreach (RecognitionRule rule in ErrorCatalogData.Rules)
        {
            if (!Matches(rule, normalised, statusCode, path))
            {
                continue;
            }

            IReadOnlyDictionary<string, object?> details = rule.CaptureIndex >= 0
                ? Capture(ErrorCatalogData.Captures[rule.CaptureIndex], original)
                : EmptyDetails;
            return new Recognised(rule.Code, details);
        }

        return null;
    }

    private static readonly Dictionary<string, object?> EmptyDetails = new(StringComparer.Ordinal);

    private static bool Matches(RecognitionRule rule, string normalised, int statusCode, string? path)
    {
        if (rule.PathContains is { } scope
            && (path is null || !path.Contains(scope, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (rule.Status is int status && statusCode != status)
        {
            return false;
        }

        if (rule.StatusClass is int statusClass && statusCode / 100 != statusClass)
        {
            return false;
        }

        bool textMatches = rule.Kind switch
        {
            // The rule text keeps Appendix B's own spelling, including the load-bearing trailing
            // space in `machine `/`key `/`version `/`role `/`ip `; only `prefix` can observe it,
            // so `exact` and `contains` compare against the trimmed literal.
            RecognitionKind.Exact => string.Equals(normalised, rule.Text.Trim(), StringComparison.Ordinal),
            RecognitionKind.Prefix => normalised.StartsWith(rule.Text, StringComparison.Ordinal),
            _ => normalised.Contains(rule.Text.Trim(), StringComparison.Ordinal),
        };

        if (!textMatches)
        {
            return false;
        }

        foreach (string required in rule.ContainsAll)
        {
            if (!normalised.Contains(required.Trim(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies one D-M1c-4 capture to the original (case-preserving) server message. A capture that
    /// fails is not an error: the code is still assigned and the key is simply absent, because
    /// recognition must never be more fragile than the code it produces.
    /// </summary>
    private static Dictionary<string, object?> Capture(DetailsCapture capture, string original)
    {
        Dictionary<string, object?> details = new(StringComparer.Ordinal);
        switch (capture.Kind)
        {
            case DetailsCaptureKind.TokenAfterPrefix:
                if (TokenAfterPrefix(original, capture.Prefix.Length) is { } token)
                {
                    details[capture.Keys[0]] = token;
                }

                break;

            case DetailsCaptureKind.RetryAfterSecs:
                string trimmed = TrimTrailingStop(original);
                Match retry = RetryAfterSuffix.Match(trimmed);
                if (!retry.Success)
                {
                    retry = TryAgainInSeconds.Match(trimmed);
                }

                if (retry.Success && int.TryParse(retry.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
                {
                    details[capture.Keys[0]] = seconds;
                }

                break;

            case DetailsCaptureKind.TwoInts:
                List<int> numbers = Integers(original);
                if (numbers.Count >= 2)
                {
                    details[capture.Keys[0]] = numbers[0];
                    details[capture.Keys[1]] = numbers[1];
                }

                break;

            default:
                string[] items = TokenListBetween(original, capture.Prefix.Length, capture.Suffix);
                if (items.Length > 0)
                {
                    details[capture.Keys[0]] = items;
                }

                break;
        }

        return details;
    }

    private static string TrimTrailingStop(string value)
    {
        string trimmed = value.Trim();
        return trimmed.EndsWith('.') ? trimmed[..^1].TrimEnd() : trimmed;
    }

    /// <summary>
    /// The first whitespace-delimited token after the capture's prefix. Every D-M1c-4 capture
    /// attaches to a <see cref="RecognitionKind.Prefix"/> rule — the generator fails if one ever
    /// does not — so the prefix is always at index 0 of the trimmed message and there is no
    /// "prefix not found" case to branch on.
    /// </summary>
    private static string? TokenAfterPrefix(string original, int start)
    {
        string rest = original[start..].TrimStart();
        int end = 0;
        while (end < rest.Length && !char.IsWhiteSpace(rest[end]))
        {
            end++;
        }

        string token = Clean(rest[..end]);
        return token.Length > 0 ? token : null;
    }

    private static List<int> Integers(string original)
    {
        List<int> numbers = new();
        int index = 0;
        while (index < original.Length)
        {
            if (!char.IsAsciiDigit(original[index]))
            {
                index++;
                continue;
            }

            int start = index;
            while (index < original.Length && char.IsAsciiDigit(original[index]))
            {
                index++;
            }

            if (int.TryParse(original.AsSpan(start, index - start), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                numbers.Add(value);
            }
        }

        return numbers;
    }

    /// <summary>
    /// The comma/space separated tokens between the capture's prefix and its suffix. A missing
    /// suffix yields an empty span and therefore no key, which D-M1c-4 explicitly allows — it is
    /// not an error and needs no branch of its own.
    /// </summary>
    private static string[] TokenListBetween(string original, int start, string suffix)
    {
        string rest = original[start..];
        int end = rest.AsSpan().IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        return rest[..Math.Max(end, 0)]
            .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Clean)
            .Where(item => item.Length > 0)
            .ToArray();
    }

    private static string Clean(string value) => value.Trim().Trim('`', '"', '\'').TrimEnd('.', ',', ';', ':');
}
