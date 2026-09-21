using System.Diagnostics.CodeAnalysis;
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
        Acme = new PkiAcmeOperations(logical, context);
    }

    /// <summary>
    /// 09 §ACME: the <c>{mount}/acme/config</c> surface plus <see cref="PkiAcmeOperations.DirectoryUrl"/>
    /// (D-M9-3's <c>Transit.Byok</c> nested-operations idiom, <c>TransitOperations.cs:36</c>). The
    /// RFC 8555 protocol paths themselves (<c>acme/directory</c>, <c>new-nonce</c>, <c>new-account</c>,
    /// …) are for ACME clients, not this SDK, and are deliberately not wrapped beyond that one helper
    /// (09 §ACME, D-M9-14's negative check).
    /// </summary>
    public PkiAcmeOperations Acme { get; }

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

    // ---------------------------------------------------------------- CA lifecycle

    /// <summary><c>POST {mount}/root/generate/{internal|exported}</c>.</summary>
    public async Task<PkiRootCertificate> GenerateRootAsync(
        PkiKeyGenerationType type, PkiRootSpec spec, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(spec.CommonName);
        string path = $"{Encode(mount)}/root/generate/{PkiWire.KeyGenerationSegment(type)}";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, PkiWire.SerialiseRootSpec(spec), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadRootCertificate(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "certificate"), path);
    }

    /// <summary><c>POST {mount}/root/sign-intermediate</c>.</summary>
    public async Task<SignedIntermediateCertificate> SignIntermediateAsync(
        SignIntermediateRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Csr);
        string path = $"{Encode(mount)}/root/sign-intermediate";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, PkiWire.SerialiseSignIntermediate(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadSignedIntermediateCertificate(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "certificate"), path);
    }

    /// <summary><c>POST {mount}/intermediate/generate/{internal|exported}</c>. See <see cref="PkiIntermediateSpec"/> for the D-M9-17 transcription behind its request shape.</summary>
    public async Task<PkiIntermediateCsr> GenerateIntermediateAsync(
        PkiKeyGenerationType type, PkiIntermediateSpec spec, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(spec.CommonName);
        string path = $"{Encode(mount)}/intermediate/generate/{PkiWire.KeyGenerationSegment(type)}";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, PkiWire.SerialiseIntermediateSpec(spec), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadIntermediateCsr(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "csr"), path);
    }

    /// <summary><c>POST {mount}/intermediate/set-signed</c>.</summary>
    public async Task<SetSignedIntermediateResult> SetSignedIntermediateAsync(
        string certificate, string? issuerName = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(certificate);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/intermediate/set-signed";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("certificate", certificate);
            if (issuerName is not null)
            {
                writer.WriteString("issuer_name", issuerName);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadSetSignedIntermediateResult(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "issuer_id"), path);
    }

    /// <summary><c>POST {mount}/config/ca</c>. 09 names only the request fields, not a response shape, and no parameter here can cause key material to return (D-M9-16's boundary).</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ConfigureCaAsync(
        string pemBundle, string? issuerName = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pemBundle);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("pem_bundle", pemBundle);
            if (issuerName is not null)
            {
                writer.WriteString("issuer_name", issuerName);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/ca", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>GET {mount}/config/urls</c>.</summary>
    public async Task<PkiUrls?> ReadUrlsAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config/urls", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadUrls(data) : null;
    }

    /// <summary><c>POST {mount}/config/urls</c>.</summary>
    public async Task WriteUrlsAsync(
        PkiUrls urls, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/urls", PkiWire.SerialiseUrls(urls), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/config/crl</c>.</summary>
    public async Task<PkiCrlConfig?> ReadCrlConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config/crl", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadCrlConfig(data) : null;
    }

    /// <summary><c>POST {mount}/config/crl</c>.</summary>
    public async Task WriteCrlConfigAsync(
        PkiCrlConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/crl", PkiWire.SerialiseCrlConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/config/issuers</c>.</summary>
    public async Task<PkiIssuersConfig?> ReadIssuersConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config/issuers", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadIssuersConfig(data) : null;
    }

    /// <summary><c>POST {mount}/config/issuers</c>.</summary>
    public async Task WriteIssuersConfigAsync(
        PkiIssuersConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/issuers", PkiWire.SerialiseIssuersConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>LIST {mount}/issuers/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListIssuersAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/issuers/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary><c>GET {mount}/issuer/{ref}</c>. 09 names no response shape for an issuer object, and it carries no private key (line 52), so it is not a PKI-002 route.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadIssuerAsync(
        string issuerRef, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuerRef);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", IssuerPath(mount, issuerRef), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>POST {mount}/issuer/{ref}</c>.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> WriteIssuerAsync(
        string issuerRef, PkiIssuerWrite issuer, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuerRef);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "POST", IssuerPath(mount, issuerRef), PkiWire.SerialiseIssuerWrite(issuer), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>DELETE {mount}/issuer/{ref}</c>.</summary>
    public async Task DeleteIssuerAsync(
        string issuerRef, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuerRef);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", IssuerPath(mount, issuerRef), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/issuer/{ref}/chain</c>. 09 names no response shape.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> IssuerChainAsync(
        string issuerRef, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuerRef);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{IssuerPath(mount, issuerRef)}/chain", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>
    /// <c>{mount}/issuer/{ref}/export</c>. Appendix A marks this route <c>R,W</c> (both GET and
    /// POST), but this binds <b>POST only</b>, for the same reason D-M9-19 restricted
    /// <see cref="ExportCertificateAsync"/> to POST: <paramref name="password"/> is
    /// <see cref="SecretString"/>-typed and D-M9-20 requires secret material to travel in a request
    /// body, never a path segment or query string — a GET form here has nowhere but the query
    /// string to carry it. 09 §CA lifecycle states private keys are <b>never</b> exported here
    /// (line 52) — the opposite case from <see cref="ExportCertificateAsync"/> — so this is not a
    /// PKI-002 route and no parameter implying it can export a key is added here (D-M9-1).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ExportIssuerAsync(
        string issuerRef, string format = "pem", bool includeChain = true, SecretString? password = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(issuerRef);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("format", format);
            writer.WriteBoolean("include_chain", includeChain);
            if (password is { HasValue: true })
            {
                writer.WriteString("password", password.Reveal());
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{IssuerPath(mount, issuerRef)}/export", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>GET {mount}/ca[/pem]</c>. 09 names no response shape.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadCaAsync(
        bool pem = false, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = pem ? $"{Encode(mount)}/ca/pem" : $"{Encode(mount)}/ca";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>GET {mount}/ca_chain</c>. 09 names no response shape.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadCaChainAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/ca_chain", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    // ---------------------------------------------------------------- managed keys

    /// <summary><c>LIST {mount}/keys/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/keys/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>
    /// <c>POST {mount}/keys/generate/{internal|exported}</c>. D-M9-16's generalised rule: 09 §Managed
    /// keys defines no response shape, and the <see cref="PkiKeyGenerationType.Exported"/> path form
    /// establishes the response may carry key material (PKI-002), so the whole body is wrapped in a
    /// redacting type rather than surfaced through an untyped, unredacted map. See
    /// <see cref="PkiGeneratedKey"/>.
    /// </summary>
    public async Task<PkiGeneratedKey> GenerateKeyAsync(
        PkiKeyGenerationType type, string? keyType = null, int? keyBits = null, string? name = null, bool? exportable = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string basePath = $"{Encode(mount)}/keys/generate/{PkiWire.KeyGenerationSegment(type)}";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            if (keyType is not null)
            {
                writer.WriteString("key_type", keyType);
            }

            if (keyBits is { } bits)
            {
                writer.WriteNumber("key_bits", bits);
            }

            if (name is not null)
            {
                writer.WriteString("name", name);
            }

            if (exportable is { } flag)
            {
                writer.WriteBoolean("exportable", flag);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", basePath, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);

        // Same D-M1b-10 fold as ExportCertificateAsync: a 304/no-body response shapes to a Raw whose
        // ValueKind is Undefined, and GetRawText() would throw an uncoded exception (ERR-020/TRN-054)
        // rather than the coded envelope mismatch a genuinely absent response gets.
        if (response is null || response.Raw.ValueKind == JsonValueKind.Undefined)
        {
            throw KvWire.EnvelopeMismatch(basePath, "key");
        }

        return new PkiGeneratedKey { Payload = new SecretString(response.Raw.GetRawText()) };
    }

    /// <summary><c>POST {mount}/keys/import</c>. D-M9-20: the private key travels in the request body, never a path segment or query string.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ImportKeyAsync(
        SecretString privateKey, string? name = null, bool? exportable = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("private_key", privateKey.Reveal());
            if (name is not null)
            {
                writer.WriteString("name", name);
            }

            if (exportable is { } flag)
            {
                writer.WriteBoolean("exportable", flag);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/keys/import", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>GET {mount}/key/{ref}</c>. 09 names no response shape, and no parameter here establishes a private key could return.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadKeyAsync(
        string keyRef, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyRef);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", KeyRefPath(mount, keyRef), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>DELETE {mount}/key/{ref}</c>.</summary>
    public async Task DeleteKeyAsync(
        string keyRef, bool force = false, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyRef);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte>? body = force
            ? KvWire.Serialise(writer => writer.WriteBoolean("force", true))
            : null;
        _ = await logical.ExecuteShapedAsync(
            "DELETE", KeyRefPath(mount, keyRef), body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- tidy

    /// <summary><c>POST {mount}/tidy</c>.</summary>
    public async Task TidyAsync(
        PkiTidyOptions? tidyOptions = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = PkiWire.SerialiseTidyOptions(tidyOptions ?? new PkiTidyOptions());
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/tidy", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>GET {mount}/tidy-status</c>. 09 names no response shape.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> TidyStatusAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/tidy-status", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>GET {mount}/config/auto-tidy</c>.</summary>
    public async Task<PkiAutoTidyConfig?> ReadAutoTidyAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config/auto-tidy", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadAutoTidyConfig(data) : null;
    }

    /// <summary><c>POST {mount}/config/auto-tidy</c>.</summary>
    public async Task WriteAutoTidyAsync(
        PkiAutoTidyConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/auto-tidy", PkiWire.SerialiseAutoTidyConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string RolePath(string mount, string name)
    {
        return $"{Encode(mount)}/roles/{UrlBuilder.EncodePathSegment(name)}";
    }

    private static string CertPath(string mount, string serial)
    {
        return $"{Encode(mount)}/cert/{UrlBuilder.EncodePathSegment(serial)}";
    }

    private static string IssuerPath(string mount, string issuerRef)
    {
        return $"{Encode(mount)}/issuer/{UrlBuilder.EncodePathSegment(issuerRef)}";
    }

    private static string KeyRefPath(string mount, string keyRef)
    {
        return $"{Encode(mount)}/key/{UrlBuilder.EncodePathSegment(keyRef)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}

/// <summary>
/// 09 §ACME: the <c>{mount}/acme/config</c> read/write/delete surface, plus
/// <see cref="DirectoryUrl"/>, which is deliberately <b>not</b> a request — 09's own MUST NOT
/// (<c>09-pki-engine.md:115-117</c>) forbids wrapping the RFC 8555 protocol paths beyond this one
/// client-side URL builder.
/// </summary>
public sealed class PkiAcmeOperations
{
    private const string DefaultMount = "pki";

    private readonly LogicalOperations logical;
    private readonly ClientContext context;

    internal PkiAcmeOperations(LogicalOperations logical, ClientContext context)
    {
        this.logical = logical;
        this.context = context;
    }

    /// <summary><c>GET {mount}/acme/config</c>.</summary>
    public async Task<PkiAcmeConfig?> ReadConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/acme/config", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadAcmeConfig(data) : null;
    }

    /// <summary><c>POST {mount}/acme/config</c>.</summary>
    public async Task WriteConfigAsync(
        PkiAcmeConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/acme/config", PkiWire.SerialiseAcmeConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>DELETE {mount}/acme/config</c>.</summary>
    public async Task DeleteConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", $"{Encode(mount)}/acme/config", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// 09's client-side-only URL builder: <c>{endpoint}/{ApiPrefix}/{mount}/acme/directory</c>. Makes
    /// <b>no request</b> — the RFC 8555 protocol paths are for an ACME client, not this SDK
    /// (<c>09-pki-engine.md:115-117</c>) — and uses the client's configured
    /// <see cref="ClientConfig.ApiPrefix"/> rather than a per-call override, since this operation
    /// takes no <see cref="RequestOptions"/> (there being no request to apply them to).
    /// </summary>
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "09-pki-engine.md:117 pins this signature exactly as `Pki.Acme.DirectoryUrl(mount) -> string`; System.Uri has no counterpart in the Rust and Python SDKs, so typing this a Uri here would make the .NET signature the odd one out for no behavioural gain (CLA-003). The rationale mirrors OidcOperations.cs's CA1054 (URI-like parameters) suppression for the same cross-language reason, not that call site itself, whose AuthUrlAsync returns Task<string> and so never trips CA1055.")]
    public string DirectoryUrl(string mount = DefaultMount)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return $"{context.Endpoint.TrimEnd('/')}/{context.Config.ApiPrefix}/{Encode(mount)}/acme/directory";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}
