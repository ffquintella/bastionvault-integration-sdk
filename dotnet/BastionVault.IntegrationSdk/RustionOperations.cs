using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The Rustion bastion-integration surface (12 §Rustion), reached from
/// <see cref="BastionVaultClient.Rustion"/>. <c>mount</c> defaults to <c>"rustion"</c>; only the
/// operator-facing subset below is typed, everything else is <c>Logical.*</c>. No field-level
/// schema is documented for <see cref="Targets"/>/<see cref="Master"/>/<see cref="Authority"/>,
/// so those carry the raw <c>JsonElement</c>/<see cref="Response"/> idiom.
/// </summary>
public sealed class RustionOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
        Targets = new RustionTargetsOperations(context, activeNamespace);
        Master = new RustionMasterOperations(context, activeNamespace);
        Authority = new RustionAuthorityOperations(context, activeNamespace);
        Session = new RustionSessionOperations(context, activeNamespace);
        Recordings = new RustionRecordingsOperations(context, activeNamespace);
        Policy = new RustionPolicyOperations(context, activeNamespace);
        BastionGroups = new RustionBastionGroupsOperations(context, activeNamespace);
        Dispatcher = new RustionDispatcherOperations(context, activeNamespace);
        Telemetry = new RustionTelemetryOperations(context, activeNamespace);
    }

    /// <summary><c>{mount}/targets[/health|/probe|/{id}[/probe|/listeners/refresh]]</c>.</summary>
    public RustionTargetsOperations Targets { get; }

    /// <summary><c>{mount}/master/{config|pubkey|issue|rotate}</c>.</summary>
    public RustionMasterOperations Master { get; }

    /// <summary><c>{mount}/authority/attest</c> — typed despite section 12's table omitting it (Appendix B names it by this canonical operation twice).</summary>
    public RustionAuthorityOperations Authority { get; }

    /// <summary><c>{mount}/session/{open|renew|kill}</c> plus the <c>/v2</c>-pinned <c>OpenConnectOnly</c>.</summary>
    public RustionSessionOperations Session { get; }

    /// <summary><c>{mount}/recordings/*</c>, including RUS-001's <c>Download</c> and RUS-002's node-local chunk/blob reads.</summary>
    public RustionRecordingsOperations Recordings { get; }

    /// <summary><c>{mount}/policy/{global,type/{t},asset-group/{id},resource/{id},force-rustion,effective}</c>.</summary>
    public RustionPolicyOperations Policy { get; }

    /// <summary><c>{mount}/bastion-groups[/{name}]</c>.</summary>
    public RustionBastionGroupsOperations BastionGroups { get; }

    /// <summary><c>{mount}/dispatcher/preview</c>.</summary>
    public RustionDispatcherOperations Dispatcher { get; }

    /// <summary><c>{mount}/telemetry[/poll]</c>.</summary>
    public RustionTelemetryOperations Telemetry { get; }

    /// <summary>Reads the deployment identifier: <c>GET {mount}/deployment-id</c>. 12 names no response shape, so this returns the raw map rather than assuming one (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw response map verbatim, or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.DeploymentId — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> DeploymentIdAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.Encode(mount)}/deployment-id", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }
}

