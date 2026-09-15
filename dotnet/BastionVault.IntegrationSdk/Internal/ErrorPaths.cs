using System.Text;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>ERR-003's path redaction, applied once where the error is built so no surface can miss it.</summary>
internal static class ErrorPaths
{
    /// <summary>The four token-bearing route shapes ERR-003 names.</summary>
    private static readonly string[] TokenSegments = ["lookup", "renew", "revoke", "revoke-orphan"];

    /// <summary>
    /// Replaces the segment that follows <c>lookup</c>, <c>renew</c>, <c>revoke</c> or
    /// <c>revoke-orphan</c> with <c>&lt;redacted&gt;</c> (ERR-003). Applied to
    /// <see cref="BastionVaultException.Path"/> at construction, so the one-line form, the verbose
    /// form and any hint that interpolates the path are all redacted by the same rule rather than
    /// by three that can drift.
    /// </summary>
    public static string? Redact(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf('/', StringComparison.Ordinal) < 0)
        {
            return path;
        }

        string[] segments = path.Split('/');
        bool changed = false;
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index + 1].Length == 0)
            {
                continue;
            }

            foreach (string marker in TokenSegments)
            {
                if (string.Equals(segments[index], marker, StringComparison.OrdinalIgnoreCase))
                {
                    segments[index + 1] = "<redacted>";
                    changed = true;
                    break;
                }
            }
        }

        return changed ? string.Join('/', segments) : path;
    }

    /// <summary>Collapses every newline and control character to a single space (ERR-002's one-line rule).</summary>
    public static string OneLine(string value)
    {
        StringBuilder builder = new(value.Length);
        bool lastWasSpace = false;
        foreach (char character in value)
        {
            bool isBreak = character is '\r' or '\n' || char.IsControl(character);
            if (isBreak)
            {
                if (!lastWasSpace)
                {
                    _ = builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            _ = builder.Append(character);
            lastWasSpace = character == ' ';
        }

        return builder.ToString();
    }
}
