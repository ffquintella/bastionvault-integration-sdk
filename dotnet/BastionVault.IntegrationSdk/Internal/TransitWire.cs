using System.Globalization;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The wire reading, argument resolution and client-side rejection <see cref="TransitOperations"/>
/// shares (08 — Transit engine). One copy, so every crypto-endpoint binding resolves a dual
/// bytes-or-base64 argument, reads a <c>bvault:</c> ciphertext and normalises the two <c>keys</c>
/// wire shapes identically.
/// </summary>
internal static class TransitWire
{
    private const string Prefix = "bvault:";

    /// <summary>
    /// TRS-003: resolves one binary argument accepted either as raw bytes (which the SDK encodes)
    /// or as an already-base64 string through the sibling <c>*Base64</c> parameter — never both,
    /// which would be ambiguous about which spelling is authoritative and is refused client-side
    /// rather than one silently winning.
    /// </summary>
    public static string? ResolveBase64(byte[]? raw, string? base64Encoded, string argument, bool required, string path)
    {
        if (raw is not null && base64Encoded is not null)
        {
            throw InvalidArgument(argument, $"provide only one of `{argument}` or `{argument}Base64`, not both", path);
        }

        if (raw is not null)
        {
            return Convert.ToBase64String(raw);
        }

        if (base64Encoded is not null)
        {
            return base64Encoded;
        }

        if (required)
        {
            throw InvalidArgument(argument, $"`{argument}` or `{argument}Base64` is required", path);
        }

        return null;
    }

