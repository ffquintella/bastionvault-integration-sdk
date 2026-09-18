using System.Buffers;
using System.Text.Json;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The path building, client-side validation (TOT-001) and wire reading shared by
/// <see cref="TotpOperations"/> (11 — TOTP engine). One copy, following the
/// <see cref="KvWire"/>/<see cref="IdentityWire"/> precedent (D-M4-1, D-M7-*).
/// </summary>
internal static class TotpWire
{
    /// <summary><c>{mount}/keys/{name}</c>, both segments percent-encoded (TRN-020).</summary>
    public static string KeyPath(string mount, string name)
    {
        return $"{UrlBuilder.EncodePathFragment(MountPaths.ToWire(mount, "mount"))}/keys/{UrlBuilder.EncodePathSegment(RequireName(name))}";
    }

    /// <summary><c>{mount}/keys/</c>, for <c>LIST</c>.</summary>
    public static string KeysRoot(string mount)
    {
        return $"{UrlBuilder.EncodePathFragment(MountPaths.ToWire(mount, "mount"))}/keys/";
    }

    /// <summary><c>{mount}/code/{name}</c>, both segments percent-encoded (TRN-020).</summary>
    public static string CodePath(string mount, string name)
    {
        return $"{UrlBuilder.EncodePathFragment(MountPaths.ToWire(mount, "mount"))}/code/{UrlBuilder.EncodePathSegment(RequireName(name))}";
    }

    private static string RequireName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    /// <summary>
    /// TOT-001: client-side validation of a <see cref="TotpKeySpec"/> before any request is sent.
    /// </summary>
    public static void ValidateSpec(TotpKeySpec spec, string path)
    {
        ArgumentNullException.ThrowIfNull(spec);

        int modeCount = (spec.Generate ? 1 : 0)
            + (spec.Key is { HasValue: true } ? 1 : 0)
            + (spec.Url is { HasValue: true } ? 1 : 0);
        if (modeCount != 1)
        {
            throw InvalidArgument(
                "spec",
                "exactly one of `generate`, `key`, `url` must be set (TOT-001)",
                path);
        }

        if (spec.Digits is { } digits && digits != 6 && digits != 8)
        {
            throw InvalidArgument("spec.Digits", "must be 6 or 8 (TOT-001)", path);
        }

        if (spec.Period is { } period && period < 1)
        {
            throw InvalidArgument("spec.Period", "must be at least 1 second (TOT-001)", path);
        }

        if (spec.Algorithm is { } algorithm)
        {
            try
            {
                _ = AlgorithmWire(algorithm);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw InvalidArgument("spec.Algorithm", $"{(int)algorithm} is not a declared TotpAlgorithm", path);
            }
        }

        bool urlCarriesLabel = spec.Url is { HasValue: true } url && UrlHasLabel(url.Reveal()!);
        if (string.IsNullOrEmpty(spec.AccountName) && !urlCarriesLabel)
        {
            throw InvalidArgument(
                "spec.AccountName",
                "is required unless `Url` carries a label (TOT-001)",
                path);
        }
    }

    /// <summary>
    /// Whether an <c>otpauth://</c> URL carries a non-empty label — the path segment after the
    /// authority (<c>otpauth://totp/&lt;label&gt;?...</c>) — which TOT-001 accepts in place of an
    /// explicit <c>account_name</c>. Never logged: the caller holds the value in a
    /// <see cref="SecretString"/> and only this parse ever sees it revealed (TOT-002).
    /// </summary>
    private static bool UrlHasLabel(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            && parsed.AbsolutePath.Trim('/').Length > 0;
    }

