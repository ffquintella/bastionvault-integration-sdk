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

    /// <summary>Configures (or generates) the SSH CA's signing key: <c>POST {mount}/config/ca</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; body carries
    /// <paramref name="generateSigningKey"/> (default <see langword="true"/>),
    /// <paramref name="privateKey"/> (optional, imported when set), <paramref name="algorithm"/>
    /// (optional; <c>ed25519</c> or <c>mldsa65</c>, PQC feature-gated). Returns
    /// <see cref="SshCaKey"/>, never <see langword="null"/>. Conformance: Complete. No error codes
    /// beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.ConfigureCa — 10-ssh-engine.md</spec>
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

    /// <summary>Reads the SSH CA's public key and algorithm: <c>GET {mount}/config/ca</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; no body. Returns
    /// <see cref="SshCaKey"/>, or <see langword="null"/> when the CA is not yet configured.
    /// Conformance: Complete. No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.ReadCa — 10-ssh-engine.md</spec>
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

    /// <summary>Deletes the SSH CA's configuration: <c>DELETE {mount}/config/ca</c>. 204.</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; no body. Returns
    /// <see langword="void"/> on the server's <c>204</c>. Conformance: Complete. No error codes
    /// beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.DeleteCa — 10-ssh-engine.md</spec>
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
    /// <c>"public_key"</c> itself is unpinned by any document or fixture. Wire params:
    /// <paramref name="mount"/> builds the route; no body. Returns the public key string, never
    /// <see langword="null"/>. Conformance: Complete (TRN-040). No error codes beyond the common
    /// set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.PublicKey — TRN-040</spec>
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

    /// <summary>Lists the role names under <paramref name="mount"/>: <c>LIST {mount}/roles/</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; no query or body params. Returns an
    /// empty list when the backend has none, never <see langword="null"/>. Conformance: Complete.
    /// No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.ListRoles — 10-ssh-engine.md</spec>
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
    /// <see cref="UserpassAdminOperations.ListUsersInfoAsync"/> for the different case where Appendix A
    /// does name <c>v2</c> for its route. Wire params: <paramref name="mount"/> builds the route;
    /// query carries <paramref name="after"/> (cursor, PAG-002) and <paramref name="limit"/>
    /// (defaulted to 100 and validated to <c>1-500</c>, PAG-001). Returns <see cref="Page{T}"/> of
    /// <see cref="SshRole"/>, never <see langword="null"/>. Conformance: Complete (PAG-001).
    /// Errors beyond the common set (ERR-061): <c>BV-INPUT-004</c> (limit out of range, PAG-001);
    /// <c>BV-PROTOCOL-002</c> (records/keys length mismatch, PAG-005).
    /// </remarks>
    /// <spec>Ssh.ListRolesInfo — PAG-001</spec>
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
    /// <remarks>
    /// HTTP call: none directly — walks <see cref="ListRolesInfoAsync"/> pages via
    /// <see cref="PagingWire.IteratePagesAsync{T}"/>. Wire params: as <see cref="ListRolesInfoAsync"/>,
    /// plus <paramref name="maxRecords"/> (client-side cap, no wire effect). Returns each record
    /// keyed by its name, never <see langword="null"/>. Conformance: Complete (PAG-004). Errors
    /// beyond the common set (ERR-061): <c>BV-INPUT-005</c> when the walk would exceed
    /// <paramref name="maxRecords"/>.
    /// </remarks>
    /// <spec>Ssh.ListRolesInfoAll — PAG-004</spec>
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

    /// <summary>Creates or replaces an SSH role: <c>POST {mount}/roles/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; body carries
    /// <paramref name="role"/>'s fields (<c>key_type</c>, <c>algorithm_signer</c>, <c>cert_type</c>,
    /// <c>allowed_users</c>, <c>default_user</c>, <c>allowed_extensions</c>,
    /// <c>default_extensions</c>, <c>allowed_critical_options</c>, <c>default_critical_options</c>,
    /// <c>ttl</c>, <c>max_ttl</c>, <c>not_before_duration</c>, <c>key_id_format</c>,
    /// <c>cidr_list</c>, <c>exclude_cidr_list</c>, <c>port</c>, <c>pqc_only</c>). Returns
    /// <see langword="void"/> on success. Conformance: Complete. No error codes beyond the common
    /// set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.WriteRole — 10-ssh-engine.md</spec>
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

    /// <summary>Reads an SSH role's configuration: <c>GET {mount}/roles/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no body. A
    /// missing role is <see langword="null"/>, never an exception. Conformance: Complete. No
    /// error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.ReadRole — 10-ssh-engine.md</spec>
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

    /// <summary>Deletes an SSH role: <c>DELETE {mount}/roles/{name}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="name"/>/<paramref name="mount"/> build the route; no body.
    /// Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond
    /// the common set (ERR-061).
    /// </remarks>
    /// <spec>Ssh.DeleteRole — 10-ssh-engine.md</spec>
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
    /// Signs a public key as a CA-mode SSH certificate: <c>POST {mount}/sign/{role}</c>. SSH-001:
    /// <paramref name="request"/>'s <see cref="SshSignRequest.PublicKey"/> empty or whitespace
    /// raises <c>BV-INPUT-001</c> before any request is sent.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="role"/>/<paramref name="mount"/> build the route; body carries
    /// <paramref name="request"/>'s <c>PublicKey</c> (required), <c>ValidPrincipals</c> (CSV on the
    /// wire), <c>Ttl</c>, <c>CertType</c>, <c>KeyId</c>, <c>Extensions</c>, <c>CriticalOptions</c>.
    /// Returns <see cref="SignedSshCertificate"/>, never <see langword="null"/>. Conformance:
    /// Complete (SSH-001). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> (empty
    /// public key, client- or server-side), <c>BV-SSH-001 CaNotConfigured</c>,
    /// <c>BV-SSH-002 RoleNotFound</c>, <c>BV-SSH-005 WrongRoleMode</c>,
    /// <c>BV-SSH-006 PqcOnlyClassicalCa</c>, <c>BV-AUTHZ-004 PrincipalNotAllowed</c>,
    /// <c>BV-SERVER-005</c> (CA key load or cert sign failure).
    /// </remarks>
    /// <spec>Ssh.Sign — SSH-001</spec>
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
    /// <remarks>
    /// HTTP call: none — a client-side file writer. Wire params: none; <paramref name="signedKey"/>
    /// and <paramref name="path"/> are used locally. Returns <see langword="void"/> on success, or
    /// throws on a file-system failure (not a <see cref="BastionVaultException"/>). Conformance:
    /// Complete (SSH-002). No error codes beyond the common set (ERR-061); this member raises no
    /// <see cref="BastionVaultException"/> at all, since it makes no request.
    /// </remarks>
    /// <spec>Ssh.WriteCertificateFile — SSH-002</spec>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "10-ssh-engine.md:42-43 pins this signature as an instance member reached through Client.Ssh (`Ssh.WriteCertificateFile(signedKey, path)`), the same calling convention as every other member of this class; a static method here would be the odd one out for no behavioural gain (CLA-003).")]
    public void WriteCertificateFile(string signedKey, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(signedKey);
        ArgumentException.ThrowIfNullOrEmpty(path);
        SshFiles.WriteCertificate($"{path}-cert.pub", signedKey);
    }

    // ---------------------------------------------------------------- one-time passwords (OTP mode)

    /// <summary>
    /// Issues a one-time-password SSH credential: <c>POST {mount}/creds/{role}</c>. SSH-003:
    /// <paramref name="ip"/> is validated as an IP literal client-side, raising
    /// <c>BV-INPUT-001</c> before any request is sent.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="role"/>/<paramref name="mount"/> build the route; body carries
    /// <paramref name="ip"/> (required), <paramref name="username"/> (optional),
    /// <paramref name="ttl"/> (optional, seconds on the wire). Returns <see cref="SshCredentials"/>,
    /// never <see langword="null"/>; <c>Key</c> is the OTP (redacting). Conformance: Complete
    /// (SSH-003). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> (invalid IP,
    /// client- or server-side), <c>BV-SSH-002 RoleNotFound</c>, <c>BV-SSH-003 IpNotAllowed</c>,
    /// <c>BV-SSH-005 WrongRoleMode</c>.
    /// </remarks>
    /// <spec>Ssh.Creds — SSH-003</spec>
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
    /// Verifies (and consumes) a one-time password: <c>POST {mount}/verify</c>. D-M9-20:
    /// <paramref name="otp"/> is secret material and travels in the request body, never a path
    /// segment or query string. An invalid or expired otp is a server-recognised failure
    /// (<c>BV-SSH-004</c>), not a null result; a genuinely absent response shapes to
    /// <see langword="null"/>, the same convention as <see cref="ReadRoleAsync"/>.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="otp"/>
    /// (required, revealed only on the wire). Returns <see cref="SshOtpVerification"/>, or
    /// <see langword="null"/> for a genuinely absent response. Conformance: Complete. Errors
    /// beyond the common set (ERR-061): <c>BV-SSH-004 InvalidOtp</c> for an invalid or expired
    /// otp.
    /// </remarks>
    /// <spec>Ssh.Verify — 10-ssh-engine.md</spec>
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
    /// Looks up the roles an IP/username pair is permitted to request OTP credentials for:
    /// <c>POST {mount}/lookup</c>. SSH-003 applies to this route's own <paramref name="ip"/> field
    /// exactly as it does to <see cref="CredsAsync"/>'s: validated as an IP literal client-side,
    /// raising <c>BV-INPUT-001</c> before any request is sent.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="ip"/>
    /// (required), <paramref name="username"/> (optional). Returns the matching role names, an
    /// empty list when there are none, never <see langword="null"/>. Conformance: Complete
    /// (SSH-003). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> (invalid IP,
    /// client- or server-side).
    /// </remarks>
    /// <spec>Ssh.Lookup — SSH-003</spec>
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
