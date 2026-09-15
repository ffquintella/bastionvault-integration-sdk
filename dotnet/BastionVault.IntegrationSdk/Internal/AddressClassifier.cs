using System.Net;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// DSC-001's address classification and DSC-002's rejection, in one place, so
/// <see cref="ConfigurationResolver"/> (which needs <see cref="ClientConfig.AddressUri"/> and
/// <see cref="ClientConfig.AddressIsClusterName"/>) and <see cref="DiscoveryEngine"/> (which needs
/// the owner name, the scheme and the DSC-012 synthesised candidate) cannot disagree about what an
/// address means.
/// </summary>
/// <remarks>
/// <para>
/// This grows <c>ParseAddress</c> from two classifications to six (D-M5-18). The pre-M5 code
/// returned <c>IsClusterName = true</c> for <b>anything</b> without <c>://</c>, so
/// <c>bv-1.corp.example:8200</c> and <c>10.0.0.5</c> were cluster names, and DSC-001 makes both
/// literal: the already-public <see cref="ClientConfig.AddressIsClusterName"/> /
/// <see cref="ClientConfig.AddressUri"/> pair therefore changes value for those inputs. That is a
/// deliberate correction — shipping DSC-001 alongside a member that contradicts it is the
/// alternative.
/// </para>
/// <para>
/// The <c>http://</c>-on-a-cluster-name row is implemented exactly as section 13's table states it,
/// and no wider: an address with <c>://</c> is literal unless it is <c>http</c>, carries no explicit
/// port, and names a host that is not an IP literal. The symmetric <c>https://name</c> case is
/// <b>not</b> treated as discovery, because the table does not say it is and D-M1c-25 forbids
/// widening a list the specification closed.
/// </para>
/// </remarks>
internal static class AddressClassifier
{
    /// <summary>What an address means.</summary>
    /// <param name="IsDiscovery">Whether the address triggers SRV discovery.</param>
    /// <param name="Uri">The parsed literal URL, or <see langword="null"/> in discovery mode.</param>
    /// <param name="OwnerName">The bare cluster name to resolve, or <see langword="null"/> in literal mode.</param>
    /// <param name="Scheme">The scheme every candidate URL will carry (<c>http</c> or <c>https</c>).</param>
    /// <param name="Literal">The DSC-012 single candidate in literal mode, or <see langword="null"/> in discovery mode.</param>
    /// <param name="Endpoint">The base address requests are sent to in literal mode, or <see langword="null"/> in discovery mode.</param>
    internal readonly record struct Classification(
        bool IsDiscovery,
        Uri? Uri,
        string? OwnerName,
        string Scheme,
        Candidate? Literal,
        string? Endpoint);

