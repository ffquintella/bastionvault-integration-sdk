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
/// Slice a covers roles, issuance, certificates and the CRL (09 §Roles and issuance,
/// §Certificates and CRL); slice b adds CA lifecycle, managed keys, tidy and ACME; slice c adds the
/// outbound CSR queue (<see cref="Csr"/>) and the inbound sign-request approval queue
/// (<see cref="SignRequests"/>) (D-M9-5).
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
        Csr = new PkiCsrOperations(logical);
        SignRequests = new PkiSignRequestOperations(logical);
    }

    /// <summary>
    /// 09 §ACME: the <c>{mount}/acme/config</c> surface plus <see cref="PkiAcmeOperations.DirectoryUrl"/>
    /// (D-M9-3's <c>Transit.Byok</c> nested-operations idiom, <c>TransitOperations.cs:36</c>). The
    /// RFC 8555 protocol paths themselves (<c>acme/directory</c>, <c>new-nonce</c>, <c>new-account</c>,
    /// …) are for ACME clients, not this SDK, and are deliberately not wrapped beyond that one helper
    /// (09 §ACME, D-M9-14's negative check).
    /// </summary>
    public PkiAcmeOperations Acme { get; }

    /// <summary>
    /// 09 §Outbound CSR queue (external signing): the <c>{mount}/csr/*</c> surface (D-M9-3's nested-
    /// operations idiom).
    /// </summary>
    public PkiCsrOperations Csr { get; }

    /// <summary>
    /// 09 §Inbound sign-request queue (approval workflow): the <c>{mount}/sign-request/*</c> surface
    /// (D-M9-3's nested-operations idiom).
    /// </summary>
    public PkiSignRequestOperations SignRequests { get; }

    // ---------------------------------------------------------------- roles and issuance

    /// <summary>Lists the role names under <paramref name="mount"/>: <c>LIST {mount}/roles/</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no query or body params. Returns an empty list when the backend has none, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ListRoles — 09-pki-engine.md</spec>
    public async Task<IReadOnlyList<string>> ListRolesAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/roles/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>Creates or replaces a role: <c>POST {mount}/roles/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; body carries <see cref="PkiRole"/>'s fields (PKI-010: CSV-typed fields such as <c>alt_names</c>, <c>allowed_domains</c>, <c>key_usage</c> are comma-joined). Returns no value. Conformance: Complete (PKI-010). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.WriteRole — PKI-010</spec>
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

    /// <summary>Reads a role: <c>GET {mount}/roles/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; no body. Returns the <see cref="PkiRole"/>, or <see langword="null"/> when the role does not exist (PKI-010: CSV-typed fields are split back into lists on read). Conformance: Complete (PKI-010). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadRole — PKI-010</spec>
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

    /// <summary>Deletes a role: <c>DELETE {mount}/roles/{name}</c>.</summary>
    /// <remarks>Wire params: <c>name</c>/<c>mount</c> build the route; no body. Returns no value; deleting an absent role is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.DeleteRole — 09-pki-engine.md</spec>
    public async Task DeleteRoleAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", RolePath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Issues a leaf certificate: <c>POST {mount}/issue/{role}</c>.</summary>
    /// <remarks>Wire params: <c>role</c>/<c>mount</c> build the route; body carries <c>common_name</c> (required), <c>alt_names</c>, <c>ip_sans</c>, <c>ttl</c>, <c>issuer_ref</c>, <c>key_ref</c>, <c>upn_sans</c>, <c>email_sans</c>, <c>ad_sid</c> (PKI-010 CSV joining). Returns the <see cref="IssuedCertificate"/>, never <see langword="null"/>; key material redacted (PKI-002), PEM verbatim (PKI-001). Conformance: Complete (PKI-011). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for an empty <see cref="IssueRequest.CommonName"/> (client-side).</remarks>
    /// <spec>Pki.Issue — PKI-011</spec>
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

    /// <summary>Signs a caller-supplied CSR against a role: <c>POST {mount}/sign/{role}</c>.</summary>
    /// <remarks>Wire params: <c>role</c>/<c>mount</c> build the route; body carries <c>csr</c> (required), <c>common_name</c>, <c>alt_names</c>, <c>ip_sans</c>, <c>ttl</c>, <c>issuer_ref</c>, <c>key_ref</c>, <c>upn_sans</c>, <c>email_sans</c>, <c>ad_sid</c> (PKI-010 CSV joining). Returns the <see cref="SignedCertificate"/>, never <see langword="null"/>; it carries no private key field (the caller already holds the key). Conformance: Complete (PKI-010). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.Sign — PKI-010</spec>
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

    /// <summary>Signs a CSR verbatim, without a role's constraints: <c>POST {mount}/sign-verbatim</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <c>csr</c> (required), <c>ttl</c>, <c>issuer_ref</c>. Returns the <see cref="SignedCertificate"/>, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.SignVerbatim — 09-pki-engine.md</spec>
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

    /// <summary>Lists certificate serials under <paramref name="mount"/>: <c>LIST {mount}/certs/</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no query or body params. Returns an empty list when the backend has none, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ListCertificates — 09-pki-engine.md</spec>
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
    /// D-M9-31: pinned to <c>/v2</c>. Appendix A's PKI table carries no Prefix column at all for
    /// <c>certs-info</c>, so the legend at <c>appendix-a-endpoint-catalogue.md:3-5</c> — which
    /// defines what the column means when present — says nothing about this route; the catalogue
    /// is silent, not <c>v1</c>. With the catalogue silent, <c>14-batch-and-request-efficiency.md:98</c>
    /// governs, and the <c>efficiency.pagination.zip-mismatch-protocol-error</c> fixture — captured
    /// from the server's own behaviour at M8 — agrees: <c>/v2</c>. This reverses D-M9-7, which
    /// mistook the catalogue's silence for an implicit <c>v1</c>. Wire params: <c>mount</c> builds the route; query carries <c>after</c> (previous page's <see cref="Page{T}.Next"/>, verbatim) and <c>limit</c> (PAG-001: 1–500, default 100). Returns a <see cref="Page{T}"/> of <see cref="CertificateSummary"/>, never <see langword="null"/>. Conformance: Complete. Errors beyond the common set (ERR-061): <c>BV-INPUT-004</c> for <c>limit</c> outside <c>1…500</c>.
    /// </remarks>
    /// <spec>Pki.ListCertificatesInfo — 09-pki-engine.md</spec>
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
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "keys");

        IReadOnlyList<string> keys = SysWire.ReadKeys(data);

        // D-M9-30/PAG-005: the structural keys/records length check runs before any per-record
        // decode, not after (R-19's shape — the fourth instance this milestone of a fixture going
        // green on a guard other than the one it names). A short or long records array is reported
        // as the length mismatch it is; a per-record field problem is a separate, later concern.
        // `response` is known non-null here, so its StatusCode is in hand for the one guard the
        // `efficiency.pagination.zip-mismatch-protocol-error` fixture (PAG-005) actually exercises.
        bool hasRecordsArray = data.TryGetValue("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array;
        int recordCount = hasRecordsArray ? recordsElement.GetArrayLength() : 0;
        if (recordCount != keys.Count)
        {
            throw KvWire.EnvelopeMismatch(path, "records", response.StatusCode);
        }

        List<CertificateSummary> records = [];
        if (hasRecordsArray)
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
    /// <see cref="UserpassAdminOperations.ListUsersInfoAllAsync"/>'s exact shape: each page fetch is
    /// ordinary rate-gated traffic, and <c>BV-INPUT-005</c> is raised at
    /// <paramref name="maxRecords"/> (default 5000) rather than paging without bound.
    /// </summary>
    /// <remarks>HTTP call: none directly — delegates each page to <see cref="ListCertificatesInfoAsync"/>. Returns an async stream of serial/<see cref="CertificateSummary"/> pairs, never <see langword="null"/>. Conformance: Complete (PAG-004). Errors beyond the common set (ERR-061): <c>BV-INPUT-004</c> (via the delegated page fetch), <c>BV-INPUT-005</c> when <paramref name="maxRecords"/> is exceeded.</remarks>
    /// <spec>Pki.ListCertificatesInfoAll — PAG-004</spec>
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

    /// <summary>Reads a certificate record: <c>GET {mount}/cert/{serial}</c>. PKI-020: <paramref name="serial"/> is sent exactly as given.</summary>
    /// <remarks>Wire params: <c>serial</c>/<c>mount</c> build the route; no body. Returns the <see cref="CertificateRecord"/>, or <see langword="null"/> when the serial does not exist; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-020). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadCertificate — PKI-020</spec>
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

    /// <summary>Deletes a certificate record: <c>DELETE {mount}/cert/{serial}</c>. PKI-020: <paramref name="serial"/> is sent exactly as given.</summary>
    /// <remarks>Wire params: <c>serial</c>/<c>mount</c> build the route; body carries <c>force</c> when <see langword="true"/>, omitted otherwise. Returns no value; deleting an absent serial is not an error. Conformance: Complete (PKI-020). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.DeleteCertificate — PKI-020</spec>
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

    /// <summary>Attaches a managed key to a certificate: <c>POST {mount}/cert/{serial}/key</c>.</summary>
    /// <remarks>Wire params: <c>serial</c>/<c>mount</c> build the route; body carries <c>key_ref</c> (required). Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.AttachKey — 09-pki-engine.md</spec>
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

    /// <summary>Detaches the managed key from a certificate: <c>DELETE {mount}/cert/{serial}/key</c>.</summary>
    /// <remarks>Wire params: <c>serial</c>/<c>mount</c> build the route; no body. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.DetachKey — 09-pki-engine.md</spec>
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
    /// closing <c>ErrorPaths.Redact</c>'s query-string gap (R-32). Wire params: <c>serial</c>/<c>mount</c> build the route; body carries <c>format</c>, <c>include_private_key</c>, <c>mode</c>, <c>password</c> (omitted when unset). Returns the <see cref="PkiCertificateExport"/>, never <see langword="null"/>; the payload is the raw response body, redacted (PKI-002). Conformance: Complete (PKI-001, PKI-002). No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>Pki.ExportCertificate — PKI-002</spec>
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

    /// <summary>Imports an externally-issued certificate: <c>POST {mount}/certs/import</c>. 09 names only the request fields, not a response shape.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <c>certificate</c> (required), <c>source</c> (omitted when unset). Returns the raw response map, or <see langword="null"/> on an empty body; no typed shape exists to decode into (09 §Certificates and CRL). Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ImportCertificate — 09-pki-engine.md</spec>
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

    /// <summary>Revokes a certificate: <c>POST {mount}/revoke</c>. PKI-020: <paramref name="serialNumber"/> is sent exactly as given.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <c>serial_number</c> (required). Returns no value. Conformance: Complete (PKI-020). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.Revoke — PKI-020</spec>
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

    /// <summary>Reads the mount's CRL: <c>GET {mount}/crl[/pem]</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; <paramref name="pem"/> selects the <c>/pem</c> segment; no body. Returns the <see cref="Crl"/>, never <see langword="null"/>; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadCrl — 09-pki-engine.md</spec>
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

    /// <summary>Forces a CRL rotation: <c>POST {mount}/crl/rotate</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.RotateCrl — 09-pki-engine.md</spec>
    public async Task RotateCrlAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/crl/rotate", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a specific issuer's CRL: <c>GET {mount}/issuer/{ref}/crl[/pem]</c>.</summary>
    /// <remarks>Wire params: <c>issuerRef</c>/<c>mount</c> build the route; <paramref name="pem"/> selects the <c>/pem</c> segment; no body. Returns the <see cref="Crl"/>, never <see langword="null"/>; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadIssuerCrl — 09-pki-engine.md</spec>
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

    /// <summary>Generates a root CA: <c>POST {mount}/root/generate/{internal|exported}</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> and <paramref name="type"/> (<c>internal</c> or <c>exported</c>) build the route; body carries <c>common_name</c> (required), <c>organization</c>, <c>key_type</c>, <c>key_bits</c>, <c>ttl</c>, <c>issuer_name</c>, <c>key_ref</c>. Returns the <see cref="PkiRootCertificate"/>, never <see langword="null"/>; <see cref="PkiRootCertificate.PrivateKey"/> is populated only for <see cref="PkiKeyGenerationType.Exported"/> and is redacted (PKI-002); PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001, PKI-002). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.GenerateRoot — PKI-002</spec>
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

    /// <summary>Signs an intermediate CSR against the root: <c>POST {mount}/root/sign-intermediate</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <c>csr</c> (required), <c>common_name</c>, <c>organization</c>, <c>ttl</c>, <c>max_path_length</c> (default -1), <c>issuer_ref</c>. Returns the <see cref="SignedIntermediateCertificate"/>, never <see langword="null"/>; PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.SignIntermediate — 09-pki-engine.md</spec>
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

    /// <summary>Generates an intermediate CA CSR: <c>POST {mount}/intermediate/generate/{internal|exported}</c>. See <see cref="PkiIntermediateSpec"/> for the D-M9-17 transcription behind its request shape.</summary>
    /// <remarks>Wire params: <c>mount</c> and <paramref name="type"/> (<c>internal</c> or <c>exported</c>) build the route; body carries <c>common_name</c> (required) plus <see cref="PkiIntermediateSpec"/>'s remaining fields. Returns the <see cref="PkiIntermediateCsr"/>, never <see langword="null"/>; <see cref="PkiIntermediateCsr.PrivateKey"/> is populated only for <see cref="PkiKeyGenerationType.Exported"/> and is redacted (PKI-002); PEM returned verbatim (PKI-001). Conformance: Complete (PKI-001, PKI-002). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.GenerateIntermediate — PKI-002</spec>
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

    /// <summary>Imports a signed intermediate certificate as a new issuer: <c>POST {mount}/intermediate/set-signed</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <c>certificate</c> (required), <c>issuer_name</c> (omitted when unset). Returns the <see cref="SetSignedIntermediateResult"/>, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.SetSignedIntermediate — 09-pki-engine.md</spec>
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

    /// <summary>Configures the CA from an externally-supplied PEM bundle: <c>POST {mount}/config/ca</c>. 09 names only the request fields, not a response shape, and no parameter here can cause key material to return (D-M9-16's boundary).</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <c>pem_bundle</c> (required), <c>issuer_name</c> (omitted when unset). Returns the raw response map, or <see langword="null"/> on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ConfigureCa — 09-pki-engine.md</spec>
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

    /// <summary>Reads the issuing/CRL/OCSP URLs config: <c>GET {mount}/config/urls</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns the <see cref="PkiUrls"/>, or <see langword="null"/> when unset. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadUrls — 09-pki-engine.md</spec>
    public async Task<PkiUrls?> ReadUrlsAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config/urls", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadUrls(data) : null;
    }

    /// <summary>Writes the issuing/CRL/OCSP URLs config: <c>POST {mount}/config/urls</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <see cref="PkiUrls"/>'s <c>issuing_certificates</c>, <c>crl_distribution_points</c>, <c>ocsp_servers</c> arrays. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.WriteUrls — 09-pki-engine.md</spec>
    public async Task WriteUrlsAsync(
        PkiUrls urls, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/urls", PkiWire.SerialiseUrls(urls), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads the CRL config: <c>GET {mount}/config/crl</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns the <see cref="PkiCrlConfig"/>, or <see langword="null"/> when unset. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadCrlConfig — 09-pki-engine.md</spec>
    public async Task<PkiCrlConfig?> ReadCrlConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config/crl", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadCrlConfig(data) : null;
    }

    /// <summary>Writes the CRL config: <c>POST {mount}/config/crl</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <see cref="PkiCrlConfig"/>'s <c>expiry</c> (default <c>72h</c>) and <c>disable</c>. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.WriteCrlConfig — 09-pki-engine.md</spec>
    public async Task WriteCrlConfigAsync(
        PkiCrlConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/crl", PkiWire.SerialiseCrlConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads the default-issuer config: <c>GET {mount}/config/issuers</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no body. Returns the <see cref="PkiIssuersConfig"/>, or <see langword="null"/> when unset. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadIssuersConfig — 09-pki-engine.md</spec>
    public async Task<PkiIssuersConfig?> ReadIssuersConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{Encode(mount)}/config/issuers", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? PkiWire.ReadIssuersConfig(data) : null;
    }

    /// <summary>Writes the default-issuer config: <c>POST {mount}/config/issuers</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; body carries <see cref="PkiIssuersConfig"/>'s <c>default</c> field. Returns no value. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.WriteIssuersConfig — 09-pki-engine.md</spec>
    public async Task WriteIssuersConfigAsync(
        PkiIssuersConfig config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/config/issuers", PkiWire.SerialiseIssuersConfig(config), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Lists issuer refs under <paramref name="mount"/>: <c>LIST {mount}/issuers/</c>.</summary>
    /// <remarks>Wire params: <c>mount</c> builds the route; no query or body params. Returns an empty list when the backend has none, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ListIssuers — 09-pki-engine.md</spec>
    public async Task<IReadOnlyList<string>> ListIssuersAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/issuers/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>Reads an issuer's metadata: <c>GET {mount}/issuer/{ref}</c>. 09 names no response shape for an issuer object, and it carries no private key (line 52), so it is not a PKI-002 route.</summary>
    /// <remarks>Wire params: <c>issuerRef</c>/<c>mount</c> build the route; no body. Returns the raw response map, or <see langword="null"/> when the issuer does not exist. Conformance: Complete (PKI-002). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.ReadIssuer — PKI-002</spec>
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

    /// <summary>Updates an issuer's name and usage: <c>POST {mount}/issuer/{ref}</c>.</summary>
    /// <remarks>Wire params: <c>issuerRef</c>/<c>mount</c> build the route; body carries <see cref="PkiIssuerWrite"/>'s <c>issuer_name</c> (omitted when unset) and <c>usage</c> as a JSON array. Returns the raw response map, or <see langword="null"/> on an empty body. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.WriteIssuer — 09-pki-engine.md</spec>
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

    /// <summary>Deletes an issuer: <c>DELETE {mount}/issuer/{ref}</c>.</summary>
    /// <remarks>Wire params: <c>issuerRef</c>/<c>mount</c> build the route; no body. Returns no value; deleting an absent issuer is not an error. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Pki.DeleteIssuer — 09-pki-engine.md</spec>
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

/// <summary>
/// 09 §Outbound CSR queue (external signing): the <c>{mount}/csr/*</c> surface, reached from
/// <see cref="PkiOperations.Csr"/>.
/// </summary>
public sealed class PkiCsrOperations
{
    private const string DefaultMount = "pki";

    private readonly LogicalOperations logical;

    internal PkiCsrOperations(LogicalOperations logical)
    {
        this.logical = logical;
    }

    /// <summary>
    /// <c>POST {mount}/csr/generate</c>. 09-pki-engine.md:90 names this operation's fields as an
    /// object literal (<c>{role, common_name, alt_names, ip_sans, email_sans, key_ref, exported,
    /// exportable}</c>), the same notation <see cref="PkiOperations.GenerateKeyAsync"/> and
    /// <see cref="PkiOperations.ImportKeyAsync"/> bind as separate optional parameters rather than a
    /// request type, so this follows the same shape. None of the fields carries a "(required)"
    /// marker on this row (unlike <see cref="PkiOperations.IssueAsync"/>'s <c>common_name</c>), so no
    /// client-side requiredness check is added beyond what 09 states.
    /// </summary>
    /// <remarks>
    /// D-M9-10: 09 defines no response shape for this route, but its own <paramref name="exported"/>
    /// parameter establishes the response may carry key material (PKI-002), so the result is the
    /// typed, redacting <see cref="PkiGeneratedCsr"/> — transcribed whole-set (D-M9-17) from the
    /// sibling shape <see cref="PkiOperations.GenerateIntermediateAsync"/> defines at
    /// <c>09-pki-engine.md:44</c> — rather than an untyped map (D-M9-16).
    /// </remarks>
    public async Task<PkiGeneratedCsr> GenerateAsync(
        string? role = null, string? commonName = null, IReadOnlyList<string>? altNames = null, IReadOnlyList<string>? ipSans = null,
        IReadOnlyList<string>? emailSans = null, string? keyRef = null, bool? exported = null, bool? exportable = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{Encode(mount)}/csr/generate";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            if (role is not null)
            {
                writer.WriteString("role", role);
            }

            if (commonName is not null)
            {
                writer.WriteString("common_name", commonName);
            }

            PkiWire.WriteCsv(writer, "alt_names", altNames);
            PkiWire.WriteCsv(writer, "ip_sans", ipSans);
            PkiWire.WriteCsv(writer, "email_sans", emailSans);
            if (keyRef is not null)
            {
                writer.WriteString("key_ref", keyRef);
            }

            if (exported is { } exportedFlag)
            {
                writer.WriteBoolean("exported", exportedFlag);
            }

            if (exportable is { } exportableFlag)
            {
                writer.WriteBoolean("exportable", exportableFlag);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return PkiWire.ReadGeneratedCsr(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "csr"), path);
    }

    /// <summary><c>LIST {mount}/csr/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/csr/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>14 §Bulk metadata listings: <c>GET {mount}/csr-info?after=&amp;limit=</c>.</summary>
    /// <remarks>
    /// D-M9-31: pinned to <c>/v2</c>, the same reasoning as
    /// <see cref="PkiOperations.ListCertificatesInfoAsync"/>: the PKI table carries no Prefix
    /// column for <c>csr-info</c>, so Appendix A is silent rather than implying <c>v1</c>, and
    /// <c>14-batch-and-request-efficiency.md:98</c> governs. This reverses D-M9-7. D-M9-10/D-M9-21
    /// still hold: 09 defines no response shape for this listing's records, so each record
    /// surfaces as the raw wire map (<see cref="PkiWire.ReadRawInfoPage"/>) rather than a guessed
    /// type. See R-31.
    /// </remarks>
    public async Task<Page<IReadOnlyDictionary<string, JsonElement>>> ListInfoAsync(
        string mount = DefaultMount, string? after = null, int? limit = null,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        int effectiveLimit = PagingWire.ValidateLimit(limit);
        string query = after is null
            ? $"limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={UrlBuilder.EncodeQueryValue(after)}&limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}";
        string path = $"{Encode(mount)}/csr-info?{query}";

        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "keys");
        return PkiWire.ReadRawInfoPage(data, path);
    }

    /// <summary>
    /// D-M9-8: PAG-004's iterator for <see cref="ListInfoAsync"/>, following
    /// <see cref="PkiOperations.ListCertificatesInfoAllAsync"/>'s exact shape.
    /// </summary>
    public IAsyncEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, JsonElement>>> ListInfoAllAsync(
        string mount = DefaultMount,
        int? limit = null,
        int maxRecords = PagingWire.DefaultMaxRecords,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return PagingWire.IteratePagesAsync(
            (after, token) => ListInfoAsync(mount, after, limit, options, token),
            maxRecords,
            cancellationToken);
    }

    /// <summary>
    /// <c>GET {mount}/csr/{id}</c>. 09 names no response shape, and no parameter here can cause key
    /// material to return, so the untyped map fallback applies (D-M9-21). See R-31.
    /// </summary>
    /// <remarks>
    /// <b>Known soft edge of D-M9-16 (DR-0016 open question 5).</b> A CSR generated with
    /// <c>exportable: true</c> (<see cref="PkiCsrOperations.GenerateAsync"/>'s <c>exportable</c>
    /// parameter) may itself carry key material when later read back, the same unresolved case as
    /// <see cref="PkiOperations.ReadKeyAsync"/>. D-M9-16's rule keys on <i>this call's own</i> request
    /// parameters, which is the only signal 09 gives it — a route that can return a secret without a
    /// parameter of its own announcing it is invisible to the rule. Not a defect in this slice; booked
    /// for M10.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", ItemPath(mount, id), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>DELETE {mount}/csr/{id}</c>.</summary>
    public async Task DeleteAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", ItemPath(mount, id), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/csr/{id}/set-signed</c>. 09 names no response shape, and no parameter here can cause key material to return, so the untyped map fallback applies (D-M9-21). See R-31.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> SetSignedAsync(
        string id, string certificate, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(certificate);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("certificate", certificate));
        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{ItemPath(mount, id)}/set-signed", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    private static string ItemPath(string mount, string id)
    {
        return $"{Encode(mount)}/csr/{UrlBuilder.EncodePathSegment(id)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}

/// <summary>
/// 09 §Inbound sign-request queue (approval workflow): the <c>{mount}/sign-request/*</c> surface,
/// reached from <see cref="PkiOperations.SignRequests"/>.
/// </summary>
public sealed class PkiSignRequestOperations
{
    private const string DefaultMount = "pki";

    private readonly LogicalOperations logical;

    internal PkiSignRequestOperations(LogicalOperations logical)
    {
        this.logical = logical;
    }

    /// <summary>
    /// <c>POST {mount}/sign-request/import</c>. 09-pki-engine.md:99 names this operation's fields as
    /// an object literal (<c>{csr, requester, notes, suggested_role, allow_duplicate}</c>), bound as
    /// separate optional parameters following <see cref="PkiOperations.ImportKeyAsync"/>'s idiom for
    /// the same notation. 09 names only the request fields, not a response shape, and no parameter
    /// here can cause key material to return, so the untyped map fallback applies (D-M9-21). See
    /// R-31.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ImportAsync(
        string csr, string? requester = null, string? notes = null, string? suggestedRole = null, bool? allowDuplicate = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(csr);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("csr", csr);
            if (requester is not null)
            {
                writer.WriteString("requester", requester);
            }

            if (notes is not null)
            {
                writer.WriteString("notes", notes);
            }

            if (suggestedRole is not null)
            {
                writer.WriteString("suggested_role", suggestedRole);
            }

            if (allowDuplicate is { } flag)
            {
                writer.WriteBoolean("allow_duplicate", flag);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{Encode(mount)}/sign-request/import", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>LIST {mount}/sign-request/</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{Encode(mount)}/sign-request/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>14 §Bulk metadata listings: <c>GET {mount}/sign-request-info?after=&amp;limit=</c>.</summary>
    /// <remarks>
    /// D-M9-31: pinned to <c>/v2</c>, the same reasoning as <see cref="PkiCsrOperations.ListInfoAsync"/>:
    /// no Prefix column names anything for this row, so Appendix A is silent rather than implying
    /// <c>v1</c>, and <c>14-batch-and-request-efficiency.md:98</c> governs. This reverses D-M9-7.
    /// D-M9-10/D-M9-21 still hold: 09 defines no response shape for this listing's records, so each
    /// record surfaces as the raw wire map (<see cref="PkiWire.ReadRawInfoPage"/>). See R-31.
    /// </remarks>
    public async Task<Page<IReadOnlyDictionary<string, JsonElement>>> ListInfoAsync(
        string mount = DefaultMount, string? after = null, int? limit = null,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        int effectiveLimit = PagingWire.ValidateLimit(limit);
        string query = after is null
            ? $"limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}"
            : $"after={UrlBuilder.EncodeQueryValue(after)}&limit={effectiveLimit.ToString(CultureInfo.InvariantCulture)}";
        string path = $"{Encode(mount)}/sign-request-info?{query}";

        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "keys");
        return PkiWire.ReadRawInfoPage(data, path);
    }

    /// <summary>
    /// D-M9-8: PAG-004's iterator for <see cref="ListInfoAsync"/>, following
    /// <see cref="PkiOperations.ListCertificatesInfoAllAsync"/>'s exact shape.
    /// </summary>
    public IAsyncEnumerable<KeyValuePair<string, IReadOnlyDictionary<string, JsonElement>>> ListInfoAllAsync(
        string mount = DefaultMount,
        int? limit = null,
        int maxRecords = PagingWire.DefaultMaxRecords,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return PagingWire.IteratePagesAsync(
            (after, token) => ListInfoAsync(mount, after, limit, options, token),
            maxRecords,
            cancellationToken);
    }

    /// <summary><c>GET {mount}/sign-request/{id}</c>. 09 names no response shape, so the untyped map fallback applies (D-M9-21). See R-31.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", ItemPath(mount, id), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary><c>DELETE {mount}/sign-request/{id}</c>.</summary>
    public async Task DeleteAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", ItemPath(mount, id), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary><c>POST {mount}/sign-request/{id}/preflight</c>. 09 names no request or response fields, so the untyped map fallback applies (D-M9-21). See R-31.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> PreflightAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{ItemPath(mount, id)}/preflight", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>
    /// <c>POST {mount}/sign-request/{id}/approve</c>. 09-pki-engine.md:103 names <paramref name="role"/>
    /// and an <c>overrides?</c> parameter but no field list for the latter. <c>Pki.Sign</c>'s sibling
    /// row (<c>09-pki-engine.md:30</c>, "+ overrides") settles the <b>placement</b> — <see
    /// cref="PkiOperations.SignAsync"/> writes its override fields flat into the same object as
    /// <c>csr</c> — but not the <b>field set</b>: D-M9-17 could transcribe <see cref="SignRequest"/>'s
    /// complete set there because 09 signals a superset for that very operation, and no document does
    /// so here, so D-M1c-25 forbids guessing one. <paramref name="overrides"/> is therefore a raw
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> written flat into the request body next to
    /// <c>role</c> (<see cref="PkiWire.WriteFlatMap"/>), rather than typed fields or an invented
    /// nested key — D-M9-24 accepts this design subject to RF-1's collision guard: <c>role</c> is
    /// reserved, so an <c>overrides</c> entry named <c>role</c> fails client-side with
    /// <c>BV-INPUT-001</c> rather than silently outranking the named parameter on the wire (a JSON
    /// object with a duplicate key resolves to whichever the parser reads last). 09 names no response
    /// shape either, and no parameter here establishes exported key material, so the result is the
    /// untyped map fallback (D-M9-21). See R-31.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ApproveAsync(
        string id, string role, IReadOnlyDictionary<string, JsonElement>? overrides = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{ItemPath(mount, id)}/approve";
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteString("role", role);
            PkiWire.WriteFlatMap(writer, overrides, ["role"], path);
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>
    /// <c>POST {mount}/sign-request/{id}/approve-verbatim</c> (server-enforced <c>ttl</c> ≤ 30 days;
    /// TRN-031: 09-pki-engine.md:104 does not quote <c>ttl</c>, so it is integer seconds like
    /// <see cref="PkiOperations.SignVerbatimAsync"/>'s <c>ttl</c>, not a Go-style string). No
    /// requirement ID mandates a client-side cap, so none is added (D-M1c-25) — the 30-day limit is
    /// left to the server. 09 names no response shape either, and no parameter here establishes
    /// exported key material, so the untyped map fallback applies (D-M9-21). See R-31.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ApproveVerbatimAsync(
        string id, TimeSpan? ttl = null, string? issuerRef = null,
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            PkiWire.WriteSeconds(writer, "ttl", ttl);
            if (issuerRef is not null)
            {
                writer.WriteString("issuer_ref", issuerRef);
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST", $"{ItemPath(mount, id)}/approve-verbatim", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>
    /// <c>POST {mount}/sign-request/{id}/reject</c>. PKI-030's first limb: an empty or
    /// whitespace-only <paramref name="reason"/> fails client-side with <c>BV-INPUT-001</c>, no
    /// request sent. The second limb — a 500-pending queue-cap breach recognised as
    /// <c>BV-QUOTA-002 QueueFull</c> "by message" — stays on the traceability baseline (D-M9-11): no
    /// document states the server's message, so nothing is guessed (D-M1c-25).
    /// </summary>
    public async Task RejectAsync(
        string id, string reason, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{ItemPath(mount, id)}/reject";
        PkiWire.RequireReason(reason, path);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("reason", reason));
        _ = await logical.ExecuteShapedAsync(
            "POST", path, body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string ItemPath(string mount, string id)
    {
        return $"{Encode(mount)}/sign-request/{UrlBuilder.EncodePathSegment(id)}";
    }

    private static string Encode(string mount)
    {
        return UrlBuilder.EncodePathFragment(mount.Trim('/'));
    }
}
