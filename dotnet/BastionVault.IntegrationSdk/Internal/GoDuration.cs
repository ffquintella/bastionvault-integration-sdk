using System.Globalization;
using System.Text;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// KV2-010's "serialise Go-style": the duration spelling BastionVault's engines accept on the wire
/// (<c>"0s"</c>, <c>"1h"</c>, <c>"90m"</c>, <c>"1h30m"</c>).
/// </summary>
/// <remarks>
/// Only the three units the server's parser accepts for these fields are emitted — <c>h</c>,
/// <c>m</c>, <c>s</c> — largest first, zero components omitted, and a zero duration as the
/// <c>"0s"</c> KV2-010 names literally. Sub-second precision is rounded to whole seconds because no
/// field in 07 is sub-second and emitting <c>"1.5s"</c> would be a spelling the specification does
/// not state.
/// </remarks>
internal static class GoDuration
{
    /// <summary>Formats <paramref name="value"/> Go-style.</summary>
    public static string Format(TimeSpan value)
    {
        long totalSeconds = (long)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);
        if (totalSeconds == 0)
        {
            return "0s";
        }

        StringBuilder builder = new();
        if (totalSeconds < 0)
        {
            _ = builder.Append('-');
            totalSeconds = -totalSeconds;
        }

        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;

        if (hours != 0)
        {
            _ = builder.Append(hours.ToString(CultureInfo.InvariantCulture)).Append('h');
        }

        if (minutes != 0)
        {
            _ = builder.Append(minutes.ToString(CultureInfo.InvariantCulture)).Append('m');
        }

        if (seconds != 0)
        {
            _ = builder.Append(seconds.ToString(CultureInfo.InvariantCulture)).Append('s');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses a Go-style duration string in the <c>h</c>/<c>m</c>/<c>s</c> spelling
    /// <see cref="Format"/> emits (and the wider variety a server may return, e.g. <c>"90m"</c>),
    /// or <see langword="null"/> when <paramref name="value"/> is absent or not a duration the SDK
    /// understands. Never throws.
    /// </summary>
    public static TimeSpan? TryParse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        int index = 0;
        bool negative = false;
        if (value[0] is '-' or '+')
        {
            negative = value[0] == '-';
            index = 1;
        }

        if (index >= value.Length)
        {
            return null;
        }

        bool sawHours = false;
        bool sawMinutes = false;
        bool sawSeconds = false;
        long hours = 0;
        long minutes = 0;
        long seconds = 0;

        while (index < value.Length)
        {
            int start = index;
            while (index < value.Length && char.IsAsciiDigit(value[index]))
            {
                index++;
            }

            if (index == start || index >= value.Length
                || !long.TryParse(
                    value.AsSpan(start, index - start),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long number))
            {
                return null;
            }

            char unit = value[index];
            index++;

            switch (unit)
            {
                case 'h' when !sawHours && !sawMinutes && !sawSeconds:
                    sawHours = true;
                    hours = number;
                    break;
                case 'm' when !sawMinutes && !sawSeconds:
                    sawMinutes = true;
                    minutes = number;
                    break;
                case 's' when !sawSeconds:
                    sawSeconds = true;
                    seconds = number;
                    break;
                default:
                    return null;
            }
        }

        if (!sawHours && !sawMinutes && !sawSeconds)
        {
            return null;
        }

        long totalSeconds = hours * 3600 + minutes * 60 + seconds;
        if (negative)
        {
            totalSeconds = -totalSeconds;
        }

        try
        {
            return TimeSpan.FromSeconds(totalSeconds);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
