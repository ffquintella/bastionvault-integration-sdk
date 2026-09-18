namespace BastionVault.IntegrationSdk;

/// <summary>
/// TRS-001: the transit key types as constants. A plain <see cref="string"/> field carries the type
/// everywhere in this surface (<see cref="TransitKeyOptions.KeyType"/>, <see cref="TransitKey.Type"/>)
/// rather than an enum, exactly the reasoning KV2-011's <c>Operation</c> field already applies
/// (D-M4-5): an enum would need an invented member for a server key type this SDK does not know
/// about yet, which is precisely the guess D-M1c-25 forbids. An unrecognised string already passes
/// through unchanged because nothing here rejects one, so TRS-001's "pass unknown strings through"
/// holds without a dedicated wrapper type.
/// </summary>
public static class TransitKeyTypes
{
    /// <summary>Symmetric AEAD; the server default when <see cref="TransitKeyOptions.KeyType"/> is omitted.</summary>
    public const string ChaCha20Poly1305 = "chacha20-poly1305";

    /// <summary>Symmetric HMAC.</summary>
    public const string Hmac = "hmac";

    /// <summary>Signature.</summary>
    public const string Ed25519 = "ed25519";

    /// <summary>Post-quantum signature.</summary>
    public const string MlDsa44 = "ml-dsa-44";

    /// <summary>Post-quantum signature.</summary>
    public const string MlDsa65 = "ml-dsa-65";

    /// <summary>Post-quantum signature.</summary>
    public const string MlDsa87 = "ml-dsa-87";

    /// <summary>Post-quantum KEM; datakey plaintext/wrapped/unwrap.</summary>
    public const string MlKem768 = "ml-kem-768";
}

/// <summary>TRS-011's default and every hash algorithm 08 names for <see cref="TransitOperations.HmacAsync"/>, <see cref="TransitOperations.VerifyHmacAsync"/> and <see cref="TransitOperations.HashAsync"/>.</summary>
public static class TransitHashAlgorithms
{
    /// <summary>The default algorithm.</summary>
    public const string Sha2256 = "sha2-256";

    /// <summary></summary>
    public const string Sha2384 = "sha2-384";

    /// <summary></summary>
    public const string Sha2512 = "sha2-512";
}

/// <summary>The body <c>Transit.CreateKey</c> sends (08 §Operations).</summary>
public sealed class TransitKeyOptions
{
    /// <summary>One of <see cref="TransitKeyTypes"/>, or any other string the server accepts (TRS-001). Server-defaulted to <see cref="TransitKeyTypes.ChaCha20Poly1305"/> when omitted.</summary>
    public string? KeyType { get; init; }

    /// <summary>Whether the raw key material may ever be exported.</summary>
    public bool? Exportable { get; init; }

    /// <summary>Whether <c>Transit.DeleteKey</c> is allowed for this key without first flipping it here.</summary>
    public bool? DeletionAllowed { get; init; }

    /// <summary>
    /// Whether this key is derived per <c>Context</c>. TRS-013: <see cref="TransitOperations.EncryptAsync"/>
    /// accepts <c>context</c> only for a key created with this set.
    /// </summary>
    public bool? Derived { get; init; }

    /// <summary>
    /// TRS-013: convergent encryption (the same plaintext and context always produce the same
    /// ciphertext). Requires <see cref="Derived"/> to also be set; the server rejects the
    /// combination otherwise (08 §Server error strings, <c>BV-INPUT-100</c>).
    /// </summary>
    public bool? ConvergentEncryption { get; init; }
}

/// <summary>
/// The body <c>Transit.ConfigureKey</c> sends (08 §Operations). Every field is nullable and
/// <see langword="null"/> means "leave alone" — the same patch shape <see cref="KvV2ConfigPatch"/>
/// already established (D-M4-6) — so a caller can change one field of a key's configuration
/// without first reading the other two back.
/// </summary>
public sealed class TransitKeyConfig
{
    /// <summary>The new minimum decryptable version, or <see langword="null"/> to leave it unchanged.</summary>
    public int? MinDecryptionVersion { get; init; }

    /// <summary>The new minimum available (rewrap) version, or <see langword="null"/> to leave it unchanged.</summary>
    public int? MinAvailableVersion { get; init; }

    /// <summary>The new deletion-allowed flag, or <see langword="null"/> to leave it unchanged.</summary>
    public bool? DeletionAllowed { get; init; }
}

/// <summary>TRS-010: one key version's metadata, normalised from either wire shape (08 §Operations).</summary>
public sealed class TransitKeyVersionInfo
{
    /// <summary>When this version was created.</summary>
    public required DateTimeOffset CreationTime { get; init; }

    /// <summary>The public key material, present only for an asymmetric (signature or KEM) key type.</summary>
    public string? PublicKey { get; init; }
}

