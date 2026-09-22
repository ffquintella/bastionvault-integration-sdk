using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// 12 §Files: <c>Files.Create</c>'s request. FIL-001: <see cref="Content"/> is raw bytes; the SDK
/// base64-encodes it into the wire's <c>content_base64</c> field itself, never exposing that
/// encoding to the caller.
/// </summary>
public sealed class FileCreateRequest
{
    /// <summary>The wire <c>name</c> field.</summary>
    public required string Name { get; init; }

    /// <summary>The wire <c>resource</c> field; omitted from the body when unset.</summary>
    public string? Resource { get; init; }

    /// <summary>The wire <c>mime_type</c> field; omitted from the body when unset.</summary>
    public string? MimeType { get; init; }

    /// <summary>The wire <c>tags</c> array; omitted from the body when unset.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>The wire <c>notes</c> field; omitted from the body when unset.</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// FIL-001: base64-encoded by the SDK into <c>content_base64</c>; never a base64 string here.
    /// <see cref="ReadOnlyMemory{T}"/> rather than <c>byte[]</c> (a caller-supplied array converts
    /// implicitly), the same property shape <see cref="RawResponse.Body"/> already uses.
    /// </summary>
    public required ReadOnlyMemory<byte> Content { get; init; }
}

/// <summary>
/// 12 §Files: the update spec for <c>Files.Update</c>. Section 12 gives no field set beyond
/// <c>Files.Create</c>'s own, so the same fields are offered here, all optional (OVR-007).
/// </summary>
public sealed class FileUpdateRequest
{
    /// <summary>The wire <c>name</c> field; omitted from the body when unset.</summary>
    public string? Name { get; init; }

    /// <summary>The wire <c>resource</c> field; omitted from the body when unset.</summary>
    public string? Resource { get; init; }

    /// <summary>The wire <c>mime_type</c> field; omitted from the body when unset.</summary>
    public string? MimeType { get; init; }

    /// <summary>The wire <c>tags</c> array; omitted from the body when unset.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>The wire <c>notes</c> field; omitted from the body when unset.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// 12 §Files: <c>Files.Sync.Write</c>'s target. Section 12 names only <see cref="Kind"/> and says
/// credential fields exist without naming them (Level X). Rather than invent one (D-M1c-25), every
/// other field, credentials included, travels through <see cref="Fields"/> as raw JSON — a
/// recorded gap, not a guess.
/// </summary>
public sealed class SyncTarget
{
    /// <summary>The wire <c>kind</c> field: <c>local-fs</c> or <c>smb</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Every other field this target needs, exactly as the caller supplies it.</summary>
    public JsonElement? Fields { get; init; }
}