/// <summary><c>{mount}/targets[/health|/probe|/{id}[/probe|/listeners/refresh]]</c>. No field-level schema is documented; every operation carries the raw idiom.</summary>
public sealed class RustionTargetsOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionTargetsOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Lists the target names: <c>LIST {mount}/targets/</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns an empty list when the backend has none, never <see langword="null"/>. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.List — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyList<string>> ListAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", $"{RustionWire.Encode(mount)}/targets/", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>Creates a target: <c>POST {mount}/targets/</c>. <paramref name="target"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="target"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.Create — 12-other-engines-and-identity.md</spec>
    public Task<Response?> CreateAsync(
        JsonElement target, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/targets/", SysWire.RequireJsonBody(target, "target"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Reads a target: <c>GET {mount}/targets/{id}</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.Read — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ReadAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", TargetPath(mount, id), null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Writes a target: <c>PUT {mount}/targets/{id}</c>. <paramref name="target"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; body carries <paramref name="target"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.Write — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteAsync(
        string id, JsonElement target, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "PUT", TargetPath(mount, id), SysWire.RequireJsonBody(target, "target"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Deletes a target: <c>DELETE {mount}/targets/{id}</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.Delete — 12-other-engines-and-identity.md</spec>
    public async Task DeleteAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", TargetPath(mount, id), null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Probes one target: <c>POST {mount}/targets/{id}/probe</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.Probe — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ProbeAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{TargetPath(mount, id)}/probe", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Probes every target: <c>POST {mount}/targets/probe</c> — every target, not one.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.ProbeAll — 12-other-engines-and-identity.md</spec>
    public Task<Response?> ProbeAllAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/targets/probe", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Reads aggregate target health: <c>GET {mount}/targets/health</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.Health — 12-other-engines-and-identity.md</spec>
    public Task<Response?> HealthAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.Encode(mount)}/targets/health", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Refreshes a target's listeners: <c>POST {mount}/targets/{id}/listeners/refresh</c>.</summary>
    /// <remarks>Wire params: <paramref name="id"/>/<paramref name="mount"/> build the route; no body. Returns <see langword="void"/> on success. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Targets.RefreshListeners — 12-other-engines-and-identity.md</spec>
    public async Task RefreshListenersAsync(
        string id, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        _ = await logical.ExecuteShapedAsync(
            "POST", $"{TargetPath(mount, id)}/listeners/refresh", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static string TargetPath(string mount, string id)
    {
        return $"{RustionWire.Encode(mount)}/targets/{UrlBuilder.EncodePathSegment(id)}";
    }
}

/// <summary><c>{mount}/master/{config|pubkey|issue|rotate}</c>. No field-level schema is documented; every operation carries the raw idiom.</summary>
public sealed class RustionMasterOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionMasterOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Reads the master engine's configuration: <c>GET {mount}/master/config</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw response map verbatim, or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Master.ReadConfig — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> ReadConfigAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.Encode(mount)}/master/config", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Writes the master engine's configuration: <c>POST {mount}/master/config</c>. <paramref name="config"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="config"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Master.WriteConfig — 12-other-engines-and-identity.md</spec>
    public Task<Response?> WriteConfigAsync(
        JsonElement config, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/master/config", SysWire.RequireJsonBody(config, "config"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Reads the master public key: <c>GET {mount}/master/pubkey</c>. Public key material, not secret — no <see cref="SecretString"/> wrapping.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw response map verbatim, or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Master.PubKey — 12-other-engines-and-identity.md</spec>
    public async Task<IReadOnlyDictionary<string, JsonElement>?> PubKeyAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", $"{RustionWire.Encode(mount)}/master/pubkey", null, options,
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data;
    }

    /// <summary>Issues a master credential: <c>POST {mount}/master/issue</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/> verbatim when supplied, otherwise none. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Master.Issue — 12-other-engines-and-identity.md</spec>
    public Task<Response?> IssueAsync(
        JsonElement? request = null, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte>? body = request is { } value ? SysWire.RequireJsonBody(value, "request") : null;
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/master/issue", body, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }

    /// <summary>Rotates the master key: <c>POST {mount}/master/rotate</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; no body. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Master.Rotate — 12-other-engines-and-identity.md</spec>
    public Task<Response?> RotateAsync(
        string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/master/rotate", null, options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }
}

/// <summary><c>{mount}/authority/attest</c>. Not in section 12's own table, but Appendix B's error hints name it by this canonical operation twice (D-M10-e).</summary>
public sealed class RustionAuthorityOperations
{
    private const string DefaultMount = "rustion";

    private readonly LogicalOperations logical;

    internal RustionAuthorityOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Submits an authority attestation envelope: <c>POST {mount}/authority/attest</c>. <paramref name="request"/> is sent verbatim (D-M1c-25).</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/> verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Authority.Attest — 12-other-engines-and-identity.md</spec>
    public Task<Response?> AttestAsync(
        JsonElement request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/authority/attest", SysWire.RequireJsonBody(request, "request"), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true);
    }
}

/// <summary><c>{mount}/session/{open|renew|kill}</c> plus the <c>/v2</c>-pinned <c>OpenConnectOnly</c>.</summary>
public sealed class RustionSessionOperations
{
    private const string DefaultMount = "rustion";

    /// <summary>
    /// <c>OpenConnectOnly</c>'s literal path (Appendix A: <c>/v2/rustion/session/open</c>, bolded and
    /// hardcoded, unlike every other Rustion row): not <c>{mount}</c>-templated. A caller changing
    /// <c>mount</c> does not change this route.
    /// </summary>
    private const string ConnectOnlyPath = "rustion/session/open";

    private readonly LogicalOperations logical;

    internal RustionSessionOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Opens a v1 bastion session: <c>POST {mount}/session/open</c>. <c>credential_material</c> is <see cref="SecretString"/>-typed; every other field is an opaque bag.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/>'s <c>credential_material</c> and remaining fields verbatim. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> when <see cref="RustionSessionRequest.CredentialMaterial"/> is empty (client-side, no request sent).</remarks>
    /// <spec>Rustion.Session.Open — 12-other-engines-and-identity.md</spec>
    public async Task<Response?> OpenAsync(
        RustionSessionRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string path = $"{RustionWire.Encode(mount)}/session/open";
        if (!request.CredentialMaterial.HasValue)
        {
            throw KvWire.InvalidArgument("credentialMaterial", "must not be empty", path);
        }

        return await logical.ExecuteShapedAsync(
            "POST", path, RustionWire.SerialiseSessionRequest(request, path), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a v2 connect-only session: <c>POST /v2/rustion/session/open</c>. Every field 12
    /// §Rustion documents is typed. R-33: <c>secret_id</c> and <c>connect_ticket</c> travel in
    /// this POST body only.
    /// </summary>
    /// <remarks>Wire params: none — the path is literal, not <c>{mount}</c>-templated; body carries <paramref name="request"/>'s <c>resource_name</c>, <c>credential_source</c>, <c>target_host</c>, <c>target_port</c>, <c>target_protocol</c>, <c>profile_id</c>, <c>connect_ticket</c>. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Session.OpenConnectOnly — 12-other-engines-and-identity.md</spec>
    public async Task<Response?> OpenConnectOnlyAsync(
        RustionSessionOpenConnectOnlyRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.ResourceName);
        ArgumentException.ThrowIfNullOrEmpty(request.SecretId);
        ArgumentException.ThrowIfNullOrEmpty(request.TargetHost);
        ArgumentException.ThrowIfNullOrEmpty(request.TargetProtocol);
        return await logical.ExecuteShapedAsync(
            "POST", ConnectOnlyPath, RustionWire.SerialiseOpenConnectOnly(request), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Renews a bastion session: <c>POST {mount}/session/renew</c>.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/>'s <c>bastion_id</c>, <c>session_id</c>, <c>correlation_id</c> (validated non-empty client-side), <c>extend_secs</c>. Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Session.Renew — 12-other-engines-and-identity.md</spec>
    public async Task<Response?> RenewAsync(
        RustionSessionRenewRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.BastionId);
        ArgumentException.ThrowIfNullOrEmpty(request.SessionId);
        ArgumentException.ThrowIfNullOrEmpty(request.CorrelationId);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return await logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/session/renew", RustionWire.SerialiseRenew(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Kills a bastion session: <c>POST {mount}/session/kill</c>. See <see cref="RustionSessionKillRequest"/>'s remarks for the inferred shape.</summary>
    /// <remarks>Wire params: <paramref name="mount"/> builds the route; body carries <paramref name="request"/>'s <c>bastion_id</c>, <c>session_id</c>, <c>correlation_id</c> (validated non-empty client-side). Returns the raw <see cref="Response"/> (no field-level schema documented), or <see langword="null"/> per the shared envelope rules. Conformance: Complete. No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Rustion.Session.Kill — 12-other-engines-and-identity.md</spec>
    public async Task<Response?> KillAsync(
        RustionSessionKillRequest request, string mount = DefaultMount, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.BastionId);
        ArgumentException.ThrowIfNullOrEmpty(request.SessionId);
        ArgumentException.ThrowIfNullOrEmpty(request.CorrelationId);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        return await logical.ExecuteShapedAsync(
            "POST", $"{RustionWire.Encode(mount)}/session/kill", RustionWire.SerialiseKill(request), options,
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }
}
