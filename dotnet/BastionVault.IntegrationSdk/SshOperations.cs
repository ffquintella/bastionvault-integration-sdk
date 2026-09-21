using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The SSH engine surface (10 — SSH engine and SSH broker), reached from
/// <see cref="BastionVaultClient.Ssh"/>. <c>mount</c> defaults to <c>"ssh"</c> everywhere. This SDK
/// performs no cryptography (00 §Purpose, §Non-goals, OVR-002, D-M9-1): every OpenSSH key and
/// certificate line this class returns is the server's bytes, unmodified.
/// </summary>
public sealed class SshOperations
{
    private const string DefaultMount = "ssh";

    private readonly LogicalOperations logical;

    internal SshOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    // ---------------------------------------------------------------- CA configuration

    /// <summary><c>POST {mount}/config/ca</c>.</summary>
    public async Task<SshCaKey> ConfigureCaAsync(
        bool generateSigningKey = true, SecretString? privateKey = null, string? algorithm = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/config/ca";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteBoolean("generate_signing_key", generateSigningKey);
            if (privateKey is { HasValue: true })
            {
                writer.WriteString("private_key", privateKey.Reveal());
            }

            if (algorithm is not null)
            {
                writer.WriteString("algorithm", algorithm);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SshWire.ReadCaKey(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "public_key"), path);
    }

