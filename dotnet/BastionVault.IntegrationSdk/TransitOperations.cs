using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The Transit engine surface (08 — Transit engine), reached from
/// <see cref="BastionVaultClient.Transit"/>. <c>mount</c> defaults to <c>"transit"</c> everywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>This SDK performs no cryptography (00 §Purpose, §Non-goals, OVR-002).</b> Every member here
/// base64-encodes the caller's bytes, sends one request, and parses the response; every
/// cryptographic operation happens on the server. That is why <see cref="EncryptAsync"/> and
/// friends look like a thin request/response mapping and not a client-side cipher — because that is
/// exactly what they are.
/// </para>
/// <para>
/// TRS-003: every binary argument accepts raw bytes (encoded here) or a pre-encoded string through
/// the sibling <c>*Base64</c> parameter, resolved by <see cref="TransitWire.ResolveBase64"/> so a
/// caller cannot supply both and have one silently win.
/// </para>
/// </remarks>
public sealed class TransitOperations
{
    private const string DefaultMount = "transit";

    private readonly LogicalOperations logical;

    internal TransitOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        Byok = new TransitByokOperations(logical);
    }

    /// <summary>The feature-gated BYOK surface (08 §Operations' <c>Transit.Byok.*</c> row).</summary>
    public TransitByokOperations Byok { get; }

    /// <summary>TRS-002: parses a <c>bvault:</c>-framed value without making a request.</summary>
    /// <remarks>
    /// HTTP call: none — a client-side parser. Wire params: none; <paramref name="ciphertext"/>
    /// is parsed locally. Returns a <see cref="TransitParsedCiphertext"/>, never
    /// <see langword="null"/>; throws <c>BV-INPUT-011</c> for a malformed prefix or version.
    /// Conformance: Standard (TRS-002). Error codes beyond the common set (ERR-061):
    /// <c>BV-INPUT-011 InvalidCiphertextFormat</c>.
    /// </remarks>
    /// <spec>Transit.ParseCiphertext — TRS-002</spec>
    public static TransitParsedCiphertext ParseCiphertext(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);
        return TransitWire.ParseCiphertext(ciphertext, "transit/parse-ciphertext");
    }

    // ---------------------------------------------------------------- key lifecycle

    /// <summary>Lists the key names under <paramref name="mount"/>: <c>LIST {mount}/keys/</c>.</summary>
    /// <remarks>
    /// Wire params: <c>mount</c> builds the route; no query or body params. Returns an
    /// empty list when the backend has none (TRN-050), never <see langword="null"/>.
    /// Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Transit.ListKeys — TRN-050</spec>
    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/keys/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>Creates a named Transit key: <c>POST {mount}/keys/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <c>name</c>/<c>mount</c> build the route; body carries <c>key_type</c>, <c>exportable</c>, <c>deletion_allowed</c>, <c>derived</c>, <c>convergent_encryption</c>, each omitted when unset.
    /// Returns the created <see cref="TransitKey"/>, never <see langword="null"/>, with per-version metadata normalised per TRS-010.
    /// Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-002 KeyTypeConflict</c>, <c>BV-INPUT-100</c> (derived / convergent_encryption validation).
    /// </remarks>
    /// <spec>Transit.CreateKey — TRS-010</spec>
    public async Task<TransitKey> CreateKeyAsync(
        string name, TransitKeyOptions? keyOptions = null, string mount = DefaultMount,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = KeyPath(mount, name);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            if (keyOptions is null)
            {
                return;
            }

            if (keyOptions.KeyType is { } keyType)
            {
                writer.WriteString("key_type", keyType);
            }

            if (keyOptions.Exportable is { } exportable)
            {
                writer.WriteBoolean("exportable", exportable);
            }

            if (keyOptions.DeletionAllowed is { } deletionAllowed)
            {
                writer.WriteBoolean("deletion_allowed", deletionAllowed);
            }

            if (keyOptions.Derived is { } derived)
            {
                writer.WriteBoolean("derived", derived);
            }

            if (keyOptions.ConvergentEncryption is { } convergent)
            {
                writer.WriteBoolean("convergent_encryption", convergent);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TransitWire.ReadKey(response?.Data ?? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal), name, path);
    }

    /// <summary>Reads a Transit key's metadata: <c>GET {mount}/keys/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <c>name</c>/<c>mount</c> build the route; no body. A missing key is <see langword="null"/> (TRN-050), never an exception.
    /// Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Transit.ReadKey — TRN-050</spec>
    public async Task<TransitKey?> ReadKeyAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = KeyPath(mount, name);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response is null ? null : TransitWire.ReadKey(response.Data ?? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal), name, path);
    }

    /// <summary>Deletes a Transit key: <c>DELETE {mount}/keys/{name}</c> → <c>204</c>.</summary>
    /// <remarks>
    /// Wire params: <c>name</c>/<c>mount</c> build the route; no body. Returns <see langword="void"/> on the server's <c>204</c>.
    /// Conformance: Standard (every typed operation is built on <c>Logical.Delete</c> per TRN-001, but that primitive-exposure MUST is TRN-001's own, not this operation's; 08 states no delete-specific behaviour beyond the route). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-003 DeletionNotAllowed</c>.
    /// </remarks>
    /// <spec>Transit.DeleteKey — 08-transit-engine.md</spec>
    public async Task DeleteKeyAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", KeyPath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Rotates a Transit key to a new version: <c>POST {mount}/keys/{name}/rotate</c>.</summary>
    /// <remarks>
    /// Wire params: <c>name</c>/<c>mount</c> build the route; no body. Returns the rotated <see cref="TransitKey"/>, never <see langword="null"/>, normalised per TRS-010.
    /// Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>.
    /// </remarks>
    /// <spec>Transit.RotateKey — TRS-010</spec>
    public async Task<TransitKey> RotateKeyAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{KeyPath(mount, name)}/rotate";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TransitWire.ReadKey(response?.Data ?? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal), name, path);
    }

    /// <summary>Reconfigures a Transit key's version bounds and deletability: <c>POST {mount}/keys/{name}/config</c>.</summary>
    /// <remarks>
    /// Wire params: <c>name</c>/<c>mount</c> build the route; body carries <c>min_decryption_version</c>, <c>min_available_version</c>, <c>deletion_allowed</c>, each omitted when unset (<c>0</c> = unchanged per 08).
    /// Returns the updated <see cref="TransitKey"/>, never <see langword="null"/>. Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-INPUT-100</c> (bounds violation).
    /// </remarks>
    /// <spec>Transit.ConfigureKey — TRS-010</spec>
    public async Task<TransitKey> ConfigureKeyAsync(
        string name, TransitKeyConfig config, string mount = DefaultMount,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(config);
        string path = $"{KeyPath(mount, name)}/config";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            if (config.MinDecryptionVersion is { } minDecryption)
            {
                writer.WriteNumber("min_decryption_version", minDecryption);
            }

            if (config.MinAvailableVersion is { } minAvailable)
            {
                writer.WriteNumber("min_available_version", minAvailable);
            }

            if (config.DeletionAllowed is { } deletionAllowed)
            {
                writer.WriteBoolean("deletion_allowed", deletionAllowed);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TransitWire.ReadKey(response?.Data ?? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal), name, path);
    }

    /// <summary>Discards old key versions below <c>min_available_version</c>: <c>POST {mount}/keys/{name}/trim</c>.</summary>
    /// <remarks>
    /// Wire params: <c>name</c>/<c>mount</c> build the route; no body. Returns the trimmed <see cref="TransitTrimResult"/> (key metadata plus <c>dropped_versions</c>), never <see langword="null"/>.
    /// Conformance: Standard (TRS-010). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-INPUT-100</c> (would leave the key with no versions).
    /// </remarks>
    /// <spec>Transit.TrimKey — TRS-010</spec>
    public async Task<TransitTrimResult> TrimKeyAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{KeyPath(mount, name)}/trim";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        return new TransitTrimResult
        {
            Key = TransitWire.ReadKey(wire, name, path),
            DroppedVersions = ReadIntList(wire, "dropped_versions"),
        };
    }

    // ---------------------------------------------------------------- crypto endpoints

    /// <summary>
    /// <c>POST {mount}/encrypt/{name}</c>. TRS-013: <paramref name="context"/>/<paramref name="contextBase64"/>
    /// is accepted only for a key created with <c>Derived = true</c> — the server rejects it
    /// otherwise (08 §Server error strings, <c>BV-INPUT-100</c>); convergent encryption further
    /// requires <c>Derived</c>, which this method does not itself check (D-M1c-25: the server
    /// already names the rejection, so the SDK does not guess it client-side).
    /// </summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries base64 <c>plaintext</c> (required) and <c>context</c> (optional). Returns <see cref="TransitEncryptResult"/>, never <see langword="null"/>. Conformance: Standard (TRS-013). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-005 OperationNotSupportedByKeyType</c>, <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.Encrypt — TRS-013</spec>
    public async Task<TransitEncryptResult> EncryptAsync(
        string name,
        byte[]? plaintext = null,
        string? plaintextBase64 = null,
        byte[]? context = null,
        string? contextBase64 = null,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/encrypt/{UrlBuilder.EncodePathSegment(name)}";
        string? plaintextWire = TransitWire.ResolveBase64(plaintext, plaintextBase64, "plaintext", required: true, path);
        string? contextWire = TransitWire.ResolveBase64(context, contextBase64, "context", required: false, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("plaintext", plaintextWire);
            if (contextWire is not null)
            {
                writer.WriteString("context", contextWire);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "ciphertext");
        return new TransitEncryptResult
        {
            Ciphertext = KvWire.ReadString(wire, "ciphertext") ?? throw KvWire.EnvelopeMismatch(path, "ciphertext"),
            KeyVersion = KvWire.ReadInt(wire, "key_version") ?? 0,
        };
    }

    /// <summary>
    /// <c>POST {mount}/decrypt/{name}</c>. TRS-002: <paramref name="ciphertext"/> is validated for
    /// the <c>bvault:</c> prefix before the request is sent (<c>BV-INPUT-011</c>). TRS-013: the
    /// plaintext is returned in <see cref="SecretBytes"/> so it never reaches a log by accident.
    /// </summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries <c>ciphertext</c> and optional base64 <c>context</c>. Returns the plaintext as <see cref="SecretBytes"/>, never <see langword="null"/>. Conformance: Standard (TRS-002). Errors beyond the common set (ERR-061): <c>BV-INPUT-011 InvalidCiphertextFormat</c>, <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-004 VersionBelowMinDecryption</c>, <c>BV-TRANSIT-005 OperationNotSupportedByKeyType</c>, <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.Decrypt — TRS-002</spec>
    public async Task<SecretBytes> DecryptAsync(
        string name,
        string ciphertext,
        byte[]? context = null,
        string? contextBase64 = null,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/decrypt/{UrlBuilder.EncodePathSegment(name)}";
        TransitWire.RequireBvaultCiphertext(ciphertext, path);
        string? contextWire = TransitWire.ResolveBase64(context, contextBase64, "context", required: false, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("ciphertext", ciphertext);
            if (contextWire is not null)
            {
                writer.WriteString("context", contextWire);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "plaintext");
        string encoded = KvWire.ReadString(wire, "plaintext") ?? throw KvWire.EnvelopeMismatch(path, "plaintext");
        return new SecretBytes(TransitWire.RequireBase64Decoded(encoded, path, "plaintext"));
    }

    /// <summary><c>POST {mount}/rewrap/{name}</c>. TRS-002: the same client-side <c>bvault:</c> validation as <see cref="DecryptAsync"/>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries <c>ciphertext</c> and optional base64 <c>context</c>. Returns <see cref="TransitEncryptResult"/> re-wrapped under the latest key version, never <see langword="null"/>. Conformance: Standard (TRS-002). Errors beyond the common set (ERR-061): <c>BV-INPUT-011 InvalidCiphertextFormat</c>, <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-004 VersionBelowMinDecryption</c>, <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.Rewrap — TRS-002</spec>
    public async Task<TransitEncryptResult> RewrapAsync(
        string name,
        string ciphertext,
        byte[]? context = null,
        string? contextBase64 = null,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/rewrap/{UrlBuilder.EncodePathSegment(name)}";
        TransitWire.RequireBvaultCiphertext(ciphertext, path);
        string? contextWire = TransitWire.ResolveBase64(context, contextBase64, "context", required: false, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("ciphertext", ciphertext);
            if (contextWire is not null)
            {
                writer.WriteString("context", contextWire);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "ciphertext");
        return new TransitEncryptResult
        {
            Ciphertext = KvWire.ReadString(wire, "ciphertext") ?? throw KvWire.EnvelopeMismatch(path, "ciphertext"),
            KeyVersion = KvWire.ReadInt(wire, "key_version") ?? 0,
        };
    }

    /// <summary>Signs <paramref name="input"/> with a signature-type Transit key: <c>POST {mount}/sign/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries base64 <c>input</c> (required). Returns <see cref="TransitSignResult"/>, never <see langword="null"/>. Conformance: Standard (TRS-003). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-005 OperationNotSupportedByKeyType</c>, <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.Sign — TRS-003</spec>
    public async Task<TransitSignResult> SignAsync(
        string name,
        byte[]? input = null,
        string? inputBase64 = null,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/sign/{UrlBuilder.EncodePathSegment(name)}";
        string? inputWire = TransitWire.ResolveBase64(input, inputBase64, "input", required: true, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("input", inputWire));

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "signature");
        return new TransitSignResult
        {
            Signature = KvWire.ReadString(wire, "signature") ?? throw KvWire.EnvelopeMismatch(path, "signature"),
            KeyVersion = KvWire.ReadInt(wire, "key_version") ?? 0,
        };
    }

    /// <summary>
    /// <c>POST {mount}/verify/{name}</c>. TRS-012: a wire <c>{valid: false}</c> becomes
    /// <see langword="false"/>; a framing or algorithm error (<c>BV-INPUT-011</c>,
    /// <c>BV-TRANSIT-005</c>/<c>006</c>) still raises, exactly as any other server error does —
    /// nothing here catches it. An envelope missing <c>valid</c> entirely (or carrying a
    /// non-boolean) also raises rather than reporting a false "signature invalid".
    /// </summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries base64 <c>input</c> and <c>signature</c>. Returns <see langword="bool"/>, never <see langword="null"/>. Conformance: Standard (TRS-012). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-INPUT-011</c>, <c>BV-TRANSIT-005 OperationNotSupportedByKeyType</c>, <c>BV-TRANSIT-006 AlgorithmMismatch</c>, <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.Verify — TRS-012</spec>
    public async Task<bool> VerifyAsync(
        string name,
        string signature,
        byte[]? input = null,
        string? inputBase64 = null,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentException.ThrowIfNullOrEmpty(signature);
        string path = $"{Encode(mount)}/verify/{UrlBuilder.EncodePathSegment(name)}";
        string? inputWire = TransitWire.ResolveBase64(input, inputBase64, "input", required: true, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("input", inputWire);
            writer.WriteString("signature", signature);
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TransitWire.ReadValid(response?.Data ?? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal), path);
    }

    /// <summary>Computes an HMAC over <paramref name="input"/> with an <c>hmac</c>-type Transit key: <c>POST {mount}/hmac/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries base64 <c>input</c> and <c>algorithm</c> (default <c>sha2-256</c>). Returns <see cref="TransitHmacResult"/>, never <see langword="null"/>. Conformance: Standard (TRS-003). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-005 OperationNotSupportedByKeyType</c>, <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.Hmac — TRS-003</spec>
    public async Task<TransitHmacResult> HmacAsync(
        string name,
        byte[]? input = null,
        string? inputBase64 = null,
        string algorithm = TransitHashAlgorithms.Sha2256,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentException.ThrowIfNullOrEmpty(algorithm);
        string path = $"{Encode(mount)}/hmac/{UrlBuilder.EncodePathSegment(name)}";
        string? inputWire = TransitWire.ResolveBase64(input, inputBase64, "input", required: true, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("input", inputWire);
            writer.WriteString("algorithm", algorithm);
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "hmac");
        return new TransitHmacResult
        {
            Hmac = KvWire.ReadString(wire, "hmac") ?? throw KvWire.EnvelopeMismatch(path, "hmac"),
            KeyVersion = KvWire.ReadInt(wire, "key_version") ?? 0,
        };
    }

    /// <summary>
    /// <c>POST {mount}/verify/{name}/hmac</c>. TRS-012: the same false-vs-raise split as
    /// <see cref="VerifyAsync"/>, including the missing/non-boolean <c>valid</c> raise.
    /// </summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries base64 <c>input</c>, <c>hmac</c>, <c>algorithm</c>. Returns <see langword="bool"/>, never <see langword="null"/>. Conformance: Standard (TRS-012). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-006 AlgorithmMismatch</c> (a pqc-tagged HMAC framing), <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.VerifyHmac — TRS-012</spec>
    public async Task<bool> VerifyHmacAsync(
        string name,
        string hmac,
        byte[]? input = null,
        string? inputBase64 = null,
        string algorithm = TransitHashAlgorithms.Sha2256,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentException.ThrowIfNullOrEmpty(hmac);
        ArgumentException.ThrowIfNullOrEmpty(algorithm);
        string path = $"{Encode(mount)}/verify/{UrlBuilder.EncodePathSegment(name)}/hmac";
        string? inputWire = TransitWire.ResolveBase64(input, inputBase64, "input", required: true, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("input", inputWire);
            writer.WriteString("hmac", hmac);
            writer.WriteString("algorithm", algorithm);
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TransitWire.ReadValid(response?.Data ?? new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal), path);
    }

    // ---------------------------------------------------------------- datakeys

    /// <summary>Generates a datakey wrapped (or plaintext-and-wrapped) by an AEAD or KEM Transit key: <c>POST {mount}/datakey/{plaintext|wrapped}/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c>/<paramref name="mode"/> build the route (<c>plaintext</c> or <c>wrapped</c> segment); body carries optional base64 <c>context</c>. Returns <see cref="TransitDataKeyResult"/>, never <see langword="null"/>; <see cref="TransitDataKeyResult.Plaintext"/> is <see langword="null"/> in <c>Wrapped</c> mode. Conformance: Standard (TRS-013). Errors beyond the common set (ERR-061): <c>BV-TRANSIT-001 KeyNotFound</c>, <c>BV-TRANSIT-005 OperationNotSupportedByKeyType</c>, <c>BV-TRANSIT-006 AlgorithmMismatch</c>, <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.GenerateDataKey — TRS-013</spec>
    public async Task<TransitDataKeyResult> GenerateDataKeyAsync(
        string name,
        TransitDataKeyMode mode = TransitDataKeyMode.Wrapped,
        byte[]? context = null,
        string? contextBase64 = null,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string modeSegment = mode == TransitDataKeyMode.Plaintext ? "plaintext" : "wrapped";
        string path = $"{Encode(mount)}/datakey/{modeSegment}/{UrlBuilder.EncodePathSegment(name)}";
        string? contextWire = TransitWire.ResolveBase64(context, contextBase64, "context", required: false, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            if (contextWire is not null)
            {
                writer.WriteString("context", contextWire);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "ciphertext");
        string? plaintextWire = KvWire.ReadString(wire, "plaintext");
        return new TransitDataKeyResult
        {
            Ciphertext = KvWire.ReadString(wire, "ciphertext") ?? throw KvWire.EnvelopeMismatch(path, "ciphertext"),
            KeyVersion = KvWire.ReadInt(wire, "key_version") ?? 0,
            Plaintext = plaintextWire is null ? null : new SecretBytes(TransitWire.RequireBase64Decoded(plaintextWire, path, "plaintext")),
        };
    }

    /// <summary><c>POST {mount}/datakey/unwrap/{name}</c>. TRS-013: the plaintext is held in <see cref="SecretBytes"/>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries <c>ciphertext</c>. Returns the unwrapped plaintext as <see cref="SecretBytes"/>, never <see langword="null"/>. Conformance: Standard (TRS-013). Errors beyond the common set (ERR-061): <c>BV-INPUT-011 InvalidCiphertextFormat</c>, <c>BV-TRANSIT-001 KeyNotFound</c>.</remarks>
    /// <spec>Transit.UnwrapDataKey — TRS-013</spec>
    public async Task<SecretBytes> UnwrapDataKeyAsync(
        string name,
        string ciphertext,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);
        string path = $"{Encode(mount)}/datakey/unwrap/{UrlBuilder.EncodePathSegment(name)}";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("ciphertext", ciphertext));

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "plaintext");
        string encoded = KvWire.ReadString(wire, "plaintext") ?? throw KvWire.EnvelopeMismatch(path, "plaintext");
        return new SecretBytes(TransitWire.RequireBase64Decoded(encoded, path, "plaintext"));
    }

    // ---------------------------------------------------------------- random / hash

    /// <summary><c>POST {mount}/random</c>. TRS-011: <paramref name="bytesCount"/> above 4096 is refused client-side (<c>BV-INPUT-004</c>).</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <c>bytes</c> (default 32). Returns the random bytes, never <see langword="null"/>. Conformance: Standard (TRS-011). Errors beyond the common set (ERR-061): <c>BV-INPUT-004</c> (server-side cap, mirrored client-side per TRS-011).</remarks>
    /// <spec>Transit.Random — TRS-011</spec>
    public async Task<byte[]> RandomAsync(
        int bytesCount = 32, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/random";
        TransitWire.RequireWithinRandomCap(bytesCount, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteNumber("bytes", bytesCount));

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "random_bytes");
        string encoded = KvWire.ReadString(wire, "random_bytes") ?? throw KvWire.EnvelopeMismatch(path, "random_bytes");
        return TransitWire.RequireBase64Decoded(encoded, path, "random_bytes");
    }

    /// <summary>Hashes <paramref name="input"/> server-side without a key: <c>POST {mount}/hash</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries base64 <c>input</c> and <c>algorithm</c> (<c>sha2-256/384/512</c>). Returns the digest bytes, never <see langword="null"/>. Conformance: Standard (TRS-003). Errors beyond the common set (ERR-061): <c>BV-INPUT-012 NotBase64</c>.</remarks>
    /// <spec>Transit.Hash — TRS-003</spec>
    public async Task<byte[]> HashAsync(
        byte[]? input = null,
        string? inputBase64 = null,
        string algorithm = TransitHashAlgorithms.Sha2256,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentException.ThrowIfNullOrEmpty(algorithm);
        string path = $"{Encode(mount)}/hash";
        string? inputWire = TransitWire.ResolveBase64(input, inputBase64, "input", required: true, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("input", inputWire);
            writer.WriteString("algorithm", algorithm);
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "sum");
        string encoded = KvWire.ReadString(wire, "sum") ?? throw KvWire.EnvelopeMismatch(path, "sum");
        return TransitWire.RequireBase64Decoded(encoded, path, "sum");
    }

    private static string KeyPath(string mount, string name)
    {
        return $"{Encode(mount)}/keys/{UrlBuilder.EncodePathSegment(name)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }

    private static int[] ReadIntList(IReadOnlyDictionary<string, System.Text.Json.JsonElement> wire, string name)
    {
        return wire.TryGetValue(name, out System.Text.Json.JsonElement value) && value.ValueKind == System.Text.Json.JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == System.Text.Json.JsonValueKind.Number).Select(item => item.GetInt32()).ToArray()
            : [];
    }
}