    private static string AlgorithmWire(TotpAlgorithm algorithm)
    {
        return algorithm switch
        {
            TotpAlgorithm.Sha1 => "SHA1",
            TotpAlgorithm.Sha256 => "SHA256",
            TotpAlgorithm.Sha512 => "SHA512",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "not a declared TotpAlgorithm"),
        };
    }

    /// <summary>Serialises a create-key request body (OVR-007: absent members are omitted, never sent empty).</summary>
    public static ReadOnlyMemory<byte> Serialise(TotpKeySpec spec)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteBoolean("generate", spec.Generate);
        if (spec.Key is { HasValue: true } key)
        {
            writer.WriteString("key", key.Reveal());
        }

        if (spec.Url is { HasValue: true } url)
        {
            writer.WriteString("url", url.Reveal());
        }

        if (spec.KeySize is { } keySize)
        {
            writer.WriteNumber("key_size", keySize);
        }

        if (!string.IsNullOrEmpty(spec.Issuer))
        {
            writer.WriteString("issuer", spec.Issuer);
        }

        if (!string.IsNullOrEmpty(spec.AccountName))
        {
            writer.WriteString("account_name", spec.AccountName);
        }

        if (spec.Algorithm is { } algorithm)
        {
            writer.WriteString("algorithm", AlgorithmWire(algorithm));
        }

        if (spec.Digits is { } digits)
        {
            writer.WriteNumber("digits", digits);
        }

        if (spec.Period is { } period)
        {
            writer.WriteNumber("period", period);
        }

        if (spec.Skew is { } skew)
        {
            writer.WriteNumber("skew", skew);
        }

        if (spec.QrSize is { } qrSize)
        {
            writer.WriteNumber("qr_size", qrSize);
        }

        if (spec.Exported is { } exported)
        {
            writer.WriteBoolean("exported", exported);
        }

        if (spec.ReplayCheck is { } replayCheck)
        {
            writer.WriteBoolean("replay_check", replayCheck);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>
    /// The create-key response: <c>{name, generate}</c> always, plus <c>key</c>/<c>url</c>/<c>barcode</c>
    /// only when the server sent them (<c>generate &amp;&amp; exported</c>, per 11's Operations table).
    /// </summary>
    public static TotpKeyCreated ReadCreated(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        string name = ReadString(data, "name")
            ?? throw EnvelopeMismatch(path, "name");
        return new TotpKeyCreated
        {
            Name = name,
            Generate = ReadBool(data, "generate"),
            Key = ReadSecret(data, "key"),
            Url = ReadSecret(data, "url"),
            Barcode = ReadBarcode(data, "barcode", path),
        };
    }

    /// <summary>SYS-080-style read: <c>{generate, issuer, account_name, algorithm, digits, period, skew, replay_check}</c>. The seed is never returned.</summary>
    public static TotpKey ReadKey(IReadOnlyDictionary<string, JsonElement> data)
    {
        return new TotpKey
        {
            Generate = ReadBool(data, "generate"),
            Issuer = ReadString(data, "issuer"),
            AccountName = ReadString(data, "account_name"),
            Algorithm = ReadString(data, "algorithm"),
            Digits = ReadInt(data, "digits"),
            Period = ReadInt(data, "period"),
            Skew = ReadInt(data, "skew"),
            ReplayCheck = ReadBool(data, "replay_check"),
        };
    }

    /// <summary>TOT-004: the wire's <c>code</c> string, verbatim — a leading zero matters and is never parsed as a number.</summary>
    public static string ReadCode(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        return ReadString(data, "code") ?? throw EnvelopeMismatch(path, "code");
    }

    public static bool ReadValid(IReadOnlyDictionary<string, JsonElement> data, string path)
    {
        return data.TryGetValue("valid", out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw EnvelopeMismatch(path, "valid");
    }

    private static SecretString? ReadSecret(IReadOnlyDictionary<string, JsonElement> data, string name)
    {
        return ReadString(data, name) is { } value ? new SecretString(value) : null;
    }

    /// <summary>
    /// TOT-002: a present <c>barcode</c> that does not parse as base64 is a protocol error, not a
    /// silent absence — otherwise a corrupt barcode is indistinguishable from the normal case of
    /// the server omitting it (<c>generate &amp;&amp; exported</c> false). Only a genuinely absent
    /// field returns <see langword="null"/>.
    /// </summary>
    private static ReadOnlyMemory<byte>? ReadBarcode(IReadOnlyDictionary<string, JsonElement> data, string name, string path)
    {
        if (ReadString(data, name) is not { } base64)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw EnvelopeMismatch(path, name);
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    }

    public static BastionVaultException InvalidArgument(string argument, string reason, string path)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.InputInvalidArgument);
        return BastionVaultException.Request(
            ErrorCodes.InputInvalidArgument,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: path,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["argument"] = argument,
                ["reason"] = reason,
            });
    }

    public static BastionVaultException EnvelopeMismatch(string path, string field)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ProtocolUnexpectedResponse);
        return BastionVaultException.Request(
            ErrorCodes.ProtocolUnexpectedResponse,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: path,
            details: new Dictionary<string, object?>(StringComparer.Ordinal) { ["expectedField"] = field });
    }
}