    /// <summary>
    /// TRS-002: <c>Transit.ParseCiphertext</c>. Accepts <c>bvault:v&lt;N&gt;:&lt;base64&gt;</c> and
    /// <c>bvault:v&lt;N&gt;:pqc:&lt;algo&gt;:&lt;base64&gt;</c>, raising <c>BV-INPUT-011</c> for
    /// anything else — including a value missing the <c>bvault:</c> prefix entirely, which mirrors
    /// the server's own <c>not a bvault ciphertext: missing `bvault:` prefix</c> string.
    /// </summary>
    public static TransitParsedCiphertext ParseCiphertext(string value, string path)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw InvalidCiphertextFormat(path);
        }

        string[] parts = value.Split(':');
        // ["bvault", "v<N>", "<base64>"] or ["bvault", "v<N>", "pqc", "<algo>", "<base64>"].
        if (parts.Length is not (3 or 5)
            || parts[0] != "bvault"
            || parts[1].Length < 2
            || parts[1][0] != 'v'
            || !int.TryParse(parts[1].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int version)
            || version < 1)
        {
            throw InvalidCiphertextFormat(path);
        }

        string? algo = null;
        string encoded;
        if (parts.Length == 5)
        {
            if (parts[2] != "pqc" || parts[3].Length == 0)
            {
                throw InvalidCiphertextFormat(path);
            }

            algo = parts[3];
            encoded = parts[4];
        }
        else
        {
            encoded = parts[2];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw InvalidCiphertextFormat(path);
        }

        return new TransitParsedCiphertext { Version = version, Algo = algo, Bytes = bytes };
    }

    /// <summary>
    /// TRS-002: validates the <c>bvault:</c> prefix client-side ahead of <c>Decrypt</c> and
    /// <c>Rewrap</c>, the two operations 08 names explicitly. Raises the same <c>BV-INPUT-011</c>
    /// <see cref="ParseCiphertext"/> would, without needing the parsed value.
    /// </summary>
    public static void RequireBvaultCiphertext(string ciphertext, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw InvalidCiphertextFormat(path);
        }
    }

    /// <summary>
    /// TRS-012: <c>Verify</c>/<c>VerifyHmac</c>'s <c>valid</c> field. Only a wire <c>true</c> or
    /// <c>false</c> answers the question; anything else (absent, <c>null</c>, a non-boolean body a
    /// misbehaving proxy might return) is a protocol violation, not a signature outcome, so this
    /// raises rather than defaulting to <see langword="false"/> the way <see cref="KvWire.ReadBool"/>
    /// would — a caller must never read "protocol error" as "signature invalid". Mirrors
    /// <c>TotpWire.ReadValid</c>'s exact shape.
    /// </summary>
    public static bool ReadValid(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        return wire.TryGetValue("valid", out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw KvWire.EnvelopeMismatch(path, "valid");
    }

    /// <summary>
    /// Decodes a server-supplied base64 field (<c>plaintext</c>, <c>random_bytes</c>, <c>sum</c>),
    /// raising the same <c>BV-PROTOCOL-*</c> envelope-mismatch <see cref="KvWire.EnvelopeMismatch"/>
    /// every other malformed-envelope reader in this file raises, rather than letting a raw
    /// <see cref="FormatException"/> escape the error model uncoded and unhinted.
    /// </summary>
    public static byte[] RequireBase64Decoded(string encoded, string path, string field)
    {
        try
        {
            return Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw KvWire.EnvelopeMismatch(path, field);
        }
    }

    /// <summary>TRS-011: <c>Random.bytes</c> capped at 4096, refused client-side above it.</summary>
    public static void RequireWithinRandomCap(int bytesCount, string path)
    {
        if (bytesCount > 4096)
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputOutOfRange);
            throw BastionVaultException.Request(
                ErrorCodes.InputOutOfRange,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: entry.Retryable,
                attempts: 0,
                path: path,
                details: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["argument"] = "bytes",
                    ["max"] = 4096,
                    ["value"] = bytesCount,
                });
        }
    }

    /// <summary>TRS-010: normalises the wire's <c>keys</c> object into <c>Map&lt;int, TransitKeyVersionInfo&gt;</c>.</summary>
    public static IReadOnlyDictionary<int, TransitKeyVersionInfo> ReadKeyVersions(IReadOnlyDictionary<string, JsonElement> wire, string path)
    {
        Dictionary<int, TransitKeyVersionInfo> result = [];
        if (!wire.TryGetValue("keys", out JsonElement keys) || keys.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in keys.EnumerateObject())
        {
            if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            {
                continue;
            }

            result[version] = property.Value.ValueKind switch
            {
                // Symmetric shape: {version: "<creation_time>"} (ISO-8601) or {version: <epoch>}
                // (DR-0021 F8a: a measured server sends a bare Unix-epoch number here).
                JsonValueKind.String or JsonValueKind.Number => new TransitKeyVersionInfo
                {
                    CreationTime = ParseInstant(property.Value, path),
                },
                // Asymmetric shape: {version: {public_key, creation_time}}; creation_time is the
                // same string-or-epoch-number union (DR-0021 F8a).
                JsonValueKind.Object => new TransitKeyVersionInfo
                {
                    CreationTime = property.Value.TryGetProperty("creation_time", out JsonElement created)
                        ? ParseInstant(created, path)
                        : throw KvWire.EnvelopeMismatch(path, "creation_time"),
                    PublicKey = property.Value.TryGetProperty("public_key", out JsonElement publicKey) && publicKey.ValueKind == JsonValueKind.String
                        ? publicKey.GetString()
                        : null,
                },
                _ => throw KvWire.EnvelopeMismatch(path, "keys"),
            };
        }

        return result;
    }

    /// <summary>08 §Operations: the shared key-metadata shape <c>CreateKey</c>, <c>ReadKey</c>, <c>RotateKey</c>, <c>ConfigureKey</c> and <c>TrimKey</c> all return.</summary>
    public static TransitKey ReadKey(IReadOnlyDictionary<string, JsonElement> wire, string name, string path)
    {
        return new TransitKey
        {
            Name = KvWire.ReadString(wire, "name") ?? name,
            Type = KvWire.ReadString(wire, "type") ?? TransitKeyTypes.ChaCha20Poly1305,
            LatestVersion = KvWire.ReadInt(wire, "latest_version") ?? 0,
            MinDecryptionVersion = KvWire.ReadInt(wire, "min_decryption_version") ?? 0,
            MinAvailableVersion = KvWire.ReadInt(wire, "min_available_version") ?? 0,
            DeletionAllowed = KvWire.ReadBool(wire, "deletion_allowed"),
            Exportable = KvWire.ReadBool(wire, "exportable"),
            Derived = KvWire.ReadBool(wire, "derived"),
            ConvergentEncryption = KvWire.ReadBool(wire, "convergent_encryption"),
            Keys = ReadKeyVersions(wire, path),
        };
    }

    /// <summary>
    /// TRS-010 / DR-0021 F8a: <c>creation_time</c> is a string-or-number union — a JSON string is
    /// ISO-8601 (as specified), a JSON number is a measured server's bare Unix epoch in seconds.
    /// Anything else, including a number too large for <see cref="long"/> or an unparseable
    /// string, is the SDK's own <c>BV-PROTOCOL-002</c> envelope mismatch, never a raw
    /// <see cref="JsonException"/> or <see cref="InvalidOperationException"/> escaping the error
    /// model.
    /// </summary>
    private static DateTimeOffset ParseInstant(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long epochSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string? text = element.GetString();
            if (!string.IsNullOrEmpty(text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            {
                return parsed;
            }
        }

        throw KvWire.EnvelopeMismatch(path, "creation_time");
    }

    private static BastionVaultException InvalidArgument(string argument, string reason, string path)
    {
        return KvWire.InvalidArgument(argument, reason, path);
    }

    private static BastionVaultException InvalidCiphertextFormat(string path)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidCiphertextFormat);
        return BastionVaultException.Request(
            ErrorCodes.InputInvalidCiphertextFormat,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: path,
            details: null);
    }
}
