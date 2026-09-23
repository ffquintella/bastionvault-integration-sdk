using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The SSH login-brokering policy surface (10 — SSH broker), reached from
/// <see cref="BastionVaultClient.SshBroker"/>. The logical mount is the fixed
/// <c>ssh-broker</c> — not a caller-supplied <c>{mount}</c> placeholder (10-ssh-engine.md:12,22
/// contrasted with :77-81) — and every route is <c>/v2</c>-pinned (SSB-001).
/// </summary>
public sealed class SshBrokerOperations
{
    private const string Root = "ssh-broker/policy";

    private readonly LogicalOperations logical;

    internal SshBrokerOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>Reads the root-gated global login-class policy: <c>GET /v2/ssh-broker/policy/global</c>.</summary>
    /// <remarks>
    /// Wire params: none; the route is fixed and <c>/v2</c>-pinned (SSB-001). Returns
    /// <see cref="SshBrokerGlobalPolicy"/>, or <see langword="null"/> when unset. Conformance:
    /// Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.ReadGlobal — 10-ssh-engine.md</spec>
    public async Task<SshBrokerGlobalPolicy?> ReadGlobalAsync(
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string path = $"{Root}/global";
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? SshWire.ReadGlobalPolicy(data) : null;
    }

    /// <summary>
    /// Writes the root-gated global login-class policy: <c>PUT /v2/ssh-broker/policy/global</c>.
    /// D-M9-27: <c>10-ssh-engine.md:77</c> states the verb explicitly.
    /// </summary>
    /// <remarks>
    /// Wire params: none in the route; body carries <c>login_class_default</c>/<c>login_class_lock</c>.
    /// Returns <see langword="void"/> on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond
    /// the common set (ERR-061): <c>BV-AUTHZ-005 LoginClassLocked</c> (SSB-002) when this tier is
    /// already locked.
    /// </remarks>
    /// <spec>SshBroker.WriteGlobal — 10-ssh-engine.md</spec>
    public async Task WriteGlobalAsync(
        SshBrokerGlobalPolicy policy, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        string path = $"{Root}/global";
        _ = await logical.ExecuteShapedAsync(
            "PUT", path, SshWire.SerialiseGlobalPolicy(policy), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a role-type login-class policy: <c>GET /v2/ssh-broker/policy/type/{type}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="type"/> builds the route (SSB-001, <c>/v2</c>-pinned). Returns
    /// <see cref="SshBrokerTypePolicy"/>, or <see langword="null"/> when unset. Conformance:
    /// Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.ReadType — 10-ssh-engine.md</spec>
    public async Task<SshBrokerTypePolicy?> ReadTypeAsync(
        string type, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        string path = TypePath(type);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? SshWire.ReadTypePolicy(data) : null;
    }

    /// <summary>
    /// Writes a role-type login-class policy: <c>PUT /v2/ssh-broker/policy/type/{type}</c>.
    /// D-M9-27: <c>10-ssh-engine.md:78</c> states no verb for this row; <c>PUT</c> is inferred as
    /// this policy family's one sibling write, and is booked to M12 for server verification if
    /// wrong.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="type"/> builds the route; body carries <c>policy</c>'s
    /// <c>login_class</c>/<c>lock</c> (SSB-001, <c>/v2</c>-pinned). Returns <see langword="void"/>
    /// on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061):
    /// <c>BV-AUTHZ-005 LoginClassLocked</c> (SSB-002) when this tier is already locked.
    /// </remarks>
    /// <spec>SshBroker.WriteType — 10-ssh-engine.md</spec>
    public async Task WriteTypeAsync(
        string type, SshBrokerTypePolicy policy, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(policy);
        _ = await logical.ExecuteShapedAsync(
            "PUT", TypePath(type), SshWire.SerialiseTypePolicy(policy), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Deletes a role-type login-class policy: <c>DELETE /v2/ssh-broker/policy/type/{type}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="type"/> builds the route (SSB-001, <c>/v2</c>-pinned). Returns
    /// <see langword="void"/> on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond
    /// the common set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.DeleteType — 10-ssh-engine.md</spec>
    public async Task DeleteTypeAsync(
        string type, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", TypePath(type), null, IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads an asset-group login-class policy: <c>GET /v2/ssh-broker/policy/asset-group/{id}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="id"/> builds the route (SSB-001, <c>/v2</c>-pinned). Returns
    /// <see cref="SshBrokerAssetGroupPolicy"/>, or <see langword="null"/> when unset. Conformance:
    /// Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.ReadAssetGroup — 10-ssh-engine.md</spec>
    public async Task<SshBrokerAssetGroupPolicy?> ReadAssetGroupAsync(
        string id, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        string path = AssetGroupPath(id);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? SshWire.ReadAssetGroupPolicy(data) : null;
    }

    /// <summary>
    /// Writes an asset-group login-class policy: <c>PUT /v2/ssh-broker/policy/asset-group/{id}</c>.
    /// D-M9-27: <c>10-ssh-engine.md:79</c> states no verb for this row; <c>PUT</c> is inferred as
    /// this policy family's one sibling write, and is booked to M12 for server verification if
    /// wrong.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="id"/> builds the route; body carries <c>policy</c>'s
    /// <c>login_class</c>/<c>priority</c>/<c>lock</c> (SSB-001, <c>/v2</c>-pinned). Returns
    /// <see langword="void"/> on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the
    /// common set (ERR-061): <c>BV-AUTHZ-005 LoginClassLocked</c> (SSB-002) when this tier is
    /// already locked.
    /// </remarks>
    /// <spec>SshBroker.WriteAssetGroup — 10-ssh-engine.md</spec>
    public async Task WriteAssetGroupAsync(
        string id, SshBrokerAssetGroupPolicy policy, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(policy);
        _ = await logical.ExecuteShapedAsync(
            "PUT", AssetGroupPath(id), SshWire.SerialiseAssetGroupPolicy(policy), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Deletes an asset-group login-class policy: <c>DELETE /v2/ssh-broker/policy/asset-group/{id}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="id"/> builds the route (SSB-001, <c>/v2</c>-pinned). Returns
    /// <see langword="void"/> on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond
    /// the common set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.DeleteAssetGroup — 10-ssh-engine.md</spec>
    public async Task DeleteAssetGroupAsync(
        string id, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", AssetGroupPath(id), null, IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Reads a resource login-class policy: <c>GET /v2/ssh-broker/policy/resource/{id}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="id"/> builds the route (SSB-001, <c>/v2</c>-pinned). Returns
    /// <see cref="SshBrokerResourcePolicy"/>, or <see langword="null"/> when unset. Conformance:
    /// Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.ReadResource — 10-ssh-engine.md</spec>
    public async Task<SshBrokerResourcePolicy?> ReadResourceAsync(
        string id, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        string path = ResourcePath(id);
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return response?.Data is { } data ? SshWire.ReadResourcePolicy(data) : null;
    }

    /// <summary>
    /// Writes a resource login-class policy: <c>PUT /v2/ssh-broker/policy/resource/{id}</c>.
    /// D-M9-27: <c>10-ssh-engine.md:80</c> states no verb for this row; <c>PUT</c> is inferred as
    /// this policy family's one sibling write, and is booked to M12 for server verification if
    /// wrong.
    /// </summary>
    /// <remarks>
    /// Wire params: <paramref name="id"/> builds the route; body carries <c>policy</c>'s
    /// <c>login_class</c> (SSB-001, <c>/v2</c>-pinned). Returns <see langword="void"/> on success.
    /// Conformance: Complete (Appendix A groups this mount, no per-operation row). Errors beyond the common set (ERR-061):
    /// <c>BV-AUTHZ-005 LoginClassLocked</c> (SSB-002) when this tier is already locked;
    /// <c>BV-CONFLICT-003 BrokeredResourceStaticCredential</c> (SSB-002) when a static credential
    /// is attached to a brokered resource.
    /// </remarks>
    /// <spec>SshBroker.WriteResource — 10-ssh-engine.md</spec>
    public async Task WriteResourceAsync(
        string id, SshBrokerResourcePolicy policy, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(policy);
        _ = await logical.ExecuteShapedAsync(
            "PUT", ResourcePath(id), SshWire.SerialiseResourcePolicy(policy), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>Deletes a resource login-class policy: <c>DELETE /v2/ssh-broker/policy/resource/{id}</c>.</summary>
    /// <remarks>
    /// Wire params: <paramref name="id"/> builds the route (SSB-001, <c>/v2</c>-pinned). Returns
    /// <see langword="void"/> on success. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond
    /// the common set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.DeleteResource — 10-ssh-engine.md</spec>
    public async Task DeleteResourceAsync(
        string id, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        _ = await logical.ExecuteShapedAsync(
            "DELETE", ResourcePath(id), null, IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the effective login-class for a resource across all four policy tiers:
    /// <c>POST /v2/ssh-broker/policy/effective</c>. <paramref name="assetGroupIds"/> is
    /// CSV-joined on the wire (<see cref="SshWire.SerialiseEffectiveRequest"/>), matching the
    /// accepted <c>sshbroker.effective-v2-pinned</c> fixture.
    /// </summary>
    /// <remarks>
    /// Wire params: body carries <paramref name="resourceId"/>/<paramref name="resourceType"/>
    /// (required) and <paramref name="assetGroupIds"/> (optional, CSV-joined) (SSB-001,
    /// <c>/v2</c>-pinned). Returns <see cref="SshBrokerEffectivePolicy"/>, never
    /// <see langword="null"/>. Conformance: Complete (Appendix A groups this mount, no per-operation row). No error codes beyond the common
    /// set (ERR-061).
    /// </remarks>
    /// <spec>SshBroker.Effective — 10-ssh-engine.md</spec>
    public async Task<SshBrokerEffectivePolicy> EffectiveAsync(
        string resourceId, string resourceType, IReadOnlyList<string>? assetGroupIds = null,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceId);
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        string path = $"{Root}/effective";
        Response? response = await logical.ExecuteShapedAsync(
            "POST", path, SshWire.SerialiseEffectiveRequest(resourceId, resourceType, assetGroupIds), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        return SshWire.ReadEffectivePolicy(response?.Data ?? throw KvWire.EnvelopeMismatch(path, "login_class"), path);
    }

    private static string TypePath(string type)
    {
        return $"{Root}/type/{UrlBuilder.EncodePathSegment(type)}";
    }

    private static string AssetGroupPath(string id)
    {
        return $"{Root}/asset-group/{UrlBuilder.EncodePathSegment(id)}";
    }

    private static string ResourcePath(string id)
    {
        return $"{Root}/resource/{UrlBuilder.EncodePathSegment(id)}";
    }
}
