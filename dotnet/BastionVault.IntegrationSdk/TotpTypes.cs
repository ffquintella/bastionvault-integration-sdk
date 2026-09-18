namespace BastionVault.IntegrationSdk;

/// <summary>
/// 11's closed algorithm set. A C# enum rather than a free string (unlike
/// <see cref="TotpKey.Algorithm"/>, D-M4-5): this is the <b>request</b> side, TOT-001 requires the
/// SDK to validate <c>algorithm</c> against exactly this set before any request is sent, and the
/// set is closed by the specification rather than server-extensible.
/// </summary>
public enum TotpAlgorithm
{
    /// <summary><c>SHA1</c>.</summary>
    Sha1,

    /// <summary><c>SHA256</c>.</summary>
    Sha256,

    /// <summary><c>SHA512</c>.</summary>
    Sha512,
}

/// <summary>
/// 11's <c>Totp.CreateKey</c> request body. A key is either <b>generate-mode</b>
/// (<see cref="Generate"/> <see langword="true"/>, the vault owns the seed) or
/// <b>provider-mode</b> (<see cref="Key"/> or <see cref="Url"/> carries the seed and the vault
/// only validates codes). TOT-001 requires exactly one of the three to be set; every other
/// member is optional and, left <see langword="null"/>, is omitted from the request so the
/// server's own default applies (OVR-007).
/// </summary>
public sealed class TotpKeySpec
{
    /// <summary>Generate-mode: the vault generates and owns the seed. Default <see langword="false"/>.</summary>
    public bool Generate { get; init; }

    /// <summary>
    /// Provider-mode: the base32 seed the caller supplies. Held as a <see cref="SecretString"/>
    /// (TOT-002) — it is seed material, not a token, but it is exactly as sensitive as one.
    /// </summary>
    public SecretString? Key { get; init; }

    /// <summary>
    /// Provider-mode: an <c>otpauth://</c> URL carrying the seed (and, optionally, a label that
    /// satisfies TOT-001's <see cref="AccountName"/> requirement in its place). Held as a
    /// <see cref="SecretString"/> (TOT-002).
    /// </summary>
    public SecretString? Url { get; init; }

    /// <summary>Generate-mode only: the seed size in bytes. Server default 20 when omitted.</summary>
    public int? KeySize { get; init; }

    /// <summary>The issuer shown in an authenticator app.</summary>
    public string? Issuer { get; init; }

    /// <summary>
    /// Required unless <see cref="Url"/> carries a label (TOT-001).
    /// </summary>
    public string? AccountName { get; init; }

    /// <summary>TOT-001: one of <see cref="TotpAlgorithm.Sha1"/>, <see cref="TotpAlgorithm.Sha256"/>, <see cref="TotpAlgorithm.Sha512"/>. Server default <c>SHA1</c> when omitted.</summary>
    public TotpAlgorithm? Algorithm { get; init; }

    /// <summary>TOT-001: 6 or 8 when set. Server default 6 when omitted.</summary>
    public int? Digits { get; init; }

    /// <summary>TOT-001: at least 1 second when set. Server default 30 when omitted.</summary>
    public int? Period { get; init; }

    /// <summary>Generate-mode: the number of periods of clock skew tolerated. Server default 1 when omitted.</summary>
    public int? Skew { get; init; }

    /// <summary>
    /// Generate-mode: the requested QR barcode size in pixels; <c>0</c> disables it. Server
    /// default 200 when omitted.
    /// </summary>
    public int? QrSize { get; init; }

    /// <summary>
    /// Generate-mode: whether the response carries <see cref="TotpKeyCreated.Key"/>,
    /// <see cref="TotpKeyCreated.Url"/> and <see cref="TotpKeyCreated.Barcode"/>. Server default
    /// <see langword="true"/> when omitted.
    /// </summary>
    public bool? Exported { get; init; }

    /// <summary>Provider-mode: whether a validated code is rejected on replay. Server default <see langword="true"/> when omitted.</summary>
    public bool? ReplayCheck { get; init; }
}

/// <summary>
/// 11's <c>Totp.CreateKey</c> response: <c>{name, generate}</c> always;
/// <see cref="Key"/>/<see cref="Url"/>/<see cref="Barcode"/> only when the request was
/// <c>generate &amp;&amp; exported</c>, and absent otherwise.
/// </summary>
public sealed class TotpKeyCreated
{
    /// <summary>The key name it was created under.</summary>
    public required string Name { get; init; }

    /// <summary>Whether this key is generate-mode.</summary>
    public required bool Generate { get; init; }

    /// <summary>The generated seed, present only for an exported generate-mode key (TOT-002).</summary>
    public SecretString? Key { get; init; }

    /// <summary>The generated <c>otpauth://</c> URL, present only for an exported generate-mode key (TOT-002).</summary>
    public SecretString? Url { get; init; }

    /// <summary>The QR barcode PNG bytes, decoded from the wire's base64 (TOT-002); present only for an exported generate-mode key.</summary>
    public ReadOnlyMemory<byte>? Barcode { get; init; }
}

/// <summary>
/// 11's <c>Totp.ReadKey</c> response. The seed is never returned by the server, so no member here
/// can carry one.
/// </summary>
public sealed class TotpKey
{
    /// <summary>Whether this key is generate-mode.</summary>
    public required bool Generate { get; init; }

    /// <summary>The configured issuer, or <see langword="null"/> when none was set.</summary>
    public string? Issuer { get; init; }

    /// <summary>The configured account name, or <see langword="null"/> when none was set.</summary>
    public string? AccountName { get; init; }

    /// <summary>
    /// The wire's algorithm string, verbatim. A plain <see langword="string"/> rather than
    /// <see cref="TotpAlgorithm"/> (D-M4-5): this is the response side, and a server value outside
    /// the three the client validates on write is a fact to report, not a guess to make (D-M1c-25).
    /// </summary>
    public string? Algorithm { get; init; }

    /// <summary>The configured code length.</summary>
    public int? Digits { get; init; }

    /// <summary>The configured period, in seconds.</summary>
    public int? Period { get; init; }

    /// <summary>The configured clock-skew tolerance, in periods.</summary>
    public int? Skew { get; init; }

    /// <summary>Whether a validated code is rejected on replay.</summary>
    public bool ReplayCheck { get; init; }
}
