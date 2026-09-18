using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>Identity.Profile.Read()</c>'s result (SYS-080): the calling token's own profile record.
/// </summary>
/// <remarks>
/// SYS-080's table says this route <b>never 404s</b>, so the operation returns a non-nullable
/// profile rather than a <c>Profile?</c>. Every named field is optional and <see cref="Raw"/>
/// carries the whole object: the specification names the route and its semantics but does not
/// enumerate the body, so naming a field as <c>required</c> here would be a guess about the wire
/// (D-M1c-25).
/// </remarks>
public sealed class IdentityProfile
{
    /// <summary>The wire <c>username</c> field.</summary>
    public string? Username { get; init; }

    /// <summary>The wire <c>display_name</c> field.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The wire <c>email</c> field. <c>""</c> is a cleared contact, not an absent one.</summary>
    public string? Email { get; init; }

    /// <summary>The wire <c>phone</c> field. <c>""</c> is a cleared contact, not an absent one.</summary>
    public string? Phone { get; init; }

    /// <summary>The wire <c>mount</c> field: the auth mount the identity belongs to.</summary>
    public string? Mount { get; init; }

    /// <summary>The whole profile object as sent, so a field this type does not name is still reachable.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>
/// The default-account record (SYS-080), as read from <c>sys/identity/default-account/*</c>.
/// </summary>
public sealed class DefaultAccount
{
    /// <summary>The wire <c>username</c> field.</summary>
    public string? Username { get; init; }

    /// <summary>The wire <c>domain</c> field.</summary>
    public string? Domain { get; init; }

    /// <summary>
    /// The wire <c>windows_password</c> field, wrapped so it never reaches a log through
    /// <c>ToString()</c> (CFG-080, DR-0003's <see cref="SecretString"/>).
    /// </summary>
    /// <remarks>
    /// ⚠️ SYS-080: the server returns this <b>only on a GET by the record's own owner</b>. It is
    /// therefore <see langword="null"/> on every admin read and on every write response, and an
    /// SDK that defaulted it to an empty string would make "the server withheld it" look like
    /// "the account has no password".
    /// </remarks>
    public SecretString? WindowsPassword { get; init; }

    /// <summary>The whole record as sent.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>The body of a default-account write (SYS-080).</summary>
public sealed class DefaultAccountSpec
{
    /// <summary>The wire <c>username</c> field; omitted from the body when unset.</summary>
    public string? Username { get; init; }

    /// <summary>The wire <c>domain</c> field; omitted from the body when unset.</summary>
    public string? Domain { get; init; }

    /// <summary>The wire <c>windows_password</c> field; omitted from the body when unset.</summary>
    public SecretString? WindowsPassword { get; init; }
}

/// <summary>One SSH security key registered against an identity (SYS-080).</summary>
public sealed class SshSecurityKey
{
    /// <summary>The wire <c>name</c> field.</summary>
    public string? Name { get; init; }

    /// <summary>The wire <c>public_key</c> field.</summary>
    public string? PublicKey { get; init; }

    /// <summary>The wire <c>fingerprint</c> field.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>The whole record as sent.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>The body of an SSH security-key write (SYS-080).</summary>
public sealed class SshSecurityKeySpec
{
    /// <summary>The wire <c>name</c> field; omitted when unset.</summary>
    public string? Name { get; init; }

    /// <summary>The wire <c>public_key</c> field; omitted when unset.</summary>
    public string? PublicKey { get; init; }
}

/// <summary>
/// An identity's namespace assignment (SYS-080): the login restriction that binds a principal to a
/// set of namespaces.
/// </summary>
public sealed class NamespaceAssignment
{
    /// <summary>The wire <c>namespaces</c> array; empty when the server omits it.</summary>
    public required IReadOnlyList<string> Namespaces { get; init; }

    /// <summary>The wire <c>default_namespace</c> field.</summary>
    public string? DefaultNamespace { get; init; }

    /// <summary>The whole record as sent.</summary>
    public JsonElement Raw { get; init; }
}
