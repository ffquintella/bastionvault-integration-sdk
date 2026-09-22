using System.Globalization;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// A comparable <c>major.minor.patch[-prerelease]</c> server version (ITG-002, ITG-031).
/// Pre-release ordering is the single rule this project needs and no more: a pre-release sorts
/// below the same release triple, and two pre-releases sort by ordinal string. The matrix's
/// non-numeric entries (<c>latest</c>, <c>main</c>) are deliberately NOT versions — see
/// <see cref="TryParse"/> returning <see langword="false"/> — because comparing against a moving
/// tag would silently answer "supported" for an unknown build.
/// </summary>
internal sealed record ServerVersion(int Major, int Minor, int Patch, string PreRelease)
    : IComparable<ServerVersion>
{
    public static bool TryParse(string? text, out ServerVersion version)
    {
        version = new ServerVersion(0, 0, 0, string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string raw = text.Trim();
        if (raw.StartsWith('v'))
        {
            raw = raw[1..];
        }

        string pre = string.Empty;
        int dash = raw.IndexOfAny(['-', '+']);
        if (dash >= 0)
        {
            pre = raw[(dash + 1)..];
            raw = raw[..dash];
        }

        string[] parts = raw.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        int[] numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new ServerVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public static ServerVersion Parse(string text)
    {
        return TryParse(text, out ServerVersion? v) ? v : throw new FormatException($"not a server version: '{text}'");
    }

    public int CompareTo(ServerVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Patch.CompareTo(other.Patch);
        if (c != 0)
        {
            return c;
        }

        return (PreRelease.Length == 0, other.PreRelease.Length == 0) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => -1,
            _ => string.CompareOrdinal(PreRelease, other.PreRelease),
        };
    }

    public static bool operator <(ServerVersion a, ServerVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(ServerVersion a, ServerVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(ServerVersion a, ServerVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(ServerVersion a, ServerVersion b) => a.CompareTo(b) >= 0;

    public override string ToString()
    {
        return PreRelease.Length == 0
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";
    }
}
