using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Path building, serialisation and RSC-001's client-side guard for M10 slice b's
/// <c>Resources</c> surface (12 §Resources): <c>{mount}/resources/*</c>, <c>{mount}/secrets/*</c>
/// and <c>{mount}/v2/connect/*</c>.
/// </summary>
internal static class ResourceWire
{
    public static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }

    public static string ResourcePath(string mount, string name)
    {
        return $"{Encode(mount)}/resources/{UrlBuilder.EncodePathSegment(name)}";
    }

    public static string SecretPath(string mount, string resource, string key)
    {
        return $"{Encode(mount)}/secrets/{UrlBuilder.EncodePathSegment(resource)}/{UrlBuilder.EncodePathSegment(key)}";
    }

    /// <summary>
    /// RSC-002: builds a <see cref="ResourceSecret"/> from <see cref="Response.Data"/> (never
    /// <see cref="Response.Raw"/>'s whole envelope), wrapping each field value redacting. A JSON
    /// string is unquoted via <c>GetString</c> first — <c>GetRawText</c> would redact the
    /// quoted-and-escaped wire text, not the actual value, the one exception every other
    /// wire-to-<see cref="SecretString"/> site in this codebase already avoids.
    /// </summary>
    public static ResourceSecret ReadSecret(IReadOnlyDictionary<string, JsonElement> data)
    {
        Dictionary<string, SecretString> values = new(StringComparer.Ordinal);
        foreach ((string key, JsonElement value) in data)
        {
            values[key] = new SecretString(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
        }

        return new ResourceSecret { Data = values };
    }

    /// <summary>RSC-001: <c>resource</c> empty → <c>BV-INPUT-001</c> client-side, no request sent.</summary>
    public static void RequireResource(string resource, string path)
    {
        if (string.IsNullOrEmpty(resource))
        {
            throw KvWire.InvalidArgument("resource", "must not be empty (RSC-001)", path);
        }
    }

    public static ReadOnlyMemory<byte> SerialiseSearch(ResourceSearchQuery query)
    {
        return KvWire.Serialise(writer =>
        {
            if (query.Q is { } q)
            {
                writer.WriteString("q", q);
            }

            if (query.Type is { } type)
            {
                writer.WriteString("type", type);
            }

            if (query.Offset is { } offset)
            {
                writer.WriteNumber("offset", offset);
            }

            if (query.Limit is { } limit)
            {
                writer.WriteNumber("limit", limit);
            }
        });
    }

    public static ReadOnlyMemory<byte> SerialiseMfaBegin(ConnectMfaBeginRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("resource", request.Resource);
            writer.WriteString("profile_id", request.ProfileId);
        });
    }

    public static ReadOnlyMemory<byte> SerialiseMfaVerify(ConnectMfaVerifyRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("resource", request.Resource);
            writer.WriteString("profile_id", request.ProfileId);
            writer.WriteString("method", request.Method);
            if (request.TotpCode is { } totpCode)
            {
                writer.WriteString("totp_code", totpCode);
            }

            if (request.Credential is { } credential)
            {
                writer.WriteString("credential", credential);
            }
        });
    }

    public static ConnectMfaVerifyResult ReadMfaVerifyResult(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        string ticket = SysWire.ReadString(data, "connect_ticket") ?? throw KvWire.EnvelopeMismatch(path, "connect_ticket");
        return new ConnectMfaVerifyResult { ConnectTicket = new SecretString(ticket) };
    }

    /// <summary>
    /// R-33 guard: <c>connect_ticket</c> is written into the POST body only, exactly like every
    /// other field here — never a path segment or query string.
    /// </summary>
    public static ReadOnlyMemory<byte> SerialiseAuthorize(ConnectAuthorizeRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("resource", request.Resource);
            writer.WriteString("profile_id", request.ProfileId);
            if (request.ConnectTicket is { HasValue: true } ticket)
            {
                writer.WriteString("connect_ticket", ticket.Reveal());
            }
        });
    }
}
