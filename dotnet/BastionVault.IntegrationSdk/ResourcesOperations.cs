using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §Resources: the <c>resource</c> engine, reached from <see cref="BastionVaultClient.Resources"/>.
/// <c>mount</c> defaults to <c>"resources"</c>. RSC-002's asymmetry: <see cref="ReadAsync"/>'s
/// record is a plain, unredacted <see cref="Response"/> (D-M1c-25), while <see cref="Secrets"/>'
/// values are redacting. An unnamed verb follows the GET/PUT/DELETE convention
/// <see cref="AssetGroupOperations"/> already uses for the same ambiguity.
/// </summary>
public sealed class ResourcesOperations
{
    private const string DefaultMount = "resources";

    private readonly LogicalOperations logical;

    internal ResourcesOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        Secrets = new ResourcesSecretsOperations(context, activeNamespace);
        Connect = new ResourcesConnectOperations(context, activeNamespace);
    }

    /// <summary>12 §Resources: <c>{mount}/secrets/{resource}/*</c>. RSC-002: values are redacting.</summary>
    public ResourcesSecretsOperations Secrets { get; }

    /// <summary>12 §Resources: <c>{mount}/v2/connect/*</c>, the connect-MFA flow (RSC-001).</summary>
    public ResourcesConnectOperations Connect { get; }

    /// <summary>Reads the resource-type schema: <c>GET {mount}/config/types</c>. 12 names no field set for the schema.</summary>
    /// <remarks>Wire params: none beyond <paramref name="mount"/>. Returns the schema as a raw field map, or <see langword="null"/> when none is set (404 treated as absent). Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.ReadTypes — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadTypesAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{ResourceWire.Encode(mount)}/config/types", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Writes the resource-type schema: <c>POST {mount}/config/types</c>. <paramref name="schema"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: schema is sent verbatim as the body. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <paramref name="schema"/> is undefined.</remarks>
    /// <spec>Resources.WriteTypes — 12-other-engines-and-identity.md</spec>
    public async Task WriteTypesAsync(
        JsonElement schema, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{ResourceWire.Encode(mount)}/config/types", SysWire.RequireJsonBody(schema, "schema"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Lists resource names: <c>LIST {mount}/resources/</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="mount"/>. Returns the resource names, empty (never <see langword="null"/>) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.List — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{ResourceWire.Encode(mount)}/resources/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>Searches resources by query: <c>POST {mount}/resources/search</c>. Body, not a query string (the spec's own note).</summary>
    /// <remarks>Wire params: q?, type?, offset?, limit?, all omitted when unset (<see cref="ResourceSearchQuery"/>). Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Search — 12-other-engines-and-identity.md</spec>
    public Task<Response?> SearchAsync(
        ResourceSearchQuery query, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{ResourceWire.Encode(mount)}/resources/search", ResourceWire.SerialiseSearch(query), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Reads a resource record: <c>GET {mount}/resources/{name}</c>. RSC-002: not redacted.</summary>
    /// <remarks>Wire params: none beyond <paramref name="name"/>/<paramref name="mount"/>. Returns the raw <see cref="Response"/>, or <see langword="null"/> when <paramref name="name"/> is not found (404 treated as absent). RSC-002: this record is plain and unredacted, unlike <see cref="ResourcesSecretsOperations"/>'s values. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Read — RSC-002</spec>
    public Task<Response?> ReadAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", ResourceWire.ResourcePath(mount, name), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Creates or replaces a resource record: <c>PUT {mount}/resources/{name}</c>. <paramref name="record"/> is sent verbatim.</summary>
    /// <remarks>Wire params: record is sent verbatim as the body. Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <paramref name="record"/> is undefined.</remarks>
    /// <spec>Resources.Write — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteAsync(
        string name, JsonElement record, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", ResourceWire.ResourcePath(mount, name), SysWire.RequireJsonBody(record, "record"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes a resource record: <c>DELETE {mount}/resources/{name}</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="name"/>/<paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Delete — 12-other-engines-and-identity.md</spec>
    public async Task DeleteAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", ResourceWire.ResourcePath(mount, name), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a resource's change history: <c>GET {mount}/resources/{name}/history</c>. No shape beyond the array itself.</summary>
    /// <remarks>Wire params: none beyond <paramref name="name"/>/<paramref name="mount"/>. Returns an empty list when there is no history, never <see langword="null"/>; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.History — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> HistoryAsync(
        string name, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{ResourceWire.ResourcePath(mount, name)}/history", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary>Renames a resource, migrating its secrets, shares, groups and ownership: <c>POST {mount}/resources/{name}/rename</c> with <c>{"new_name": newName}</c>.</summary>
    /// <remarks>Wire params: new_name from <paramref name="newName"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Rename — 12-other-engines-and-identity.md</spec>
    public async Task RenameAsync(
        string name, string newName, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(newName);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer => writer.WriteString("new_name", newName));
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{ResourceWire.ResourcePath(mount, name)}/rename", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }
}

/// <summary>12 §Resources: <c>{mount}/secrets/{resource}/{key}[/history|/version/{n}]</c>. RSC-002: values are redacting.</summary>
public sealed class ResourcesSecretsOperations
{
    private const string DefaultMount = "resources";

    private readonly LogicalOperations logical;

    internal ResourcesSecretsOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Lists a resource's secret keys: <c>LIST {mount}/secrets/{resource}/</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="resource"/>/<paramref name="mount"/>. Returns the secret keys, empty (never <see langword="null"/>) when none exist or the path is absent. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Secrets.List — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<string>> ListAsync(
        string resource, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{ResourceWire.Encode(mount)}/secrets/{UrlBuilder.EncodePathSegment(resource)}/";
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>RSC-002: <c>GET {mount}/secrets/{resource}/{key}</c>. Each field's value comes back redacting.</summary>
    /// <remarks>Wire params: none beyond <paramref name="resource"/>/<paramref name="key"/>/<paramref name="mount"/>. Returns <see cref="ResourceSecret"/>, or <see langword="null"/> when not found (404 treated as absent); RSC-002: each field's value comes back redacting via <see cref="SecretString"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Secrets.Read — RSC-002</spec>
    public async Task<ResourceSecret?> ReadAsync(
        string resource, string key, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = ResourceWire.SecretPath(mount, resource, key);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        return ResourceWire.ReadSecret(response.Data ?? throw KvWire.EnvelopeMismatch(path, "value"));
    }

    /// <summary>Writes a secret value: <c>PUT {mount}/secrets/{resource}/{key}</c>. <paramref name="value"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: value is sent verbatim as the body. Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <paramref name="value"/> is undefined.</remarks>
    /// <spec>Resources.Secrets.Write — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteAsync(
        string resource, string key, JsonElement value, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", ResourceWire.SecretPath(mount, resource, key), SysWire.RequireJsonBody(value, "value"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes a secret value: <c>DELETE {mount}/secrets/{resource}/{key}</c>.</summary>
    /// <remarks>Wire params: none beyond <paramref name="resource"/>/<paramref name="key"/>/<paramref name="mount"/>. Returns <see langword="void"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Secrets.Delete — 12-other-engines-and-identity.md</spec>
    public async Task DeleteAsync(
        string resource, string key, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", ResourceWire.SecretPath(mount, resource, key), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a secret's change history: <c>GET {mount}/secrets/{resource}/{key}/history</c>. No shape beyond the array itself.</summary>
    /// <remarks>Wire params: none beyond <paramref name="resource"/>/<paramref name="key"/>/<paramref name="mount"/>. Returns an empty list when there is no history, never <see langword="null"/>; each entry is a raw <see cref="JsonElement"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Secrets.History — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<JsonElement>> HistoryAsync(
        string resource, string key, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{ResourceWire.SecretPath(mount, resource, key)}/history", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return IdentityKernelWire.ReadArrayEnvelope(response);
    }

    /// <summary>RSC-002: <c>GET {mount}/secrets/{resource}/{key}/version/{n}</c>. Each field's value comes back redacting.</summary>
    /// <remarks>Wire params: version builds the route, no body. Returns <see cref="ResourceSecret"/>, or <see langword="null"/> when not found (404 treated as absent); RSC-002: each field's value comes back redacting via <see cref="SecretString"/>, the same as this type's Read. Conformance: Level X (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Resources.Secrets.ReadVersion — RSC-002</spec>
    public async Task<ResourceSecret?> ReadVersionAsync(
        string resource, string key, int version, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{ResourceWire.SecretPath(mount, resource, key)}/version/{version.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        return ResourceWire.ReadSecret(response.Data ?? throw KvWire.EnvelopeMismatch(path, "value"));
    }
}

/// <summary>
/// 12 §Resources: <c>{mount}/v2/connect/*</c>, the connect-MFA flow. RSC-001's <c>BV-AUTH-002</c>/
/// <c>BV-AUTH-016</c> mapping is the standard message pipeline (an exact-message row already in
/// Appendix B §2) — only the client-side <c>resource is required</c> guard needs code here.
/// </summary>
public sealed class ResourcesConnectOperations
{
    private const string DefaultMount = "resources";

    private readonly LogicalOperations logical;

    internal ResourcesConnectOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>RSC-001: <c>POST {mount}/v2/connect/mfa/begin</c>. No shape given beyond "→ factors" (D-M1c-25).</summary>
    /// <remarks>Wire params: resource, profile_id (<see cref="ConnectMfaBeginRequest"/>). Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <see cref="ConnectMfaBeginRequest.Resource"/> is empty (RSC-001, refused client-side before any request is sent); <c>BV-AUTH-002</c> for an unauthenticated caller (RSC-001).</remarks>
    /// <spec>Resources.Connect.MfaBegin — RSC-001</spec>
    public Task<Response?> MfaBeginAsync(
        ConnectMfaBeginRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{ResourceWire.Encode(mount)}/v2/connect/mfa/begin";
        ResourceWire.RequireResource(request.Resource, path);
        return logical.ExecuteShapedAsync(
            "POST", path, ResourceWire.SerialiseMfaBegin(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>RSC-001: <c>POST {mount}/v2/connect/mfa/verify</c> → <c>{connect_ticket}</c>, single-use and redacting.</summary>
    /// <remarks>Wire params: resource, profile_id, method, totp_code?, credential? (<see cref="ConnectMfaVerifyRequest"/>). Returns <see cref="ConnectMfaVerifyResult"/>, never <see langword="null"/>; <see cref="ConnectMfaVerifyResult.ConnectTicket"/> is a single-use, redacting <see cref="SecretString"/>. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <see cref="ConnectMfaVerifyRequest.Resource"/> is empty (RSC-001, refused client-side); <c>BV-AUTH-002</c> for an unauthenticated caller; <c>BV-AUTH-016</c> when second-factor verification fails (RSC-001).</remarks>
    /// <spec>Resources.Connect.MfaVerify — RSC-001</spec>
    public async Task<ConnectMfaVerifyResult> MfaVerifyAsync(
        ConnectMfaVerifyRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{ResourceWire.Encode(mount)}/v2/connect/mfa/verify";
        ResourceWire.RequireResource(request.Resource, path);
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, ResourceWire.SerialiseMfaVerify(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data ?? throw KvWire.EnvelopeMismatch(path, "connect_ticket");
        return ResourceWire.ReadMfaVerifyResult(data, path);
    }

    /// <summary>
    /// RSC-001: <c>POST {mount}/v2/connect/authorize</c>. R-33 guard: the ticket travels in this
    /// POST body only, never a query string (<see cref="ResourceWire.SerialiseAuthorize"/>).
    /// </summary>
    /// <remarks>Wire params: resource, profile_id, connect_ticket? (<see cref="ConnectAuthorizeRequest"/>). Returns the raw <see cref="Response"/>, which may be <see langword="null"/> for an empty body. Conformance: Level X (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <see cref="ConnectAuthorizeRequest.Resource"/> is empty (RSC-001, refused client-side); <c>BV-AUTH-002</c> for an unauthenticated caller (RSC-001).</remarks>
    /// <spec>Resources.Connect.Authorize — RSC-001</spec>
    public Task<Response?> AuthorizeAsync(
        ConnectAuthorizeRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{ResourceWire.Encode(mount)}/v2/connect/authorize";
        ResourceWire.RequireResource(request.Resource, path);
        return logical.ExecuteShapedAsync(
            "POST", path, ResourceWire.SerialiseAuthorize(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }
}
