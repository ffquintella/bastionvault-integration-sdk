using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 11 — TOTP engine, reached from <see cref="BastionVaultClient.Totp"/>, with <c>mount</c>
/// defaulting to <c>"totp"</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The SDK generates and validates no TOTP codes.</b> A key is either
/// <b>generate-mode</b> (the vault owns the seed and produces codes,
/// <see cref="GenerateCodeAsync"/>) or <b>provider-mode</b> (the seed came from
/// <see cref="TotpKeySpec.Key"/>/<see cref="TotpKeySpec.Url"/> and the vault validates codes,
/// <see cref="ValidateCodeAsync"/>). Every operation here is request building, response parsing,
/// TOT-001's client-side validation and error mapping — the server owns the seed and the HOTP/TOTP
/// algorithm itself (`specifications/00-overview.md` Purpose and Non-goals, OVR-002).
/// </para>
/// <para>
/// Server-error recognition for this engine (<c>BV-TOTP-001</c>, <c>BV-TOTP-002</c>,
/// <c>BV-INPUT-001</c>) is generated from Appendix B §2 and needs no operation-local remap here.
/// </para>
/// </remarks>
public sealed class TotpOperations
{
    private const string DefaultMount = "totp";

    private readonly LogicalOperations logical;

    internal TotpOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>11: <c>LIST {mount}/keys/</c>. An empty list when there are none (TRN-050).</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Totp.ListKeys — TRN-050</spec>
    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", TotpWire.KeysRoot(mount), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>
    /// 11: <c>POST {mount}/keys/{name}</c>. TOT-001's client-side validation runs before any
    /// request is sent.
    /// </summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries <c>generate</c>, <c>key</c>, <c>url</c>, <c>key_size</c>, <c>issuer</c>, <c>account_name</c>, <c>algorithm</c>, <c>digits</c>, <c>period</c>, <c>skew</c>, <c>qr_size</c>, <c>exported</c>, <c>replay_check</c> per <see cref="TotpKeySpec"/>. Returns <see cref="TotpKeyCreated"/>, never <see langword="null"/>. Conformance: Standard (TOT-001). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> (raised server-side too, though TOT-001 validates client-side first).</remarks>
    /// <spec>Totp.CreateKey — TOT-001</spec>
    public async Task<TotpKeyCreated> CreateKeyAsync(
        string name,
        TotpKeySpec spec,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = TotpWire.KeyPath(mount, name);
        TotpWire.ValidateSpec(spec, path);

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, TotpWire.Serialise(spec), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TotpWire.ReadCreated(response?.Data ?? throw TotpWire.EnvelopeMismatch(path, "name"), path);
    }

    /// <summary>11: <c>GET {mount}/keys/{name}</c>. The seed is never returned.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; no body. A missing key is <see langword="null"/> (TRN-050), never an exception. Conformance: Standard (TRN-050). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Totp.ReadKey — TRN-050</spec>
    public async Task<TotpKey?> ReadKeyAsync(
        string name,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", TotpWire.KeyPath(mount, name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? TotpWire.ReadKey(data) : null;
    }

    /// <summary>11: <c>DELETE {mount}/keys/{name}</c> → <c>204</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; no body. Returns <see langword="void"/> on the server's <c>204</c>. Conformance: Standard (TRN-001 — every typed operation is built on <c>Logical.Delete</c>; 11 states no delete-specific behaviour beyond the route). Errors beyond the common set (ERR-061): <c>BV-TOTP-001 KeyNotFound</c>.</remarks>
    /// <spec>Totp.DeleteKey — TRN-001</spec>
    public async Task DeleteKeyAsync(
        string name,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", TotpWire.KeyPath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// 11: <c>GET {mount}/code/{name}</c> — generate-mode only. Returns the wire's <c>code</c>
    /// string verbatim (TOT-004): a leading zero is significant and the value is never parsed as a
    /// number. A provider-mode key answers <c>BV-TOTP-002 WrongModeForOperation</c>, generated from
    /// Appendix B §2 with no remap here.
    /// </summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; no body. Never returns <see langword="null"/>; a missing key raises rather than yielding an empty code. Conformance: Standard (TOT-004). Errors beyond the common set (ERR-061): <c>BV-TOTP-001 KeyNotFound</c>, <c>BV-TOTP-002 WrongModeForOperation</c>.</remarks>
    /// <spec>Totp.GenerateCode — TOT-004</spec>
    public async Task<string> GenerateCodeAsync(
        string name,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = TotpWire.CodePath(mount, name);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TotpWire.ReadCode(response?.Data ?? throw TotpWire.EnvelopeMismatch(path, "code"), path);
    }

    /// <summary>
    /// 11: <c>POST {mount}/code/{name}</c> with <c>{"code": "&lt;code&gt;"}</c> — provider-mode
    /// only. <paramref name="code"/> is sent as a string (TOT-004), preserving a leading zero.
    /// </summary>
    /// <remarks>
    /// <b>TOT-003: a wrong code and a replayed code (when <c>replay_check</c> is on) both answer
    /// <c>{valid: false}</c>, not an error, and the two are indistinguishable at the API level.</b>
    /// The server gives the SDK no way to tell them apart, and this method does not invent one.
    /// A generate-mode key answers <c>BV-TOTP-002 WrongModeForOperation</c>, generated from
    /// Appendix B §2 with no remap here.
    /// </remarks>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries <c>code</c> as a string. Returns <see langword="bool"/>, never <see langword="null"/>. Conformance: Standard (TOT-003). Errors beyond the common set (ERR-061): <c>BV-TOTP-001 KeyNotFound</c>, <c>BV-TOTP-002 WrongModeForOperation</c>, <c>BV-INPUT-001</c> (<c>code</c> required).</remarks>
    /// <spec>Totp.ValidateCode — TOT-003</spec>
    public async Task<bool> ValidateCodeAsync(
        string name,
        string code,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrEmpty(code);
        string path = TotpWire.CodePath(mount, name);
        ReadOnlyMemory<byte> body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>(StringComparer.Ordinal) { ["code"] = code });
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return TotpWire.ReadValid(response?.Data ?? throw TotpWire.EnvelopeMismatch(path, "valid"), path);
    }
}
