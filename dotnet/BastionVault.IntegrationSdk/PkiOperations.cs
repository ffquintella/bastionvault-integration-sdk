using System.Globalization;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The PKI engine surface (09 — PKI engine), reached from <see cref="BastionVaultClient.Pki"/>.
/// <c>mount</c> defaults to <c>"pki"</c> everywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>This SDK performs no cryptography and parses no certificate (00 §Purpose, §Non-goals,
/// OVR-002).</b> Every PEM field this class returns is the server's bytes, unmodified (PKI-001);
/// no <c>Parse*</c> helper ships in this slice (D-M9-1).
/// </para>
/// <para>
/// This slice covers roles, issuance, certificates and the CRL (09 §Roles and issuance,
/// §Certificates and CRL). CA lifecycle, managed keys, tidy, ACME and the two queues are later
/// slices (D-M9-5) and are not exposed here.
/// </para>
/// </remarks>
public sealed class PkiOperations
{
    private const string DefaultMount = "pki";

    private readonly LogicalOperations logical;

    internal PkiOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    // ---------------------------------------------------------------- roles and issuance

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

    /// <summary><c>POST {mount}/roles/{name}</c>.</summary>
    public async Task WriteRoleAsync(
        string name, PkiRole role, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(role);
        _ = await logical.ExecuteShapedAsync(
            "POST", RolePath(mount, name), PkiWire.SerialiseRole(role), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/roles/{name}</c>.</summary>
    public async Task<PkiRole?> ReadRoleAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", RolePath(mount, name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadRole(data) : null;
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

    /// <summary>
    /// <c>POST {mount}/issue/{role}</c>. PKI-011: <see cref="IssueRequest.CommonName"/> empty
    /// raises <c>BV-INPUT-001</c> before any request is sent.
    /// </summary>
    public async Task<IssuedCertificate> IssueAsync(
        string role, IssueRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(request);
        string path = $"{Encode(mount)}/issue/{UrlBuilder.EncodePathSegment(role)}";
        PkiWire.RequireCommonName(request.CommonName, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("common_name", request.CommonName);
            PkiWire.WriteCsv(writer, "alt_names", request.AltNames);
            PkiWire.WriteCsv(writer, "ip_sans", request.IpSans);
            PkiWire.WriteSeconds(writer, "ttl", request.Ttl);
            if (request.IssuerRef is { } issuerRef)
            {
                writer.WriteString("issuer_ref", issuerRef);
            }

            if (request.KeyRef is { } keyRef)
            {
                writer.WriteString("key_ref", keyRef);
            }

            PkiWire.WriteCsv(writer, "upn_sans", request.UpnSans);
            PkiWire.WriteCsv(writer, "email_sans", request.EmailSans);
            if (request.AdSid is { } adSid)
            {
                writer.WriteString("ad_sid", adSid);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadIssuedCertificate(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "certificate"), path);
    }

    /// <summary><c>POST {mount}/sign/{role}</c>.</summary>
    public async Task<SignedCertificate> SignAsync(
        string role, SignRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Csr);
        string path = $"{Encode(mount)}/sign/{UrlBuilder.EncodePathSegment(role)}";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("csr", request.Csr);
            if (request.CommonName is { } commonName)
            {
                writer.WriteString("common_name", commonName);
            }

            PkiWire.WriteCsv(writer, "alt_names", request.AltNames);
            PkiWire.WriteCsv(writer, "ip_sans", request.IpSans);
            PkiWire.WriteSeconds(writer, "ttl", request.Ttl);
            if (request.IssuerRef is { } issuerRef)
            {
                writer.WriteString("issuer_ref", issuerRef);
            }

            if (request.KeyRef is { } keyRef)
            {
                writer.WriteString("key_ref", keyRef);
            }

            PkiWire.WriteCsv(writer, "upn_sans", request.UpnSans);
            PkiWire.WriteCsv(writer, "email_sans", request.EmailSans);
            if (request.AdSid is { } adSid)
            {
                writer.WriteString("ad_sid", adSid);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadSignedCertificate(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "certificate"), path);
    }

    /// <summary><c>POST {mount}/sign-verbatim</c>.</summary>
    public async Task<SignedCertificate> SignVerbatimAsync(
        string csr, TimeSpan? ttl = null, string? issuerRef = null, string mount = DefaultMount,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(csr);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/sign-verbatim";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("csr", csr);
            PkiWire.WriteSeconds(writer, "ttl", ttl);
            if (issuerRef is not null)
            {
                writer.WriteString("issuer_ref", issuerRef);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadSignedCertificate(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "certificate"), path);
    }

    // ---------------------------------------------------------------- certificates and CRL

    /// <summary><c>LIST {mount}/certs/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListCertificatesAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/certs/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>
    /// 14 §Bulk metadata listings: <c>GET {mount}/certs-info?after=&amp;limit=</c>, the
    /// cursor-paginated bulk listing.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately unpinned.</b> Appendix A's Prefix column means "<c>v1</c> follows
    /// <c>ApiPrefix</c>, only an explicit <c>v2</c> pins" (<c>appendix-a-endpoint-catalogue.md:3-5</c>),
    /// the PKI table carries no Prefix column for <c>certs-info</c> at all, and R-27/D-M8-45/D-M8-5
    /// already ruled that the owning section and Appendix A win over section 14's own table, which
    /// writes <c>/v2/</c> uniformly across all seven <c>*-info</c> rows as a formatting artefact
    /// never reconciled with Appendix A. Do not "fix" this back to a pin — see
    /// <see cref="UserpassOperations.ListUsersInfoAsync"/>'s pin for the *different* case where
    /// Appendix A does name <c>v2</c> for that specific route (line 87, "v2 recommended").
    /// </remarks>
    public async Task<Page<CertificateSummary>> ListCertificatesInfoAsync(
        string mount = DefaultMount, string? after = null, int? limit = null,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        int effectiveLimit = PagingWire.ValidateLimit(limit);
        string query = after is null
            ? $"limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={UrlBuilder.EncodeQueryValue(after)}&limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}";
        string path = $"{Encode(mount)}/certs-info?{query}";

        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "keys");

        IReadOnlyList<string> keys = SysWire.ReadKeys(data);
        List<CertificateSummary> records = [];
        if (data.TryGetValue("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement record in recordsElement.EnumerateArray())
            {
                string fallback = index < keys.Count ? keys[index] : string.Empty;
                records.Add(record.ValueKind == JsonValueKind.Object
                    ? PkiWire.ReadCertificateSummary(SysWire.AsMap(record), fallback, path)
                    : throw KvWire.EnvelopeMismatch(path, "records[]"));
                index++;
            }
        }

        if (records.Count != keys.Count)
        {
            throw KvWire.EnvelopeMismatch(path, "records");
        }

        string? next = SysWire.ReadString(data, "next");
        return new Page<CertificateSummary>
        {
            Keys = keys,
            Records = records,
            Total = SysWire.ReadNullableLong(data, "total") is { } total ? (int)total : keys.Count,
            Next = string.IsNullOrEmpty(next) ? null : next,
            Truncated = data.TryGetValue("truncated", out JsonElement truncated) && truncated.ValueKind == JsonValueKind.True,
        };
    }

    /// <summary>
    /// D-M9-8: PAG-004's iterator for this area. Walks every page in cursor order via
    /// <see cref="PagingWire.IteratePagesAsync{T}"/>, following
    /// <see cref="SysOperations.ListNamespacesInfoAllAsync"/> and
    /// <see cref="UserpassOperations.ListUsersInfoAllAsync"/>'s exact shape: each page fetch is
    /// ordinary rate-gated traffic, and <c>BV-INPUT-005</c> is raised at
    /// <paramref name="maxRecords"/> (default 5000) rather than paging without bound.
    /// </summary>
    public IAsyncEnumerable<KeyValuePair<string, CertificateSummary>> ListCertificatesInfoAllAsync(
        string mount = DefaultMount,
        int? limit = null,
        int maxRecords = PagingWire.DefaultMaxRecords,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return PagingWire.IteratePagesAsync(
            (after, token) => ListCertificatesInfoAsync(mount, after, limit, options, token),
            maxRecords,
            cancellationToken);
    }

    /// <summary><c>GET {mount}/cert/{serial}</c>. PKI-020: <paramref name="serial"/> is sent exactly as given.</summary>
    public async Task<CertificateRecord?> ReadCertificateAsync(
        string serial, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = CertPath(mount, serial);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadCertificateRecord(data, path) : null;
    }

    /// <summary><c>DELETE {mount}/cert/{serial}</c>. PKI-020: <paramref name="serial"/> is sent exactly as given.</summary>
    public async Task DeleteCertificateAsync(
        string serial, bool force = false, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte>? body = force
            ? KvWire.Serialise(writer => writer.WriteBoolean("force", true))
            : null;
        _ = await logical.ExecuteShapedAsync(
            "DELETE", CertPath(mount, serial), body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/cert/{serial}/key</c>.</summary>
    public async Task AttachKeyAsync(
        string serial, string keyRef, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        ArgumentException.ThrowIfNullOrEmpty(keyRef);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("key_ref", keyRef));
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{CertPath(mount, serial)}/key", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE {mount}/cert/{serial}/key</c>.</summary>
    public async Task DetachKeyAsync(
        string serial, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"{CertPath(mount, serial)}/key", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>POST {mount}/cert/{serial}/export</c>. D-M9-16 (M9 slice a handback): 09 defines no
    /// response shape for this route, and its own <c>includePrivateKey</c>/<c>mode</c> parameters
    /// establish that the response may carry key material (PKI-002) — unlike
    /// <see cref="TransitByokOperations"/>'s unshaped routes, which carry none — so the whole body
    /// is wrapped verbatim (PKI-001) in <see cref="PkiCertificateExport"/> rather than surfaced
    /// through an untyped, unredacted map.
    /// </summary>
    /// <remarks>
    /// D-M9-19 (M9 slice a handback, second round): 09 and Appendix A specify this route as
    /// <c>GET/POST</c>, and an earlier revision of this binding added a <c>useGet</c> form that
    /// carried <paramref name="password"/> in the query string — a worse leak than the one D-M9-16
    /// closed, because <c>Internal/ErrorPaths.cs</c> redacts path <i>segments</i> only and never
    /// inspects a query string, so the secret reached the request observer, the exception's
    /// <c>Path</c>/<c>Details["path"]</c>, and the hint enrichment unredacted. **Binds POST only.**
    /// D-M9-20: secret material never travels in a URL path segment or query string, only in a
    /// request body — the GET form is not offered here, and is not to be re-added without first
    /// closing <c>ErrorPaths.Redact</c>'s query-string gap (R-32).
    /// </remarks>
    public async Task<PkiCertificateExport> ExportCertificateAsync(
        string serial, string format, bool includePrivateKey, string mode = "normal", SecretString? password = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ArgumentException.ThrowIfNullOrEmpty(mode);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string basePath = $"{CertPath(mount, serial)}/export";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("format", format);
            writer.WriteBoolean("include_private_key", includePrivateKey);
            writer.WriteString("mode", mode);
            if (password is { HasValue: true })
            {
                writer.WriteString("password", password.Reveal());
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", basePath, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);

        // A 304 (no body) shapes to a Response whose Raw is default(JsonElement) — Undefined —
        // rather than null (D-M1b-10); GetRawText() throws InvalidOperationException on that, an
        // uncoded exception ERR-020/TRN-054 forbid a caller from having to catch, so it is folded
        // into the same envelope-mismatch a genuinely absent response gets.
        if (response is null || response.Raw.ValueKind == JsonValueKind.Undefined)
        {
            throw KvWire.EnvelopeMismatch(basePath, "export");
        }

        return new PkiCertificateExport { Payload = new SecretString(response.Raw.GetRawText()) };
    }

    /// <summary>
    /// <c>POST {mount}/certs/import</c>. 09 names only the request fields, not a response shape.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ImportCertificateAsync(
        string certificate, string? source = null, string mount = DefaultMount,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(certificate);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("certificate", certificate);
            if (source is not null)
            {
                writer.WriteString("source", source);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/certs/import", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>POST {mount}/revoke</c>. PKI-020: <paramref name="serialNumber"/> is sent exactly as given.</summary>
    public async Task RevokeAsync(
        string serialNumber, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serialNumber);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("serial_number", serialNumber));
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/revoke", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/crl[/pem]</c>.</summary>
    public async Task<Crl> ReadCrlAsync(
        bool pem = false, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = pem ? $"{Encode(mount)}/crl/pem" : $"{Encode(mount)}/crl";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadCrl(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "crl"), path);
    }

    /// <summary><c>POST {mount}/crl/rotate</c>.</summary>
    public async Task RotateCrlAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/crl/rotate", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/issuer/{ref}/crl[/pem]</c>.</summary>
    public async Task<Crl> ReadIssuerCrlAsync(
        string issuerRef, bool pem = false, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuerRef);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string root = $"{Encode(mount)}/issuer/{UrlBuilder.EncodePathSegment(issuerRef)}/crl";
        string path = pem ? $"{root}/pem" : root;
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadCrl(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "crl"), path);
    }

    private static string RolePath(string mount, string name)
    {
        return $"{Encode(mount)}/roles/{UrlBuilder.EncodePathSegment(name)}";
    }

    private static string CertPath(string mount, string serial)
    {
        return $"{Encode(mount)}/cert/{UrlBuilder.EncodePathSegment(serial)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}
