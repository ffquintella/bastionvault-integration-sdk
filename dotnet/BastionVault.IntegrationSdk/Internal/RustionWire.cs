using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// Path building, serialisation and RUS-001's digest handling for M10 slice e's <c>Rustion</c>
/// surface (12 §Rustion).
/// </summary>
internal static partial class RustionWire
{
    /// <summary>12 §Rustion: <c>rid</c> matches <c>rec_[A-Za-z0-9_-]+</c> (client-side format guard, mirrors <see cref="SshWire.RequireIp"/>).</summary>
    [GeneratedRegex(@"^rec_[A-Za-z0-9_-]+$")]
    private static partial Regex RidPattern();

    public static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }

    /// <summary><c>BV-INPUT-001</c> client-side, no request sent, on a malformed <paramref name="rid"/>.</summary>
    public static void RequireValidRid(string rid, string path)
    {
        if (string.IsNullOrEmpty(rid) || !RidPattern().IsMatch(rid))
        {
            throw KvWire.InvalidArgument("rid", $"must match `rec_[A-Za-z0-9_-]+`, got '{rid}'", path);
        }
    }

    /// <summary><c>{mount}/recordings/{rid}</c>, after validating <paramref name="rid"/>'s format.</summary>
    public static string RecordingPath(string mount, string rid)
    {
        string path = $"{Encode(mount)}/recordings/{UrlBuilder.EncodePathSegment(rid)}";
        RequireValidRid(rid, path);
        return path;
    }

    // ---------------------------------------------------------------- session

    /// <summary>
    /// <c>Session.Open</c>'s shadow guard, mirroring <c>FileWire.RequireValidSyncFields</c>: a
    /// non-object <see cref="RustionSessionRequest.Fields"/>, or one carrying a
    /// <c>credential_material</c> key, is refused (<c>BV-INPUT-001</c>) rather than silently
    /// dropped or shadowing the typed field on the wire.
    /// </summary>
    public static void RequireValidSessionFields(JsonElement? fields, string path)
    {
        if (fields is not { } value)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw KvWire.InvalidArgument("request.fields", "must be a JSON object when set", path);
        }

        if (value.TryGetProperty("credential_material", out _))
        {
            throw KvWire.InvalidArgument(
                "request.fields", "must not contain a `credential_material` key; it would shadow SessionRequest.CredentialMaterial on the wire", path);
        }
    }

    public static ReadOnlyMemory<byte> SerialiseSessionRequest(RustionSessionRequest request, string path)
    {
        RequireValidSessionFields(request.Fields, path);
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("credential_material", request.CredentialMaterial.Reveal());
            if (request.Fields is { } fields)
            {
                foreach (JsonProperty property in fields.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
            }
        });
    }

    /// <summary>
    /// <c>Session.OpenConnectOnly</c>. <c>kind</c> is not a settable property — 12 §Rustion pins it
    /// to the literal <c>secret</c> and names no other variant (D-M1c-25). R-33: the secret fields
    /// are written into the POST body only.
    /// </summary>
    public static ReadOnlyMemory<byte> SerialiseOpenConnectOnly(RustionSessionOpenConnectOnlyRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("resource_name", request.ResourceName);
            writer.WriteStartObject("credential_source");
            writer.WriteString("kind", "secret");
            writer.WriteString("secret_id", request.SecretId);
            writer.WriteEndObject();
            writer.WriteString("target_host", request.TargetHost);
            writer.WriteNumber("target_port", request.TargetPort);
            writer.WriteString("target_protocol", request.TargetProtocol);
            if (request.ProfileId is { } profileId)
            {
                writer.WriteString("profile_id", profileId);
            }

            if (request.ConnectTicket is { HasValue: true } ticket)
            {
                writer.WriteString("connect_ticket", ticket.Reveal());
            }
        });
    }

    public static ReadOnlyMemory<byte> SerialiseRenew(RustionSessionRenewRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("bastion_id", request.BastionId);
            writer.WriteString("session_id", request.SessionId);
            writer.WriteString("correlation_id", request.CorrelationId);
            writer.WriteNumber("extend_secs", (long)request.ExtendSecs.TotalSeconds);
        });
    }

    /// <summary>
    /// <c>Session.Kill</c>: 12 §Rustion names no fields. Inferred as <c>Renew</c>'s three
    /// identifying fields minus <c>extend_secs</c> (the <c>RepointResource</c>-style inference); a
    /// wrong name fails loudly as a server rejection, not silently.
    /// </summary>
    public static ReadOnlyMemory<byte> SerialiseKill(RustionSessionKillRequest request)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("bastion_id", request.BastionId);
            writer.WriteString("session_id", request.SessionId);
            writer.WriteString("correlation_id", request.CorrelationId);
        });
    }

    // ---------------------------------------------------------------- recordings

    /// <summary>
    /// RUS-001: a chunk's <c>bytes_b64</c> and <c>eof</c>. A missing/non-boolean <c>eof</c> fails
    /// loudly as <c>BV-PROTOCOL-002</c> rather than defaulting to <c>false</c>, which would risk an
    /// unbounded download loop against a non-conformant server.
    /// </summary>
    public static RustionRecordingChunk ReadChunk(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        string bytesB64 = SysWire.ReadString(data, "bytes_b64") ?? throw KvWire.EnvelopeMismatch(path, "bytes_b64");
        byte[] bytes = TransitWire.RequireBase64Decoded(bytesB64, path, "bytes_b64");

        if (!data.TryGetValue("eof", out JsonElement eofElement) || eofElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw KvWire.EnvelopeMismatch(path, "eof");
        }

        bool? digestVerified = data.TryGetValue("digest_verified", out JsonElement digestElement) && digestElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? digestElement.ValueKind == JsonValueKind.True
            : null;

        return new RustionRecordingChunk
        {
            Bytes = bytes,
            Eof = eofElement.ValueKind == JsonValueKind.True,
            DigestVerified = digestVerified,
            Sha256 = SysWire.ReadString(data, "sha256"),
        };
    }

    /// <summary><c>Recordings.Blob</c>'s fallback payload: the same <c>bytes_b64</c> field a chunk carries (RUS-001), delivered whole.</summary>
    public static byte[] ReadBytesB64(IReadOnlyDictionary<string, JsonElement>? data, string path)
    {
        if (data is null || !data.TryGetValue("bytes_b64", out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            throw KvWire.EnvelopeMismatch(path, "bytes_b64");
        }

        return TransitWire.RequireBase64Decoded(element.GetString() ?? string.Empty, path, "bytes_b64");
    }

    /// <summary>Concatenates every chunk's decoded payload in order (RUS-001).</summary>
    public static byte[] Concatenate(IReadOnlyList<ReadOnlyMemory<byte>> segments)
    {
        int total = 0;
        foreach (ReadOnlyMemory<byte> segment in segments)
        {
            total += segment.Length;
        }

        byte[] result = new byte[total];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in segments)
        {
            segment.Span.CopyTo(result.AsSpan(offset));
            offset += segment.Length;
        }

        return result;
    }

    /// <summary>
    /// RUS-001: verifies <paramref name="assembled"/> against a reported <paramref name="expectedSha256"/>.
    /// When <c>digest_verified</c> was true but no chunk ever reported a <c>sha256</c>, there is
    /// nothing to compare locally, so the server's own claim is trusted.
    /// </summary>
    public static void RequireDigestMatch(byte[] assembled, string? expectedSha256, string path)
    {
        if (string.IsNullOrEmpty(expectedSha256))
        {
            return;
        }

        string actual = Convert.ToHexStringLower(SHA256.HashData(assembled));
        if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw KvWire.Engine(ErrorCodes.ProtocolDigestMismatch, path, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["expected"] = expectedSha256,
                ["actual"] = actual,
            });
        }
    }

    public static ReadOnlyMemory<byte> SerialiseKeystrokeSearch(string query, int? limit)
    {
        return KvWire.Serialise(writer =>
        {
            writer.WriteString("query", query);
            if (limit is { } value)
            {
                writer.WriteNumber("limit", value);
            }
        });
    }
}
