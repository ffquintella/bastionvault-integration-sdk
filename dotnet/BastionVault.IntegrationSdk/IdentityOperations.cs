using System.Buffers;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// SYS-080's identity self-service surface, reached from <see cref="BastionVaultClient.Identity"/>:
/// the calling token's own profile, its default account, its SSH security keys, and its namespace
/// assignment.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <c>Client.Identity</c> and not <c>Client.Sys.Identity</c>.</b> Every route here is under
/// <c>sys/identity/*</c>, so <c>Sys.Identity</c> would mirror the wire. The specification names the
/// operations <c>Identity.Profile.Read()</c> in <c>06-system-api.md</c>'s own table and
/// <c>Identity.Profile.Read</c> in Appendix A's canonical-operation column, and that column is the
/// cross-language contract Rust and Python will transcribe. See DR-0012 D-M7-27.
/// </para>
/// <para>
/// <c>12-other-engines-and-identity.md</c> owns a wider <c>Identity.*</c> surface
/// (<c>Identity.Self</c>, <c>Groups</c>, <c>Sharing</c>, <c>Owner</c>) on the <c>identity/</c>
/// mount, which a later milestone adds <b>to this same class</b>. That is additive; nothing here
/// is re-shaped by it.
/// </para>
/// <para>
/// ⚠️ Every route on this surface is pinned to <c>/v2</c> (SYS-080, TRN-071): neither
/// <c>ApiPrefix</c> nor a per-call <see cref="RequestOptions.ApiVersion"/> can route one at a
/// <c>/v1</c> handler that does not exist.
/// </para>
/// </remarks>
public sealed class IdentityOperations
{
    internal IdentityOperations(ClientContext context, string activeNamespace)
    {
        Profile = new IdentityProfileOperations(context, activeNamespace);
        DefaultAccount = new DefaultAccountOperations(context, activeNamespace);
        SshSecurityKey = new SshSecurityKeyOperations(context, activeNamespace);
        NamespaceAssignment = new NamespaceAssignmentOperations(context, activeNamespace);
    }

    /// <summary>SYS-080: <c>/v2/sys/identity/profile/self[…]</c> — read, change password, update contact.</summary>
    public IdentityProfileOperations Profile { get; }

    /// <summary>SYS-080: <c>/v2/sys/identity/default-account[…]</c> — the self and admin forms.</summary>
    public DefaultAccountOperations DefaultAccount { get; }

    /// <summary>SYS-080: <c>/v2/sys/identity/ssh-security-key[…]</c>.</summary>
    public SshSecurityKeyOperations SshSecurityKey { get; }

    /// <summary>SYS-080: <c>/v2/sys/identity/ns-assignment[…]</c> — the login restriction.</summary>
    public NamespaceAssignmentOperations NamespaceAssignment { get; }
}

/// <summary>SYS-080's profile self-service operations.</summary>
public sealed class IdentityProfileOperations
{
    private readonly LogicalOperations logical;

    internal IdentityProfileOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>
    /// SYS-080: <c>GET /v2/sys/identity/profile/self</c>. The table states this route
    /// <b>never 404s</b>, so the return is non-nullable; a body-less response is
    /// <c>BV-PROTOCOL-002</c> rather than an invented empty profile (D-M1c-25).
    /// </summary>
    public async Task<IdentityProfile> ReadAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/identity/profile/self", null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = response?.Data
            ?? throw IdentityWire.EnvelopeMismatch("sys/identity/profile/self", "username");

