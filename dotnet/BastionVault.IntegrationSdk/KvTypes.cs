using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// KV1-003: one KV v1 secret. Storage is the request body verbatim, so
/// <see cref="Data"/> is whatever was written.
/// </summary>
public sealed class KvV1Secret
{
    /// <summary>The stored object (the wire envelope's <c>data</c>).</summary>
    public required IReadOnlyDictionary<string, JsonElement> Data { get; init; }

    /// <summary>The wire's <c>lease_duration</c> in seconds, defaulted to 3600 by the server (07 §KV v1).</summary>
    public required TimeSpan LeaseDuration { get; init; }

    /// <summary>The wire's <c>renewable</c>, true when a <c>ttl</c>/<c>lease</c> field was stored.</summary>
    public required bool Renewable { get; init; }
}

/// <summary>
/// KV2-004's derived state of one KV v2 version. Derived from the wire, never sent: a version is
/// <see cref="SoftDeleted"/> exactly when the server returned <c>200</c> with <c>data.data == null</c>
/// and a <c>deletion_time</c>.
/// </summary>
public enum KvV2SecretState
{
    /// <summary>The version has data.</summary>
    Live,

    /// <summary>The version is soft-deleted: recoverable with <c>Kv.V2.Undelete</c>.</summary>
    SoftDeleted,
}

/// <summary>
/// KV2-011, KV2-020: the per-version metadata a KV v2 read or write returns.
/// </summary>
public sealed class KvV2VersionMetadata
{
    /// <summary>The version number.</summary>
    public required int Version { get; init; }

    /// <summary>When this version was created.</summary>
    public required DateTimeOffset CreatedTime { get; init; }

    /// <summary>When this version was soft-deleted, or <see langword="null"/> when it is live (the wire spells "live" as <c>""</c>).</summary>
    public DateTimeOffset? DeletionTime { get; init; }

    /// <summary>Whether this version was permanently destroyed.</summary>
    public required bool Destroyed { get; init; }

    /// <summary>
    /// KV2-011: a BastionVault extension, optional. Absent on a server that does not record it.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// KV2-011: a BastionVault extension, optional. A <see langword="string"/> and not an enum
    /// (D-M4-5): the value set (<c>create</c>, <c>update</c>, <c>restore</c>) is the server's, and
    /// an enum would need an invented member for a value a later server adds (D-M1c-25).
    /// </summary>
    public string? Operation { get; init; }

    /// <summary>
    /// KV2-020: the environment the read resolved to, or <see langword="null"/> when none was
    /// requested or the secret declares no <c>envs</c> (07 §Environments).
    /// </summary>
    public string? ResolvedEnv { get; init; }

    /// <summary>KV2-020: the environments this secret declares overrides for; empty when it declares none.</summary>
    public IReadOnlyList<string> AvailableEnvs { get; init; } = Array.Empty<string>();
}

/// <summary>One version of a KV v2 secret (07 §Types).</summary>
public sealed class KvV2Secret
{
    /// <summary>
    /// The merged secret data, or <see langword="null"/> when this version is soft-deleted
    /// (KV2-004). With <c>env</c> given, this is <c>merge(base, envs[env])</c> — the server
    /// performs the merge, the SDK never does (KV2-021).
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Data { get; init; }

    /// <summary>This version's metadata.</summary>
    public required KvV2VersionMetadata Metadata { get; init; }

    /// <summary>KV2-004's derived state.</summary>
    public required KvV2SecretState State { get; init; }
}

/// <summary>KV2-010, KV2-011: the whole-secret metadata behind <c>metadata/{path}</c>.</summary>
public sealed class KvV2Metadata
{
    /// <summary>The newest version number.</summary>
    public required int CurrentVersion { get; init; }

    /// <summary>The oldest version still retained.</summary>
    public required int OldestVersion { get; init; }

    /// <summary>The engine's retained-version cap that applies to this secret.</summary>
    public required int MaxVersions { get; init; }

    /// <summary>Whether check-and-set is required for writes.</summary>
    public required bool CasRequired { get; init; }