    /// <summary>
    /// Classifies <paramref name="raw"/>. Raises <c>BV-CONFIG-001</c> for a malformed address and
    /// for DSC-002's unbracketed IPv6; this is validation position 1 in DR-0003's fixed order, so
    /// first-failure-wins is unchanged (D-M5-18).
    /// </summary>
    public static Classification Classify(string raw, DiscoveryConfig discovery, bool clusterDiscovery)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw Invalid();
        }

        if (raw.Contains("://", StringComparison.Ordinal))
        {
            return ClassifyUrl(raw, clusterDiscovery);
        }

        if (raw.Any(char.IsWhiteSpace) || raw.Any(char.IsControl) || raw.Contains('/', StringComparison.Ordinal))
        {
            throw Invalid();
        }

        // DSC-002: `fe80::1:8200` cannot be told from an address whose port is `8200` and whose host
        // ends in `:1`, so it is rejected rather than guessed at.
        if (raw.Count(character => character == ':') >= 2 && !raw.StartsWith('['))
        {
            throw AmbiguousIpv6();
        }

        return ClassifyBare(raw, discovery, clusterDiscovery);
    }

    /// <summary>
    /// DSC-010's owner name: <c>{SrvService}.{name}</c>, or <paramref name="ownerName"/> verbatim
    /// when it already starts with <c>_</c>.
    /// </summary>
    public static string SrvOwnerName(string ownerName, DiscoveryConfig discovery)
    {
        return ownerName.StartsWith('_') ? ownerName : $"{discovery.SrvService}.{ownerName}";
    }

    /// <summary>DSC-013: the candidate URL, with the port always explicit.</summary>
    public static string CandidateUrl(string scheme, string target, int port)
    {
        return $"{scheme}://{target}:{port}";
    }

    private static Classification ClassifyUrl(string raw, bool clusterDiscovery)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            || (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid();
        }

        // Normalised by comparison rather than by ToLowerInvariant, which CA1308 forbids.
        string scheme = string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
        if (clusterDiscovery
            && string.Equals(scheme, "http", StringComparison.Ordinal)
            && !HasExplicitPort(raw)
            && !IsIpLiteral(uri.Host))
        {
            // Section 13's last row: an `http://` prefix on a cluster name is discovery with the
            // scheme forced to http.
            return new Classification(IsDiscovery: true, Uri: null, uri.Host, scheme, Literal: null, Endpoint: null);
        }

        // The endpoint is the raw address verbatim, not a re-rendered `uri.ToString()`: M1b built
        // every request URI from `config.Address`, and literal-mode behaviour must stay
        // byte-identical (D-M5-11).
        Candidate literal = new(CandidateUrl(scheme, uri.Host, uri.Port), uri.Host, uri.Port, null, null);
        return new Classification(IsDiscovery: false, uri, OwnerName: null, scheme, literal, raw);
    }

    /// <summary>
    /// Whether the address <b>as written</b> carries a <c>:port</c>. Decided on the raw string and
    /// never on <see cref="Uri.IsDefaultPort"/> (D-M5-21): that property is true for
    /// <c>http://vault.corp.example:80</c>, an address the section 13 table's row 2 makes literal,
    /// so reading it would route an operator's explicit <c>:80</c> to SRV discovery and silently
    /// replace it with <see cref="DiscoveryConfig.DefaultPort"/>.
    /// </summary>
    private static bool HasExplicitPort(string raw)
    {
        int schemeEnd = raw.IndexOf("://", StringComparison.Ordinal) + 3;
        string rest = raw[schemeEnd..];
        // The authority ends at the first '/', '?' or '#'; anything after that is path or query and
        // cannot contain the port.
        int authorityEnd = rest.IndexOfAny(['/', '?', '#']);
        string authority = authorityEnd < 0 ? rest : rest[..authorityEnd];
        // A bracketed IPv6 host contains colons of its own, so the port can only follow the ']'.
        int hostEnd = authority.StartsWith('[') ? authority.IndexOf(']', StringComparison.Ordinal) + 1 : 0;
        return authority.IndexOf(':', hostEnd) >= 0;
    }

    private static Classification ClassifyBare(string raw, DiscoveryConfig discovery, bool clusterDiscovery)
    {
        (string host, int? explicitPort) = SplitHostAndPort(raw);
        bool isIpLiteral = IsIpLiteral(host);

        // A bare DNS name with no port is the one discovery shape; `ClusterDiscovery = false` forces
        // it literal at `DefaultScheme://name:DefaultPort` (DSC-001).
        if (explicitPort is null && !isIpLiteral && clusterDiscovery)
        {
            return new Classification(IsDiscovery: true, Uri: null, host, discovery.DefaultScheme, Literal: null, Endpoint: null);
        }

        int port = explicitPort ?? discovery.DefaultPort;
        string url = CandidateUrl(discovery.DefaultScheme, host, port);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            throw Invalid();
        }

        return new Classification(
            IsDiscovery: false,
            uri,
            OwnerName: null,
            discovery.DefaultScheme,
            new Candidate(url, host, port, null, null),
            url);
    }

    /// <summary>
    /// Splits <c>host</c> from <c>host:port</c>, honouring <c>[::1]:8200</c>'s brackets. A value
    /// after the colon that is not a port number is a malformed address, not a host whose name
    /// contains a colon.
    /// </summary>
    private static (string Host, int? Port) SplitHostAndPort(string raw)
    {
        if (raw.StartsWith('['))
        {
            int closing = raw.IndexOf(']', StringComparison.Ordinal);
            if (closing < 0)
            {
                throw Invalid();
            }

            string bracketed = raw[..(closing + 1)];
            string remainder = raw[(closing + 1)..];
            if (remainder.Length == 0)
            {
                return (bracketed, null);
            }

            return remainder.StartsWith(':') ? (bracketed, ParsePort(remainder[1..])) : throw Invalid();
        }

        int separator = raw.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? (raw, null) : (raw[..separator], ParsePort(raw[(separator + 1)..]));
    }

    private static int ParsePort(string value)
    {
        return int.TryParse(value, out int port) && port is > 0 and <= 65535 ? port : throw Invalid();
    }

    /// <summary>Whether <paramref name="host"/> is an IPv4 literal or a bracketed IPv6 literal.</summary>
    private static bool IsIpLiteral(string host)
    {
        string unbracketed = host.Length >= 2 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
        return IPAddress.TryParse(unbracketed, out _);
    }

    private static BastionVaultException Invalid()
    {
        return BastionVaultException.Config(
            ErrorCodes.ConfigInvalidAddress,
            ConfigCatalogue.InvalidAddressMessage,
            ConfigCatalogue.InvalidAddressHint);
    }

    private static BastionVaultException AmbiguousIpv6()
    {
        return BastionVaultException.Config(
            ErrorCodes.ConfigInvalidAddress,
            ConfigCatalogue.InvalidAddressMessage,
            "An IPv6 literal must be bracketed, with or without a port (`[::1]` or `[::1]:8200`). "
                + "Unbracketed, `::1:8200` is ambiguous — it cannot be told from a host whose name "
                + "ends in `:1` listening on port 8200 — and `::1` is the same shape (DSC-002).");
    }
}
