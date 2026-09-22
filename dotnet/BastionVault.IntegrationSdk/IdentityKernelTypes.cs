using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary><c>Identity.Self()</c>'s result (12): <c>GET identity/entity/self</c>, which lazily provisions the entity and never 404s.</summary>
/// <remarks>
/// Every field is nullable and <see cref="Raw"/> carries the whole object: section 12 gives a
/// field list, not a captured wire body (DR-0017 D-M10-4, R-35 — no fixture for this route).
/// </remarks>
public sealed class EntitySelf
{
    /// <summary>The wire <c>entity_id</c> field.</summary>
    public string? EntityId { get; init; }

    /// <summary>The wire <c>username</c> field.</summary>
    public string? Username { get; init; }

    /// <summary>The wire <c>mount_path</c> field.</summary>
    public string? MountPath { get; init; }

    /// <summary>The wire <c>role_name</c> field.</summary>
    public string? RoleName { get; init; }

    /// <summary>The wire <c>primary_mount</c> field.</summary>
    public string? PrimaryMount { get; init; }

    /// <summary>The wire <c>primary_name</c> field.</summary>
    public string? PrimaryName { get; init; }

    /// <summary>The wire <c>created_at</c> field, RFC 3339.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>The wire <c>aliases</c> array; each entry a raw element, since 12 names no field inside one.</summary>
    public IReadOnlyList<JsonElement>? Aliases { get; init; }

    /// <summary>The whole object as sent.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>One <c>Identity.Groups</c> record (<c>kind ∈ user | app</c>), the field list section 12 gives.</summary>
public sealed class IdentityGroup
{
    /// <summary>The wire <c>description</c> field.</summary>
    public string? Description { get; init; }

    /// <summary>The wire <c>members</c> array; empty when the server omits it.</summary>
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();

    /// <summary>The wire <c>policies</c> array; empty when the server omits it.</summary>
    public IReadOnlyList<string> Policies { get; init; } = Array.Empty<string>();

    /// <summary>The whole record as sent.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>The body of an <c>Identity.Groups.Write</c> (12): every member omitted from the request when unset.</summary>
public sealed class IdentityGroupSpec
{
    /// <summary>The wire <c>description</c> field; omitted when unset.</summary>
    public string? Description { get; init; }

    /// <summary>The wire <c>members</c> array; omitted when unset.</summary>
    public IReadOnlyList<string>? Members { get; init; }

    /// <summary>The wire <c>policies</c> array; omitted when unset.</summary>
    public IReadOnlyList<string>? Policies { get; init; }
}

/// <summary>The body of <c>Identity.Sharing.Put</c> (IDN-001); <c>target_kind</c>/<c>target_path</c> come from the call's own arguments.</summary>
public sealed class IdentitySharingSpec
{
    /// <summary>The wire <c>grantee_kind</c> field (<c>entity</c> default | <c>group_user</c> | <c>group_app</c>); omitted when unset.</summary>
    public string? GranteeKind { get; init; }

    /// <summary>The wire <c>capabilities</c> array; omitted from the body when unset.</summary>
    public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>The wire <c>expires_at</c> field, RFC 3339; omitted from the body when unset.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary><c>Identity.Sharing.ForMe()</c>'s result (IDN-002): <c>{entity_id, group_shared_resources, entries[]}</c>.</summary>
/// <remarks>
/// IDN-002: a group share appears in <see cref="Entries"/> only when the policy behind it carries
/// <c>metadata.group_shared_resources = "true"</c> server-side; there is nothing client-side to
/// enforce, since the filtering happens before the response is built.
/// </remarks>
public sealed class IdentitySharingForMe
{
    /// <summary>The wire <c>entity_id</c> field.</summary>
    public string? EntityId { get; init; }

    /// <summary>The wire <c>group_shared_resources</c> field.</summary>
    public bool? GroupSharedResources { get; init; }

    /// <summary>The wire <c>entries</c> array; each entry a raw element, since 12 names no field inside one.</summary>
    public IReadOnlyList<JsonElement> Entries { get; init; } = Array.Empty<JsonElement>();

    /// <summary>The whole object as sent.</summary>
    public JsonElement Raw { get; init; }
}