/// <summary>The metadata <c>Transit.ReadKey</c>, <c>Transit.CreateKey</c>, <c>Transit.RotateKey</c> and <c>Transit.ConfigureKey</c> return (08 §Operations).</summary>
public sealed class TransitKey
{
    /// <summary>The key's name.</summary>
    public required string Name { get; init; }

    /// <summary>One of <see cref="TransitKeyTypes"/>, or any other string the server reports (TRS-001).</summary>
    public required string Type { get; init; }

    /// <summary>The newest key version.</summary>
    public required int LatestVersion { get; init; }

    /// <summary>Versions older than this cannot decrypt (<c>BV-TRANSIT-004</c>).</summary>
    public required int MinDecryptionVersion { get; init; }

    /// <summary>Versions older than this cannot rewrap.</summary>
    public required int MinAvailableVersion { get; init; }

    /// <summary>Whether <c>Transit.DeleteKey</c> is currently allowed.</summary>
    public required bool DeletionAllowed { get; init; }

    /// <summary>Whether the raw key material may be exported.</summary>
    public required bool Exportable { get; init; }

    /// <summary>Whether this key is derived per <c>Context</c>.</summary>
    public required bool Derived { get; init; }

    /// <summary>Whether convergent encryption is enabled.</summary>
    public required bool ConvergentEncryption { get; init; }

    /// <summary>
    /// TRS-010: every retained version's metadata, normalised from the wire's
    /// <c>{version: creation_time}</c> (symmetric) or <c>{version: {public_key, creation_time}}</c>
    /// (asymmetric) shape into one map either way.
    /// </summary>
    public required IReadOnlyDictionary<int, TransitKeyVersionInfo> Keys { get; init; }
}

/// <summary><c>Transit.TrimKey</c>'s result: the key's refreshed metadata plus which versions were dropped (08 §Operations).</summary>
public sealed class TransitTrimResult
{
    /// <summary>The key's metadata after trimming.</summary>
    public required TransitKey Key { get; init; }

    /// <summary>The version numbers the trim removed.</summary>
    public required IReadOnlyList<int> DroppedVersions { get; init; }
}

/// <summary><c>Transit.Encrypt</c> and <c>Transit.Rewrap</c> share this result shape (08 §Operations).</summary>
public sealed class TransitEncryptResult
{
    /// <summary>The <c>bvault:</c>-framed ciphertext (see <see cref="TransitOperations.ParseCiphertext"/>).</summary>
    public required string Ciphertext { get; init; }

    /// <summary>The key version the operation used.</summary>
    public required int KeyVersion { get; init; }
}

/// <summary><c>Transit.Sign</c>'s result (08 §Operations).</summary>
public sealed class TransitSignResult
{
    /// <summary>The <c>bvault:</c>-framed signature.</summary>
    public required string Signature { get; init; }

    /// <summary>The key version the operation used.</summary>
    public required int KeyVersion { get; init; }
}

/// <summary><c>Transit.Hmac</c>'s result (08 §Operations).</summary>
public sealed class TransitHmacResult
{
    /// <summary>The <c>bvault:</c>-framed HMAC.</summary>
    public required string Hmac { get; init; }

    /// <summary>The key version the operation used.</summary>
    public required int KeyVersion { get; init; }
}

/// <summary><c>Transit.GenerateDataKey</c>'s mode (08 §Operations); the server default is <see cref="Wrapped"/>.</summary>
public enum TransitDataKeyMode
{
    /// <summary>Return only the wrapped (encrypted) datakey.</summary>
    Wrapped,

    /// <summary>Return the wrapped datakey and the raw plaintext (TRS-013: held in <see cref="SecretBytes"/>).</summary>
    Plaintext,
}

/// <summary><c>Transit.GenerateDataKey</c>'s result (08 §Operations).</summary>
public sealed class TransitDataKeyResult
{
    /// <summary>The wrapped (encrypted) datakey.</summary>
    public required string Ciphertext { get; init; }

    /// <summary>The key version the operation used.</summary>
    public required int KeyVersion { get; init; }

    /// <summary>
    /// TRS-013: the raw datakey, present only when <see cref="TransitDataKeyMode.Plaintext"/> was
    /// requested, held in a redacting type so it never reaches a log by accident.
    /// </summary>
    public SecretBytes? Plaintext { get; init; }
}

/// <summary>TRS-002's <c>Transit.ParseCiphertext</c> result: the parsed <c>bvault:</c> framing.</summary>
public sealed class TransitParsedCiphertext
{
    /// <summary>The framing version (the <c>v&lt;N&gt;</c> segment).</summary>
    public required int Version { get; init; }

    /// <summary>The post-quantum algorithm tag, present only on a <c>pqc</c>-framed value.</summary>
    public string? Algo { get; init; }

    /// <summary>The decoded payload bytes.</summary>
    public required ReadOnlyMemory<byte> Bytes { get; init; }
}