        return new IdentityProfile
        {
            Username = SysWire.ReadString(data, "username"),
            DisplayName = SysWire.ReadString(data, "display_name"),
            Email = SysWire.ReadString(data, "email"),
            Phone = SysWire.ReadString(data, "phone"),
            Mount = SysWire.ReadString(data, "mount"),
            Raw = response!.Raw,
        };
    }

    /// <summary>
    /// SYS-080: <c>POST /v2/sys/identity/profile/self/password</c> with
    /// <c>{"current_password", "new_password"}</c>. A <c>400</c> reaches the caller as
    /// <c>BV-INPUT-100</c> and a <c>403</c> as <c>BV-AUTHZ-001</c>, both through the shared status
    /// mapping with no operation-local remap (the same situation as D-M7-18, not D-M7-6's).
    /// </summary>
    public async Task ChangePasswordAsync(
        SecretString currentPassword,
        SecretString newPassword,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        _ = await logical.ExecuteShapedAsync(
            "POST", "sys/identity/profile/self/password", SerialisePassword(currentPassword, newPassword), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SYS-080: <c>POST /v2/sys/identity/profile/self/contact</c>, <b>write-preserve</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ The tri-state is the whole requirement and it is preserved onto the wire:
    /// <see langword="null"/> <b>omits</b> the key and keeps the stored value, <c>""</c> is sent as
    /// an empty string and <b>clears</b> it, and any other value replaces it. Modelling either
    /// parameter as a non-nullable <c>string</c> would collapse "keep" and "clear" into one
    /// request, which is the defect the requirement exists to prevent — the same shape as
    /// SYS-045's tri-state (D-M7-17) and decided in the same way.
    /// </remarks>
    public async Task UpdateContactAsync(
        string? email = null,
        string? phone = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = await logical.ExecuteShapedAsync(
            "POST", "sys/identity/profile/self/contact", SerialiseContact(email, phone), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> SerialisePassword(SecretString currentPassword, SecretString newPassword)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteString("current_password", currentPassword.Reveal());
        writer.WriteString("new_password", newPassword.Reveal());
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }

    /// <summary>SYS-080's write-preserve body: a null member writes no key at all, and <c>""</c> writes an empty string.</summary>
    private static ReadOnlyMemory<byte> SerialiseContact(string? email, string? phone)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        if (email is not null)
        {
            writer.WriteString("email", email);
        }

        if (phone is not null)
        {
            writer.WriteString("phone", phone);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }
}

/// <summary>SYS-080's default-account operations: the self form and the admin form.</summary>
public sealed class DefaultAccountOperations
{
    private const string SelfPath = "sys/identity/default-account/self";

    private readonly LogicalOperations logical;

    internal DefaultAccountOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>
    /// SYS-080: <c>GET /v2/sys/identity/default-account/self</c>. ⚠️ This is the <b>only</b>
    /// operation on this surface the server ever fills <see cref="DefaultAccount.WindowsPassword"/>
    /// on, and only for the record's own owner.
    /// </summary>
    public async Task<DefaultAccount?> ReadSelfAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await ReadAtAsync(SelfPath, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080: <c>POST /v2/sys/identity/default-account/self</c>.</summary>
    public async Task WriteSelfAsync(DefaultAccountSpec spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        await WriteAtAsync(SelfPath, spec, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080 (admin): <c>GET /v2/sys/identity/default-account/{mount}/{name}</c>. <see cref="DefaultAccount.WindowsPassword"/> is never filled here.</summary>
    public async Task<DefaultAccount?> ReadAsync(string mount, string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await ReadAtAsync(IdentityWire.MountAndName("sys/identity/default-account", mount, name), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080 (admin): <c>POST /v2/sys/identity/default-account/{mount}/{name}</c>.</summary>
    public async Task WriteAsync(string mount, string name, DefaultAccountSpec spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        await WriteAtAsync(IdentityWire.MountAndName("sys/identity/default-account", mount, name), spec, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DefaultAccount?> ReadAtAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        if (response?.Data is not { } data)
        {
            return null;
        }

        string? password = SysWire.ReadString(data, "windows_password");
        return new DefaultAccount
        {
            Username = SysWire.ReadString(data, "username"),
            Domain = SysWire.ReadString(data, "domain"),
            // Left null rather than wrapped as an empty SecretString: SYS-080 says the server
            // withholds this field outside a GET by the owner, and "withheld" and "empty" are
            // different facts a caller may need to tell apart.
            WindowsPassword = password is null ? null : new SecretString(password),
            Raw = response.Raw,
        };
    }

    private async Task WriteAtAsync(string path, DefaultAccountSpec spec, RequestOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _ = await logical.ExecuteShapedAsync(
            "POST", path, Serialise(spec), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> Serialise(DefaultAccountSpec spec)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        if (spec.Username is { } username)
        {
            writer.WriteString("username", username);
        }

        if (spec.Domain is { } domain)
        {
            writer.WriteString("domain", domain);
        }

        if (spec.WindowsPassword is { } password)
        {
            writer.WriteString("windows_password", password.Reveal());
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }
}

/// <summary>SYS-080's SSH security-key operations: list, and the self and admin record forms.</summary>
public sealed class SshSecurityKeyOperations
{
    private const string Root = "sys/identity/ssh-security-key";
    private const string SelfPath = Root + "/self";

    private readonly LogicalOperations logical;

    internal SshSecurityKeyOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>SYS-080: <c>LIST /v2/sys/identity/ssh-security-key</c> → <c>{"keys": [...]}</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", Root, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>SYS-080: <c>GET /v2/sys/identity/ssh-security-key/self</c>.</summary>
    public async Task<SshSecurityKey?> ReadSelfAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await ReadAtAsync(SelfPath, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080: <c>POST /v2/sys/identity/ssh-security-key/self</c>.</summary>
    public async Task WriteSelfAsync(SshSecurityKeySpec spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        await WriteAtAsync(SelfPath, spec, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080: <c>DELETE /v2/sys/identity/ssh-security-key/self</c>.</summary>
    public async Task DeleteSelfAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        await DeleteAtAsync(SelfPath, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080 (admin): <c>GET /v2/sys/identity/ssh-security-key/{mount}/{name}</c>.</summary>
    public async Task<SshSecurityKey?> ReadAsync(string mount, string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await ReadAtAsync(IdentityWire.MountAndName(Root, mount, name), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080 (admin): <c>POST /v2/sys/identity/ssh-security-key/{mount}/{name}</c>.</summary>
    public async Task WriteAsync(string mount, string name, SshSecurityKeySpec spec, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        await WriteAtAsync(IdentityWire.MountAndName(Root, mount, name), spec, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SYS-080 (admin): <c>DELETE /v2/sys/identity/ssh-security-key/{mount}/{name}</c>.</summary>
    public async Task DeleteAsync(string mount, string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        await DeleteAtAsync(IdentityWire.MountAndName(Root, mount, name), options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SshSecurityKey?> ReadAtAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        if (response?.Data is not { } data)
        {
            return null;
        }

        return new SshSecurityKey
        {
            Name = SysWire.ReadString(data, "name"),
            PublicKey = SysWire.ReadString(data, "public_key"),
            Fingerprint = SysWire.ReadString(data, "fingerprint"),
            Raw = response.Raw,
        };
    }

    private async Task WriteAtAsync(string path, SshSecurityKeySpec spec, RequestOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _ = await logical.ExecuteShapedAsync(
            "POST", path, Serialise(spec), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private async Task DeleteAtAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        _ = await logical.ExecuteShapedAsync(
            "DELETE", path, null, IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> Serialise(SshSecurityKeySpec spec)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        if (spec.Name is { } name)
        {
            writer.WriteString("name", name);
        }

        if (spec.PublicKey is { } publicKey)
        {
            writer.WriteString("public_key", publicKey);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }
}

/// <summary>SYS-080's namespace-assignment operations (the login restriction).</summary>
/// <remarks>
/// ⚠️ Appendix A line 66 marks this row's prefix <c>v1</c> while <c>06-system-api.md</c>'s own table
/// writes the route as <c>/v2/sys/identity/ns-assignment/…</c> and SYS-080 says <b>all</b>
/// <c>/v2/sys/identity/*</c> paths are pinned. The section that owns the requirement wins, exactly
/// as it does for the <c>Page&lt;Namespace&gt;</c> contradiction (D-M7-29): pinned to <c>/v2</c>,
/// with the contradiction recorded rather than resolved in code. See DR-0012 D-M7-28.
/// </remarks>
public sealed class NamespaceAssignmentOperations
{
    private const string Root = "sys/identity/ns-assignment";

    private readonly LogicalOperations logical;

    internal NamespaceAssignmentOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>SYS-080: <c>LIST /v2/sys/identity/ns-assignment</c> → <c>{"keys": [...]}</c>.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "LIST", Root, null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>SYS-080: <c>GET /v2/sys/identity/ns-assignment/{mount}/{name}</c>, or <see langword="null"/> when there is no assignment.</summary>
    public async Task<NamespaceAssignment?> ReadAsync(string mount, string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", IdentityWire.MountAndName(Root, mount, name), null, IdentityWire.PinV2(options),
            defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        if (response?.Data is not { } data)
        {
            return null;
        }

        return new NamespaceAssignment
        {
            Namespaces = SysWire.ReadStringArray(data, "namespaces"),
            DefaultNamespace = SysWire.ReadString(data, "default_namespace"),
            Raw = response.Raw,
        };
    }

    /// <summary>
    /// SYS-080: <c>POST /v2/sys/identity/ns-assignment/{mount}/{name}</c> with
    /// <c>{"namespaces": [...], "default_namespace"?}</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="namespaces"/> is always written, including when empty: this is a login
    /// <i>restriction</i>, so an empty list and an omitted key would differ in effect and only the
    /// list the caller passed is knowable here. <paramref name="defaultNamespace"/> is omitted when
    /// <see langword="null"/>.
    /// </remarks>
    public async Task WriteAsync(
        string mount,
        string name,
        IReadOnlyList<string> namespaces,
        string? defaultNamespace = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(namespaces);
        _ = await logical.ExecuteShapedAsync(
            "POST", IdentityWire.MountAndName(Root, mount, name), Serialise(namespaces, defaultNamespace), IdentityWire.PinV2(options),
            defaultIdempotent: false, treatNotFoundEmptyAsAbsent: false, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
    }

    private static ReadOnlyMemory<byte> Serialise(IReadOnlyList<string> namespaces, string? defaultNamespace)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer);
        writer.WriteStartObject();
        writer.WriteStartArray("namespaces");
        foreach (string value in namespaces)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
        if (defaultNamespace is not null)
        {
            writer.WriteString("default_namespace", defaultNamespace);
        }

        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenMemory;
    }
}