    /// <summary>
    /// KV2-010: a Go-style duration string, <c>"0s"</c> when version expiry is disabled. Carried
    /// as the wire's string (D-M4-5 pins 07 §Types' <c>DeleteVersionAfter: string</c>).
    /// </summary>
    public required string DeleteVersionAfter { get; init; }

    /// <summary>When the secret was first written.</summary>
    public required DateTimeOffset CreatedTime { get; init; }

    /// <summary>When the secret was last written.</summary>
    public required DateTimeOffset UpdatedTime { get; init; }

    /// <summary>Every retained version, keyed by version number.</summary>
    public required IReadOnlyDictionary<int, KvV2VersionMetadata> Versions { get; init; }
}

/// <summary>
/// The KV v2 engine configuration behind <c>config</c> (KV2-009, KV2-024).
/// </summary>
public sealed class KvV2Config
{
    /// <summary>The number of versions the engine retains per secret.</summary>
    public required int MaxVersions { get; init; }

    /// <summary>Whether the engine requires check-and-set on every write.</summary>
    public required bool CasRequired { get; init; }

    /// <summary>KV2-010: a Go-style duration string; <c>"0s"</c> disables version expiry.</summary>
    public required string DeleteVersionAfter { get; init; }

    /// <summary>
    /// KV2-010: the parsed view of <see cref="DeleteVersionAfter"/>, or <see langword="null"/>
    /// when the string is absent or is not a duration the SDK can parse. <c>"0s"</c> parses to
    /// <see cref="TimeSpan.Zero"/>, KV2-010's "disabled".
    /// </summary>
    public TimeSpan? DeleteVersionAfterDuration => GoDuration.TryParse(DeleteVersionAfter);

    /// <summary>
    /// KV2-024: the advisory environment registry. The SDK never validates an <c>env</c> argument
    /// against it.
    /// </summary>
    public IReadOnlyList<string> Environments { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The patch <c>Kv.V2.UpdateConfig</c> merges into the current configuration (KV2-009, D-M4-6).
/// Every field is nullable and <see langword="null"/> means "leave alone", which is the only way a
/// patch can distinguish "unset" from "set to the default".
/// </summary>
public sealed class KvV2ConfigPatch
{
    /// <summary>The new retained-version cap, or <see langword="null"/> to keep the current one.</summary>
    public int? MaxVersions { get; init; }

    /// <summary>The new check-and-set requirement, or <see langword="null"/> to keep the current one.</summary>
    public bool? CasRequired { get; init; }

    /// <summary>The new version-expiry duration string, or <see langword="null"/> to keep the current one.</summary>
    public string? DeleteVersionAfter { get; init; }

    /// <summary>
    /// KV2-010: the new version-expiry duration, formatted Go-style and sent as
    /// <see cref="DeleteVersionAfter"/> when set. Setting both this and <see cref="DeleteVersionAfter"/>
    /// to conflicting values is a caller error, rejected client-side as <c>BV-INPUT-001</c>.
    /// </summary>
    public TimeSpan? DeleteVersionAfterDuration { get; init; }

    /// <summary>The new environment registry, or <see langword="null"/> to keep the current one.</summary>
    public IReadOnlyList<string>? Environments { get; init; }
}

/// <summary>
/// 07 §Types' <c>WriteOptions</c>, named <c>KvWriteOptions</c> (D-M4-5) because a bare
/// <c>WriteOptions</c> beside <see cref="RequestOptions"/> and <see cref="LoginOptions"/> reads as
/// transport-level rather than KV-scoped.
/// </summary>
public sealed class KvWriteOptions
{
    /// <summary>
    /// KV2-003's check-and-set version. <c>0</c> means "must not exist yet" and is sent as
    /// <c>0</c>, never omitted.
    /// </summary>
    public int? Cas { get; init; }

    /// <summary>
    /// KV2-002: the single environment this write targets. Mutually exclusive with
    /// <see cref="Envs"/>, and may contain neither <c>/</c> nor a control character.
    /// </summary>
    public string? Env { get; init; }

    /// <summary>
    /// KV2-021: the full per-environment override map for a multi-environment replace. Mutually
    /// exclusive with <see cref="Env"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? Envs { get; init; }
}