    /// <summary><c>GET {mount}/config/ca</c>.</summary>
    public async Task<SshCaKey?> ReadCaAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/config/ca";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? SshWire.ReadCaKey(data, path) : null;
    }

    /// <summary><c>DELETE {mount}/config/ca</c>. 204.</summary>
    public async Task DeleteCaAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"{Encode(mount)}/config/ca", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/public_key</c>. Returns the authorized-keys-form bytes verbatim (D-M9-1).</summary>
    /// <remarks>
    /// A mount path is Shape A (<c>03-transport-and-protocol.md:114</c>) — an object envelope
    /// carrying <c>data</c> — and <c>TRN-040</c> makes shape detection safe either way, so this
    /// reads <c>data.public_key</c> through the same envelope every other route in this class
    /// uses, rather than treating the response as a raw, unwrapped body. The wire field name
    /// <c>"public_key"</c> itself is unpinned by any document or fixture.
    /// </remarks>
    public async Task<string> PublicKeyAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/public_key";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "public_key");
        return KvWire.ReadString(data, "public_key") ?? throw KvWire.EnvelopeMismatch(path, "public_key");
    }

    // ---------------------------------------------------------------- roles

    /// <summary><c>LIST {mount}/roles/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListRolesAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/roles/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>
    /// 14 §Bulk metadata listings: <c>GET {mount}/roles-info?after=&amp;limit=</c>, the
    /// cursor-paginated bulk listing.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately unpinned</b> (D-M9-7): <c>appendix-a-endpoint-catalogue.md:221</c> marks this
    /// route <c>v1</c>, and the legend at <c>:3-5</c> defines <c>v1</c> as "uses <c>ApiPrefix</c>,
    /// only an explicit <c>v2</c> pins" — so this passes <paramref name="options"/> through
    /// unmodified rather than pinning a literal <c>v2/</c>. Do not "fix" this back to a pin; see
    /// <see cref="UserpassOperations.ListUsersInfoAsync"/> for the different case where Appendix A
    /// does name <c>v2</c> for its route.
    /// </remarks>
    public async Task<Page<SshRole>> ListRolesInfoAsync(
        string mount = DefaultMount, string? after = null, int? limit = null,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        int effectiveLimit = PagingWire.ValidateLimit(limit);
        string query = after is null
            ? $"limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={UrlBuilder.EncodeQueryValue(after)}&limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}";
        string path = $"{Encode(mount)}/roles-info?{query}";

        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "keys");
        return SshWire.ReadRolePage(data, path);
    }

    /// <summary>
    /// D-M9-8: PAG-004's iterator for this area, following
    /// <see cref="PkiOperations.ListCertificatesInfoAllAsync"/>'s exact shape.
    /// </summary>
    public IAsyncEnumerable<KeyValuePair<string, SshRole>> ListRolesInfoAllAsync(
        string mount = DefaultMount,
        int? limit = null,
        int maxRecords = PagingWire.DefaultMaxRecords,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return PagingWire.IteratePagesAsync(
            (after, token) => ListRolesInfoAsync(mount, after, limit, options, token),
            maxRecords,
            cancellationToken);
    }

    /// <summary><c>POST {mount}/roles/{name}</c>.</summary>
    public async Task WriteRoleAsync(
        string name, SshRole role, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(role);
        _ = await logical.ExecuteShapedAsync(
            "POST", RolePath(mount, name), SshWire.SerialiseRole(role), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/roles/{name}</c>.</summary>
    public async Task<SshRole?> ReadRoleAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", RolePath(mount, name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? SshWire.ReadRole(data) : null;
    }

    /// <summary><c>DELETE {mount}/roles/{name}</c>.</summary>
    public async Task DeleteRoleAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", RolePath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- signing (CA mode)

    /// <summary>
    /// <c>POST {mount}/sign/{role}</c>. SSH-001: <paramref name="request"/>'s
    /// <see cref="SshSignRequest.PublicKey"/> empty or whitespace raises <c>BV-INPUT-001</c> before
    /// any request is sent.
    /// </summary>
    public async Task<SignedSshCertificate> SignAsync(
        string role, SshSignRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(request);
        string path = $"{Encode(mount)}/sign/{UrlBuilder.EncodePathSegment(role)}";
        SshWire.RequirePublicKey(request.PublicKey, path);
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, SshWire.SerialiseSignRequest(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SshWire.ReadSignedSshCertificate(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "signed_key"), path);
    }

    /// <summary>
    /// SSH-002: writes <paramref name="signedKey"/> verbatim to <c>{path}-cert.pub</c> with
    /// <c>0644</c>, applied at file creation (D-M9-12). Not a network operation: makes no request.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "10-ssh-engine.md:42-43 pins this signature as an instance member reached through Client.Ssh (`Ssh.WriteCertificateFile(signedKey, path)`), the same calling convention as every other member of this class; a static method here would be the odd one out for no behavioural gain (CLA-003).")]
    public void WriteCertificateFile(string signedKey, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(signedKey);
        ArgumentException.ThrowIfNullOrEmpty(path);
        SshFiles.WriteCertificate($"{path}-cert.pub", signedKey);
    }

    // ---------------------------------------------------------------- one-time passwords (OTP mode)

    /// <summary>
    /// <c>POST {mount}/creds/{role}</c>. SSH-003: <paramref name="ip"/> is validated as an IP
    /// literal client-side, raising <c>BV-INPUT-001</c> before any request is sent.
    /// </summary>
    public async Task<SshCredentials> CredsAsync(
        string role, string ip, string? username = null, TimeSpan? ttl = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(ip);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/creds/{UrlBuilder.EncodePathSegment(role)}";
        SshWire.RequireIp(ip, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("ip", ip);
            if (username is not null)
            {
                writer.WriteString("username", username);
            }

            PkiWire.WriteSeconds(writer, "ttl", ttl);
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SshWire.ReadCredentials(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "key"), path);
    }

    /// <summary>
    /// <c>POST {mount}/verify</c>. D-M9-20: <paramref name="otp"/> is secret material and travels
    /// in the request body, never a path segment or query string. An invalid or expired otp is a
    /// server-recognised failure (<c>BV-SSH-004</c>), not a null result; a genuinely absent
    /// response shapes to <see langword="null"/>, the same convention as
    /// <see cref="ReadRoleAsync"/>.
    /// </summary>
    public async Task<SshOtpVerification?> VerifyAsync(
        SecretString otp, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(otp);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/verify";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("otp", otp.Reveal()));
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? SshWire.ReadOtpVerification(data, path) : null;
    }

    /// <summary>
    /// <c>POST {mount}/lookup</c>. SSH-003 applies to this route's own <paramref name="ip"/> field
    /// exactly as it does to <see cref="CredsAsync"/>'s: validated as an IP literal client-side,
    /// raising <c>BV-INPUT-001</c> before any request is sent.
    /// </summary>
    public async Task<IReadOnlyList<string>> LookupAsync(
        string ip, string? username = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ip);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/lookup";
        SshWire.RequireIp(ip, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("ip", ip);
            if (username is not null)
            {
                writer.WriteString("username", username);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? KvWire.ReadStringList(data, "roles") : [];
    }

    private static string RolePath(string mount, string name)
    {
        return $"{Encode(mount)}/roles/{UrlBuilder.EncodePathSegment(name)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}
