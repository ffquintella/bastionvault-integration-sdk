using System.Globalization;
using System.Text;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Builds the request URL exactly as specified in
/// <c>specifications/03-transport-and-protocol.md#url-construction</c> (TRN-020, TRN-021). A raw
/// logical path may carry an embedded <c>?query</c> component, split off before either half is
/// encoded.
/// </summary>
internal static class UrlBuilder
{
    // TRN-020: percent-encode controls, space, and this exact set. '/' is a segment separator and
    // is preserved between segments; a literal '/' inside a segment (there should be none once the
    // path is split on '/') is still encoded because it is in this set.
    private static readonly HashSet<char> PathReserved = new("\"#%/<>?`\\^{}|[]");

    // TRN-021: query additionally encodes '&', '+', '=' and MUST NOT encode '/'.
    private static readonly HashSet<char> QueryReserved = BuildQueryReserved();

    private static HashSet<char> BuildQueryReserved()
    {
        HashSet<char> set = new(PathReserved) { '&', '+', '=' };
        set.Remove('/');
        return set;
    }

    /// <summary>
    /// Splits <paramref name="rawPath"/> on its first <c>?</c> (if any), strips a leading <c>/</c>
    /// (TRN-002), and returns the encoded path and, when present, the encoded query string
    /// (without a leading <c>?</c>).
    /// </summary>
    public static (string EncodedPath, string? EncodedQuery) SplitAndEncode(string rawPath)
    {
        string path = rawPath.StartsWith('/') ? rawPath[1..] : rawPath;
        int queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        string query = string.Empty;
        if (queryIndex >= 0)
        {
            query = path[(queryIndex + 1)..];
            path = path[..queryIndex];
        }

        string encodedPath = EncodePath(path);
        string? encodedQuery = queryIndex >= 0 ? EncodeQuery(query) : null;
        return (encodedPath, encodedQuery);
    }

    private static string EncodePath(string path)
    {
        string[] segments = path.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            segments[index] = Encode(segments[index], PathReserved);
        }

        return string.Join('/', segments);
    }

    private static string EncodeQuery(string query)
    {
        if (query.Length == 0)
        {
            return string.Empty;
        }

        string[] pairs = query.Split('&');
        for (int index = 0; index < pairs.Length; index++)
        {
            string pair = pairs[index];
            int equalsIndex = pair.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex < 0)
            {
                pairs[index] = Encode(pair, QueryReserved);
            }
            else
            {
                string key = pair[..equalsIndex];
                string value = pair[(equalsIndex + 1)..];
                pairs[index] = Encode(key, QueryReserved) + "=" + Encode(value, QueryReserved);
            }
        }

        return string.Join('&', pairs);
    }

    private static string Encode(string value, HashSet<char> reserved)
    {
        StringBuilder builder = new();
        foreach (char c in value)
        {
            if (char.IsControl(c) || c == ' ' || reserved.Contains(c))
            {
                foreach (byte b in Encoding.UTF8.GetBytes(c.ToString()))
                {
                    builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
