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
    private static readonly HashSet<char> PathReserved = [.. "\"#%/<>?`\\^{}|[]"];

    // TRN-021: query additionally encodes '&', '+', '=' and MUST NOT encode '/'.
    private static readonly HashSet<char> QueryReserved = BuildQueryReserved();

    private static HashSet<char> BuildQueryReserved()
    {
        HashSet<char> set = [.. PathReserved, '&', '+', '='];
        _ = set.Remove('/');
        return set;
    }

    /// <summary>
    /// Splits <paramref name="rawPath"/> on its first <c>?</c> (if any), strips a leading <c>/</c>
    /// (TRN-002), and returns the encoded path and, when present, the encoded query string
    /// (without a leading <c>?</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split happens whether or not <paramref name="pathIsEncoded"/> is set; only the
    /// <i>encoding</i> is skipped. That is safe because a pre-encoded path can contain no literal
    /// <c>?</c> — <see cref="EncodePathSegment"/> and <see cref="EncodePathFragment"/> both put
    /// <c>?</c> in the reserved set, so a value that carried one arrives as <c>%3F</c> — and it is
    /// necessary because KV v2's selectors are query parameters on a path whose segments are
    /// caller-supplied (KV2-001, KV2-030). Before M4 this arm returned the whole string as the
    /// path, which was correct only because the sole pre-encoded caller (the login runner) never
    /// appends a query.
    /// </para>
    /// <para>
    /// A pre-encoded query is passed through verbatim for the same reason as the path: its values
    /// were encoded while they were still values, by <see cref="EncodeQueryValue"/>, and encoding
    /// is not idempotent.
    /// </para>
    /// </remarks>
    public static (string EncodedPath, string? EncodedQuery) SplitAndEncode(string rawPath, bool pathIsEncoded = false)
    {
        string path = rawPath.StartsWith('/') ? rawPath[1..] : rawPath;
        int queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        string query = string.Empty;
        if (queryIndex >= 0)
        {
            query = path[(queryIndex + 1)..];
            path = path[..queryIndex];
        }

        string encodedPath = pathIsEncoded ? path : EncodePath(path);
        string? encodedQuery = queryIndex < 0
            ? null
            : pathIsEncoded ? query : EncodeQuery(query);
        return (encodedPath, encodedQuery);
    }

    /// <summary>
    /// TRN-021 for <b>one</b> query-parameter value, for the same reason
    /// <see cref="EncodePathSegment"/> exists for one path segment: <c>&amp;</c>, <c>=</c> and
    /// <c>+</c> are structure once the value has been interpolated into a query string, so a
    /// caller-supplied value (KV v2's <c>env</c>, KV2-001) is encoded while it is still a value.
    /// </summary>
    public static string EncodeQueryValue(string value)
    {
        return Encode(value, QueryReserved);
    }

    /// <summary>
    /// TRN-020 for <b>one</b> segment: percent-encodes the separator <c>/</c> and the query
    /// delimiter <c>?</c> as well as the rest of the reserved set, so a caller-supplied value
    /// cannot change the shape of the path it is interpolated into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SplitAndEncode"/> cannot do this, and not by oversight: it receives a whole
    /// logical path, where <c>/</c> <i>is</i> the separator TRN-020 says to preserve and <c>?</c>
    /// begins the query TRN-021 encodes differently. Once a path parameter has been interpolated
    /// into a string, those two characters are indistinguishable from structure. So a value that
    /// may contain them is encoded here, while it is still a value, and the result is carried to
    /// <see cref="SplitAndEncode"/> with <c>pathIsEncoded</c> set — otherwise the <c>%</c> of the
    /// triplet would itself be escaped and the segment double-encoded.
    /// </para>
    /// <para>
    /// AUT-030's <c>username</c> is the first path parameter in this SDK that can legitimately
    /// contain either character, which is why the seam appears now. Encoding is not idempotent, so
    /// a segment goes through exactly one of the two routes, never both.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <see cref="PathReserved"/> is already TRN-020's full set, <c>/</c>, <c>?</c> and <c>%</c>
    /// included, so no second table is needed. What differs is only the input:
    /// <see cref="EncodePath"/> feeds it substrings that can never contain <c>/</c> (it split on
    /// them) or <c>?</c> (the query was split off first).
    /// </remarks>
    public static string EncodePathSegment(string segment)
    {
        return Encode(segment, PathReserved);
    }

    /// <summary>
    /// TRN-020 for a multi-segment path fragment: each <c>/</c>-separated segment is encoded and
    /// the separators are preserved. Used with <see cref="EncodePathSegment"/> by a caller that
    /// assembles a path from parts of both kinds — an auth mount, which may contain separators, and
    /// a single parameter such as AUT-030's username, which may not.
    /// </summary>
    public static string EncodePathFragment(string fragment)
    {
        return EncodePath(fragment);
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
                    _ = builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            else
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