/// <summary>
/// 08 §Operations' feature-gated <c>Transit.Byok.*</c> row: <c>wrapping_key</c>,
/// <c>keys/{name}/import</c>, <c>import_version</c>. Section 08 names only the routes, not their
/// body or response shapes, so this binds the routes generically (a caller-supplied body map, a
/// wire-shaped result map) rather than inventing a typed contract the specification does not pin —
/// exactly D-M1c-25's rule against a plausible guess. When the server does not carry the
/// <c>transit_byok</c> feature, every member here surfaces <c>BV-SERVER-004</c> unchanged, like any
/// other unsupported-feature route (08 §Operations).
/// </summary>
public sealed class TransitByokOperations
{
    private readonly LogicalOperations logical;

    internal TransitByokOperations(LogicalOperations logical)
    {
        this.logical = logical;
    }

    /// <summary>Reads the BYOK wrapping key so a caller can wrap an external key for import: <c>GET {mount}/wrapping_key</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns the wrapping-key response map, or <see langword="null"/> on a <c>404</c> empty body (TRN-050). Conformance: Standard (08 §Operations). CNF-043 is the SDK-wide rule that a missing endpoint surfaces BV-SERVER-004; it governs every operation, not this one. Errors beyond the common set (ERR-061): <c>BV-SERVER-004 UnsupportedByServer</c> when the <c>transit_byok</c> feature is absent.</remarks>
    /// <spec>Transit.Byok.WrappingKey — 08-transit-engine.md</spec>
    public async Task<IReadOnlyDictionary<string, System.Text.Json.JsonElement>?> WrappingKeyAsync(
        string mount = "transit", RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{UrlBuilder.EncodePathFragment(mount.Trim('/'))}/wrapping_key", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Imports an externally-wrapped key as a new Transit key: <c>POST {mount}/keys/{name}/import</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; <paramref name="body"/>'s entries are written as the request body's top-level fields verbatim — section 08 names only the routes, not a typed body (D-M1c-25). Returns the server's response map, or <see langword="null"/> per the shared envelope rules. Conformance: Standard (08 §Operations). CNF-043 is the SDK-wide rule that a missing endpoint surfaces BV-SERVER-004; it governs every operation, not this one. Errors beyond the common set (ERR-061): <c>BV-SERVER-004 UnsupportedByServer</c> when the <c>transit_byok</c> feature is absent.</remarks>
    /// <spec>Transit.Byok.ImportKey — 08-transit-engine.md</spec>
    public async Task<IReadOnlyDictionary<string, System.Text.Json.JsonElement>?> ImportKeyAsync(
        string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement> body, string mount = "transit",
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(body);
        string path = $"{UrlBuilder.EncodePathFragment(mount.Trim('/'))}/keys/{UrlBuilder.EncodePathSegment(name)}/import";
        ReadOnlyMemory<byte> serialised = SerialiseBody(body);
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, serialised, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Imports a new version of an existing BYOK key: <c>POST {mount}/keys/{name}/import_version</c>.</summary>
    /// <remarks>Wire params: as <see cref="ImportKeyAsync"/>, at <c>{mount}/keys/{name}/import_version</c>. Returns the server's response map, or <see langword="null"/> per the shared envelope rules. Conformance: Standard (08 §Operations). CNF-043 is the SDK-wide rule that a missing endpoint surfaces BV-SERVER-004; it governs every operation, not this one. Errors beyond the common set (ERR-061): <c>BV-SERVER-004 UnsupportedByServer</c> when the <c>transit_byok</c> feature is absent.</remarks>
    /// <spec>Transit.Byok.ImportVersion — 08-transit-engine.md</spec>
    public async Task<IReadOnlyDictionary<string, System.Text.Json.JsonElement>?> ImportVersionAsync(
        string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement> body, string mount = "transit",
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(body);
        string path = $"{UrlBuilder.EncodePathFragment(mount.Trim('/'))}/keys/{UrlBuilder.EncodePathSegment(name)}/import_version";
        ReadOnlyMemory<byte> serialised = SerialiseBody(body);
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, serialised, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Writes <paramref name="body"/>'s entries directly as the request body's top-level fields (KvV1Operations.WriteAsync's precedent).</summary>
    private static ReadOnlyMemory<byte> SerialiseBody(IReadOnlyDictionary<string, System.Text.Json.JsonElement> body)
    {
        return KvWire.Serialise(writer =>
        {
            foreach ((string key, System.Text.Json.JsonElement value) in body)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }
        });
    }
}
