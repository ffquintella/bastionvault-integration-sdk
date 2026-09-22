using System.Globalization;
using System.Text;
using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// KV v2 (KV-002's version-explicit <c>Kv.V2</c>): versions, check-and-set, soft delete, destroy,
/// metadata, per-environment overrides and the KV2-030 path helpers. Reached from
/// <see cref="KvOperations.V2"/>.
/// </summary>
/// <remarks>
/// <para>
/// The route groups are 07 §KV v2's exactly, including the three deliberate differences from
/// HashiCorp KV v2 that section mandates the SDK encode: there is <b>no</b> <c>delete/{path}</c>
/// route (a soft delete is <c>DELETE data/{path}</c>), <c>metadata/{path}</c> has <b>no write</b>,
/// and there is no <c>subkeys/</c> and no <c>patch</c>. Those are absences by requirement.
/// </para>
/// <para>
/// Parameter order is D-M4-4's, as for <see cref="KvV1Operations"/>.
/// </para>
/// </remarks>
public sealed class KvV2Operations
{
    private const string DefaultMount = "secret";
    private const string DataGroup = "data";
    private const string MetadataGroup = "metadata";
    private const string DestroyGroup = "destroy";
    private const string UndeleteGroup = "undelete";
    private const string ConfigGroup = "config";

    private readonly ClientContext context;
    private readonly LogicalOperations logical;

    internal KvV2Operations(ClientContext context, string activeNamespace)
    {
        this.context = context;
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>KV2-030: the logical <c>{mount}/data/{path}</c> path, for <c>Sys.Batch</c> and policy authoring.</summary>
    /// <remarks>No HTTP call — a client-side string helper. Never <see langword="null"/>. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only <c>BV-INPUT-001</c> for an unsafe <c>mount</c>/<c>path</c>.</remarks>
    /// <spec>Kv.V2.DataPath — KV2-030</spec>
    public static string DataPath(string path, string mount = DefaultMount)
    {
        return Helper(mount, DataGroup, path);
    }

    /// <summary>KV2-030: the logical <c>{mount}/metadata/{path}</c> path.</summary>
    /// <remarks>No HTTP call — a client-side string helper. Never <see langword="null"/>. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only <c>BV-INPUT-001</c> for an unsafe <c>mount</c>/<c>path</c>.</remarks>
    /// <spec>Kv.V2.MetadataPath — KV2-030</spec>
    public static string MetadataPath(string path, string mount = DefaultMount)
    {
        return Helper(mount, MetadataGroup, path);
    }

    /// <summary>KV2-030: the logical <c>{mount}/destroy/{path}</c> path.</summary>
    /// <remarks>No HTTP call — a client-side string helper. Never <see langword="null"/>. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only <c>BV-INPUT-001</c> for an unsafe <c>mount</c>/<c>path</c>.</remarks>
    /// <spec>Kv.V2.DestroyPath — KV2-030</spec>
    public static string DestroyPath(string path, string mount = DefaultMount)
    {
        return Helper(mount, DestroyGroup, path);
    }

    /// <summary>KV2-030: the logical <c>{mount}/undelete/{path}</c> path.</summary>
    /// <remarks>No HTTP call — a client-side string helper. Never <see langword="null"/>. Conformance: Core (KV2-030). No error codes beyond the common set (ERR-061); throws only <c>BV-INPUT-001</c> for an unsafe <c>mount</c>/<c>path</c>.</remarks>
    /// <spec>Kv.V2.UndeletePath — KV2-030</spec>
    public static string UndeletePath(string path, string mount = DefaultMount)
    {
        return Helper(mount, UndeleteGroup, path);
    }

    /// <summary>
    /// KV2-001, KV2-004, KV2-006, KV2-020: <c>GET {mount}/data/{path}?version=N&amp;env=E</c>.
    /// </summary>
    /// <remarks>
    /// Both selectors travel as query parameters and never in a body (KV2-001); a
    /// <paramref name="version"/> of <c>0</c> or <see langword="null"/> means "latest" and is
    /// omitted. A <c>404</c> with an empty body is <see langword="null"/> — including when
    /// <paramref name="env"/> was given, which is KV2-006's strict-miss row — and this method never
    /// performs the extra metadata read that KV2-006 permits only to <see cref="GetSecretAsync"/>.
    /// A soft-deleted version comes back as a <see cref="KvV2Secret"/> with no
    /// <see cref="KvV2Secret.Data"/> and <see cref="KvV2SecretState.SoftDeleted"/>, never as
    /// <see langword="null"/> and never as an error (KV2-004).
    /// </remarks>
    /// <remarks>Wire params: <c>path</c>/<c>mount</c> build the route; <c>version</c> and <c>env</c> are query params. Conformance: Core (KV2-001, KV2-004, KV2-006, KV2-020). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V2.ReadSecret — KV2-001</spec>
    public async Task<KvV2Secret?> ReadSecretAsync(
        string path,
        string mount = DefaultMount,
        int? version = null,
        string? env = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string logicalPath = GuardDataOperation(path, mount, env);
        Response? response = await logical.ExecuteShapedAsync(
            "GET",
            EncodedDataRoute(mount, path, version, env),
            null,
            options,
            defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);

        if (response?.Data is not { } data)
        {
            return null;
        }

        IReadOnlyDictionary<string, JsonElement>? secretData = KvWire.ReadDataMap(data, "data");
        KvV2VersionMetadata metadata = data.TryGetValue("metadata", out JsonElement metadataElement)
            ? KvWire.ReadVersionMetadata(KvWire.AsMap(metadataElement), logicalPath)
            : throw KvWire.EnvelopeMismatch(logicalPath, "data.metadata");

        return new KvV2Secret
        {
            Data = secretData,
            Metadata = metadata,
            // KV2-004's derived rule, verbatim: no data *and* a deletion time.
            State = secretData is null && metadata.DeletionTime is not null
                ? KvV2SecretState.SoftDeleted
                : KvV2SecretState.Live,
        };
    }

    /// <summary>
    /// KV2-004, KV2-006: <see cref="ReadSecretAsync"/> with the three absences turned into errors.
    /// </summary>
    /// <remarks>
    /// A soft-deleted version raises <c>BV-KV-007 VersionSoftDeleted</c>. A <c>404</c> with an
    /// empty body raises <c>BV-KV-001 SecretNotFound</c>, except that when
    /// <paramref name="env"/> was given this method performs one extra <see cref="ReadMetadataAsync"/>
    /// — the read KV2-006 permits <i>only</i> here and <i>only</i> then — and raises
    /// <c>BV-KV-006 EnvironmentNotDeclared</c> when it shows the secret exists. "If permitted" is
    /// read as written: a <c>403</c> on the metadata read means the SDK cannot tell the two apart,
    /// so it falls back to <c>BV-KV-001</c> rather than guessing.
    /// </remarks>
    /// <remarks>Wire params as <see cref="ReadSecretAsync"/>. Never returns <see langword="null"/>. Conformance: Core (KV2-004, KV2-006). Errors beyond the common set (ERR-061): <c>BV-KV-007 VersionSoftDeleted</c>, <c>BV-KV-001 SecretNotFound</c>, <c>BV-KV-006 EnvironmentNotDeclared</c>.</remarks>
    /// <spec>Kv.V2.ReadSecret — KV2-004</spec>
    public async Task<KvV2Secret> GetSecretAsync(
        string path,
        string mount = DefaultMount,
        int? version = null,
        string? env = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        KvV2Secret? secret = await ReadSecretAsync(path, mount, version, env, options, cancellationToken).ConfigureAwait(false);
        string logicalPath = DataPath(path, mount);

        if (secret is null)
        {
            if (!string.IsNullOrEmpty(env)
                && await SecretExistsAsync(path, mount, options, cancellationToken).ConfigureAwait(false))
            {
                throw KvWire.Engine(
                    ErrorCodes.KvEnvironmentNotDeclared,
                    logicalPath,
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["env"] = env });
            }

            throw KvWire.Engine(ErrorCodes.KvSecretNotFound, logicalPath);
        }

        if (secret.State == KvV2SecretState.SoftDeleted)
        {
            throw KvWire.Engine(
                ErrorCodes.KvVersionSoftDeleted,
                logicalPath,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["version"] = secret.Metadata.Version });
        }

        return secret;
    }

    /// <summary>
    /// KV2-002, KV2-003: <c>POST {mount}/data/{path}</c> with body
    /// <c>{"data": {…}, "options": {"cas": N}?, "env": E?, "envs": {…}?}</c>.
    /// </summary>
    /// <remarks>
    /// Four client-side refusals, all before any request is sent and all <c>BV-INPUT-001</c>
    /// (KV2-002): an empty or absent <paramref name="data"/>, both <c>Env</c> and <c>Envs</c> set,
    /// an <c>Env</c> containing <c>/</c> or a control character, and — RF-3, the same rule applied
    /// to every key of <c>Envs</c>, not only to <c>Env</c>, since KV2-002's environment-name rule
    /// applies wherever a name appears on the wire — an <c>Envs</c> key containing <c>/</c> or a
    /// control character. <c>Cas = 0</c> is sent as <c>0</c> rather than omitted, because KV2-003
    /// gives it the distinct meaning "must not exist yet"; <c>Cas = null</c> omits the whole
    /// <c>options</c> object.
    /// </remarks>
    /// <remarks>Wire body fields: <c>data</c>, <c>options.cas</c>, <c>env</c>, <c>envs</c>. Never returns <see langword="null"/>. Conformance: Core (KV2-002, KV2-003). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c>, <c>BV-KV-003 CasMismatch</c>, <c>BV-KV-009 EnvironmentRequired</c>.</remarks>
    /// <spec>Kv.V2.WriteSecret — KV2-002</spec>
    public async Task<KvV2VersionMetadata> WriteSecretAsync(
        string path,
        IReadOnlyDictionary<string, JsonElement> data,
        string mount = DefaultMount,
        KvWriteOptions? options = null,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string logicalPath = DataPath(path, mount);
        KvWire.RequireSafePath(path, "path", logicalPath);

        if (options is { Env: not null, Envs: not null })
        {
            throw KvWire.InvalidArgument("options", "`Env` and `Envs` are mutually exclusive (KV2-002)", logicalPath);
        }

        KvWire.RequireData(data, "data", logicalPath);
        string? env = ValidateEnv(options?.Env, logicalPath);
        if (options?.Envs is { } envs)
        {
            // RF-3: WriteAllEnvironmentsAsync already validated every `envs` key; WriteSecretAsync
            // did not, which was two client-side contracts for one wire shape. No `?? throw` arm,
            // matching WriteAllEnvironmentsAsync's own loop: ValidateEnv returns null only for a
            // null input, and a dictionary key cannot be null.
            foreach (string name in envs.Keys)
            {
                _ = ValidateEnv(name, logicalPath);
            }
        }

        GuardEnvironmentScope(env, logicalPath);

        return await SendWriteAsync(logicalPath, mount, path, data, options?.Cas, env, options?.Envs, requestOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// KV2-021: a targeted single-environment patch. Sends the same write with <c>env</c> set; the
    /// server carries the base set and the other environments forward, so the SDK performs
    /// <b>no</b> read-merge-write for this.
    /// </summary>
    /// <remarks>Wire body: <c>data</c> (from <c>overrides</c>), <c>env</c>, <c>options.cas</c>. Never returns <see langword="null"/>. Conformance: Core (KV2-021). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c>, <c>BV-KV-003 CasMismatch</c>.</remarks>
    /// <spec>Kv.V2.WriteSecret — KV2-021</spec>
    public async Task<KvV2VersionMetadata> PatchEnvironmentAsync(
        string path,
        string env,
        IReadOnlyDictionary<string, JsonElement> overrides,
        string mount = DefaultMount,
        int? cas = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string logicalPath = DataPath(path, mount);
        KvWire.RequireSafePath(path, "path", logicalPath);
        KvWire.RequireData(overrides, "overrides", logicalPath);
        string validated = ValidateEnv(env, logicalPath)
            ?? throw KvWire.InvalidArgument("env", "must be non-empty", logicalPath);

        return await SendWriteAsync(logicalPath, mount, path, overrides, cas, validated, null, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// KV2-021: a full multi-environment replace. Sends the base set as <c>data</c> and the whole
    /// override map as <c>envs</c>; again no client-side read-merge-write.
    /// </summary>
    /// <remarks>Wire body: <c>data</c> (from <c>baseData</c>), <c>envs</c>, <c>options.cas</c>. Never returns <see langword="null"/>. Conformance: Core (KV2-021, KV2-022). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c>, <c>BV-KV-003 CasMismatch</c>.</remarks>
    /// <spec>Kv.V2.WriteSecret — KV2-021</spec>
    public async Task<KvV2VersionMetadata> WriteAllEnvironmentsAsync(
        string path,
        IReadOnlyDictionary<string, JsonElement> baseData,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> envs,
        string mount = DefaultMount,
        int? cas = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(envs);
        string logicalPath = DataPath(path, mount);
        KvWire.RequireSafePath(path, "path", logicalPath);
        KvWire.RequireData(baseData, "baseData", logicalPath);
        foreach (string name in envs.Keys)
        {
            // No `?? throw` arm: ValidateEnv returns null only for a null input, and a dictionary
            // key cannot be null, so an arm for it would be dead code (D-M1c-25).
            _ = ValidateEnv(name, logicalPath);
        }

        // KV2-022 as D-M4-7 states it: the check is "a v2 data operation with no `env`", and a
        // multi-environment replace supplies `envs`, not `env`. The server answers 403 for this
        // shape too, so failing fast is the same answer one round trip earlier.
        GuardEnvironmentScope(null, logicalPath);

        return await SendWriteAsync(logicalPath, mount, path, baseData, cas, null, envs, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// KV2-004: <c>DELETE {mount}/data/{path}</c>, the <b>soft</b> delete — 07 mandates that no
    /// <c>delete/{path}</c> route exists. With no <paramref name="versions"/> the server deletes the
    /// latest version only and no body is sent.
    /// </summary>
    /// <remarks>
    /// An explicitly supplied <i>empty</i> list is refused with <c>BV-INPUT-002</c> rather than
    /// silently treated as "no body": the two mean different things to the caller, and the silent
    /// reading would widen a delete the caller narrowed. KV2-007 states this for
    /// <see cref="UndeleteAsync"/> and <see cref="DestroyAsync"/>; applying it to an explicit empty
    /// list here is ruled in DR-0009's addendum, D-M4-10.
    /// </remarks>
    /// <remarks>Wire: <c>versions</c> array in the body, omitted when <see langword="null"/>. Returns nothing. Conformance: Core (KV2-004). Errors beyond the common set (ERR-061): <c>BV-INPUT-002</c> for an explicit empty <c>versions</c> list.</remarks>
    /// <spec>Kv.V2.SoftDelete — KV2-004</spec>
    public async Task SoftDeleteAsync(
        string path,
        string mount = DefaultMount,
        IReadOnlyList<int>? versions = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string logicalPath = DataPath(path, mount);
        KvWire.RequireSafePath(path, "path", logicalPath);

        ReadOnlyMemory<byte>? body = null;
        if (versions is not null)
        {
            KvWire.RequireVersions(versions, logicalPath);
            body = KvWire.Serialise(writer => WriteVersions(writer, versions));
        }

        _ = await logical.ExecuteShapedAsync(
            "DELETE",
            KvWire.EncodedRoute(mount, DataGroup, path),
            body,
            options,
            defaultIdempotent: false,
            treatNotFoundEmptyAsAbsent: false,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>KV2-007: <c>POST {mount}/undelete/{path}</c>. A non-empty version list is required (<c>BV-INPUT-002</c>).</summary>
    /// <remarks>Wire: <c>versions</c> array in the body. Returns nothing. Conformance: Core (KV2-007). Errors beyond the common set (ERR-061): <c>BV-INPUT-002</c>.</remarks>
    /// <spec>Kv.V2.Undelete — KV2-007</spec>
    public async Task UndeleteAsync(
        string path,
        IReadOnlyList<int> versions,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await SendVersionsAsync(UndeleteGroup, path, versions, mount, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// KV2-005, KV2-007: <c>POST {mount}/destroy/{path}</c>. Irreversible; a non-empty version list
    /// is required (<c>BV-INPUT-002</c>).
    /// </summary>
    /// <remarks>Wire: <c>versions</c> array in the body. Returns nothing. Conformance: Core (KV2-005, KV2-007). Errors beyond the common set (ERR-061): <c>BV-INPUT-002</c>.</remarks>
    /// <spec>Kv.V2.Destroy — KV2-005</spec>
    public async Task DestroyAsync(
        string path,
        IReadOnlyList<int> versions,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await SendVersionsAsync(DestroyGroup, path, versions, mount, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// KV2-010, KV2-011: <c>GET {mount}/metadata/{path}</c>. <see langword="null"/> on a <c>404</c>
    /// with an empty body.
    /// </summary>
    /// <remarks>Wire params: <c>path</c>/<c>mount</c> build the route. Conformance: Core (KV2-010, KV2-011). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V2.ReadMetadata — KV2-010</spec>
    public async Task<KvV2Metadata?> ReadMetadataAsync(
        string path,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string logicalPath = MetadataPath(path, mount);
        KvWire.RequireSafePath(path, "path", logicalPath);

        Response? response = await logical.ExecuteShapedAsync(
            "GET",
            KvWire.EncodedRoute(mount, MetadataGroup, path),
            null,
            options,
            defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);

        if (response?.Data is not { } data)
        {
            return null;
        }

        Dictionary<int, KvV2VersionMetadata> versions = [];
        if (data.TryGetValue("versions", out JsonElement versionsElement) && versionsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in versionsElement.EnumerateObject())
            {
                // The wire keys the map by the version number as a JSON property name, i.e. a
                // string. A name that is not an integer is a shape 07 does not describe, so it is
                // skipped rather than mapped onto an invented key.
                if (int.TryParse(property.Name, CultureInfo.InvariantCulture, out int number))
                {
                    versions[number] = KvWire.ReadVersionMetadata(KvWire.AsMap(property.Value), logicalPath);
                }
            }
        }

        return new KvV2Metadata
        {
            CurrentVersion = KvWire.ReadInt(data, "current_version") ?? 0,
            OldestVersion = KvWire.ReadInt(data, "oldest_version") ?? 0,
            MaxVersions = KvWire.ReadInt(data, "max_versions") ?? 0,
            CasRequired = KvWire.ReadBool(data, "cas_required"),
            // KV2-010 names "0s" as the disabled spelling, so an absent field is reported as
            // disabled in the specification's own words rather than as an empty string.
            DeleteVersionAfter = KvWire.ReadString(data, "delete_version_after") ?? "0s",
            CreatedTime = KvWire.RequireInstant(data, "created_time", logicalPath),
            UpdatedTime = KvWire.RequireInstant(data, "updated_time", logicalPath),
            Versions = versions,
        };
    }

    /// <summary>KV-002: <c>DELETE {mount}/metadata/{path}</c> — removes <b>all</b> versions permanently.</summary>
    /// <remarks>Returns nothing. Conformance: Core (KV2-011). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V2.DeleteMetadata — KV2-011</spec>
    public async Task DeleteMetadataAsync(
        string path,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        KvWire.RequireSafePath(path, "path", MetadataPath(path, mount));
        _ = await logical.ExecuteShapedAsync(
            "DELETE",
            KvWire.EncodedRoute(mount, MetadataGroup, path),
            null,
            options,
            defaultIdempotent: false,
            treatNotFoundEmptyAsAbsent: false,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// KV2-008: <c>LIST {mount}/metadata/</c> or <c>LIST {mount}/metadata/{prefix}/</c>. The
    /// trailing slash is mandatory; a prefix containing <c>..</c> is refused with
    /// <c>BV-INPUT-001</c>; a <c>404</c> with an empty body is an empty list.
    /// </summary>
    /// <remarks>Never returns <see langword="null"/>. Conformance: Core (KV2-008). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c>.</remarks>
    /// <spec>Kv.V2.List — KV2-008</spec>
    public async Task<IReadOnlyList<string>> ListAsync(
        string prefix = "",
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        KvWire.RequireSafePrefix(prefix, MetadataPath(prefix, mount));
        Response? response = await logical.ExecuteShapedAsync(
            "LIST",
            KvWire.EncodedListRoute(mount, MetadataGroup, prefix),
            null,
            options,
            defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
        return KvWire.ReadKeys(response);
    }

    /// <summary>KV2-009, KV2-024: <c>GET {mount}/config</c>. <see langword="null"/> when the mount answers a <c>404</c> with an empty body.</summary>
    /// <remarks>Conformance: Core (KV2-009, KV2-024). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V2.ReadConfig — KV2-009</spec>
    public async Task<KvV2Config?> ReadConfigAsync(
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        Response? response = await logical.ExecuteShapedAsync(
            "GET",
            ConfigRoute(mount),
            null,
            options,
            defaultIdempotent: true,
            treatNotFoundEmptyAsAbsent: true,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);

        if (response?.Data is not { } data)
        {
            return null;
        }

        return new KvV2Config
        {
            MaxVersions = KvWire.ReadInt(data, "max_versions") ?? 0,
            CasRequired = KvWire.ReadBool(data, "cas_required"),
            DeleteVersionAfter = KvWire.ReadString(data, "delete_version_after") ?? "0s",
            Environments = KvWire.ReadStringList(data, "environments"),
        };
    }

    /// <summary>
    /// KV2-009: <c>POST {mount}/config</c>. <b>Replaces</b> the whole configuration — all four
    /// fields are sent, because the server rewrites the full config and an omitted
    /// <c>environments</c> would therefore clear the registry rather than leave it alone. Use
    /// <see cref="UpdateConfigAsync"/> to change one field.
    /// </summary>
    /// <remarks>Wire body: <c>max_versions</c>, <c>cas_required</c>, <c>delete_version_after</c>, <c>environments</c> (all sent). Returns nothing. Conformance: Core (KV2-009). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.V2.WriteConfig — KV2-009</spec>
    public async Task WriteConfigAsync(
        KvV2Config config,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            writer.WriteNumber("max_versions", config.MaxVersions);
            writer.WriteBoolean("cas_required", config.CasRequired);
            writer.WriteString("delete_version_after", config.DeleteVersionAfter);
            writer.WriteStartArray("environments");
            foreach (string environment in config.Environments)
            {
                writer.WriteStringValue(environment);
            }

            writer.WriteEndArray();
        });

        _ = await logical.ExecuteShapedAsync(
            "POST",
            ConfigRoute(mount),
            body,
            options,
            defaultIdempotent: false,
            treatNotFoundEmptyAsAbsent: false,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
    }

    /// <summary>
    /// KV2-009's mandated read-merge-write: reads the current configuration, overlays the fields
    /// <paramref name="patch"/> sets (a <see langword="null"/> field is left alone), writes the full
    /// configuration back, and returns what it wrote.
    /// </summary>
    /// <remarks>
    /// Two round trips, by requirement rather than by choice: the server rewrites the whole config
    /// on every write, so changing <c>environments</c> without first reading would clear
    /// <c>max_versions</c> and the rest. A mount whose config read comes back absent raises
    /// <c>BV-NOTFOUND-002 MountNotFound</c> — there is no configuration to merge into, and
    /// inventing a default one to write would be the plausible guess D-M1c-25 forbids.
    /// </remarks>
    /// <remarks>Never returns <see langword="null"/>. Conformance: Core (KV2-009). Errors beyond the common set (ERR-061): <c>BV-NOTFOUND-002 MountNotFound</c>, <c>BV-INPUT-001</c> when <c>DeleteVersionAfter</c> and <c>DeleteVersionAfterDuration</c> disagree.</remarks>
    /// <spec>Kv.V2.WriteConfig — KV2-009</spec>
    public async Task<KvV2Config> UpdateConfigAsync(
        KvV2ConfigPatch patch,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        KvV2Config current = await ReadConfigAsync(mount, options, cancellationToken).ConfigureAwait(false)
            ?? throw KvWire.Engine(ErrorCodes.NotFoundMountNotFound, $"{mount.Trim('/')}/{ConfigGroup}");

        KvV2Config merged = new()
        {
            MaxVersions = patch.MaxVersions ?? current.MaxVersions,
            CasRequired = patch.CasRequired ?? current.CasRequired,
            DeleteVersionAfter = ResolveDeleteVersionAfter(patch, mount) ?? current.DeleteVersionAfter,
            Environments = patch.Environments ?? current.Environments,
        };

        await WriteConfigAsync(merged, mount, options, cancellationToken).ConfigureAwait(false);
        return merged;
    }

    /// <summary>
    /// KV2-010: resolves <paramref name="patch"/>'s string and duration forms of
    /// <c>DeleteVersionAfter</c>. Either alone is used as-is (formatting the duration Go-style);
    /// both set to values that format to different strings is a caller error (<c>BV-INPUT-001</c>),
    /// because silently preferring one would discard the other without telling the caller.
    /// </summary>
    private static string? ResolveDeleteVersionAfter(KvV2ConfigPatch patch, string mount)
    {
        if (patch.DeleteVersionAfterDuration is not { } duration)
        {
            return patch.DeleteVersionAfter;
        }

        string formatted = GoDuration.Format(duration);
        if (patch.DeleteVersionAfter is { } explicitValue
            && !string.Equals(explicitValue, formatted, StringComparison.Ordinal))
        {
            throw KvWire.InvalidArgument(
                "DeleteVersionAfter",
                "`DeleteVersionAfter` and `DeleteVersionAfterDuration` disagree (KV2-010)",
                $"{mount.Trim('/')}/{ConfigGroup}");
        }

        return formatted;
    }

    // ---------------------------------------------------------------- convenience helpers

    /// <summary>
    /// KV-011: <see cref="WriteSecretAsync"/> with <c>Cas = 0</c> (KV2-003's "must not exist yet"),
    /// translating the server's <c>BV-KV-003 CasMismatch</c> into <c>BV-CONFLICT-006
    /// SecretAlreadyExists</c>. The caller asked for "create, don't overwrite", so the conflict is
    /// reported in those terms rather than the generic CAS mismatch a caller retrying with a real
    /// version number would expect.
    /// </summary>
    /// <remarks>Wire as <see cref="WriteSecretAsync"/> with <c>options.cas = 0</c>. Never returns <see langword="null"/>. Conformance: Core (KV-011). Errors beyond the common set (ERR-061): <c>BV-CONFLICT-006 SecretAlreadyExists</c> (translated from the server's <c>BV-KV-003</c>).</remarks>
    /// <spec>Kv.WriteIfAbsent — KV-011</spec>
    public async Task<KvV2VersionMetadata> WriteIfAbsentAsync(
        string path,
        IReadOnlyDictionary<string, JsonElement> data,
        string mount = DefaultMount,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await WriteSecretAsync(path, data, mount, new KvWriteOptions { Cas = 0 }, options, cancellationToken).ConfigureAwait(false);
        }
        catch (BastionVaultException exception) when (exception.Code == ErrorCodes.KvCasMismatch)
        {
            ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ConflictSecretAlreadyExists);
            throw BastionVaultException.Request(
                ErrorCodes.ConflictSecretAlreadyExists,
                entry.Category,
                entry.Message,
                entry.Hint,
                retryable: entry.Retryable,
                attempts: exception.Attempts,
                statusCode: exception.StatusCode,
                method: exception.Method,
                path: DataPath(path, mount),
                details: new Dictionary<string, object?>(exception.Details, StringComparer.Ordinal),
                cause: exception);
        }
    }

    /// <summary>
    /// KV-012: reads the latest version, applies <paramref name="transform"/>, writes it back with
    /// <c>Cas</c> set to the version just read (<c>0</c> when the secret does not exist yet), and
    /// retries on the server's <c>BV-KV-003 CasMismatch</c> up to <paramref name="maxAttempts"/>
    /// attempts in total.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Race semantics.</b> This is a read-modify-write, not a transaction: between the read this
    /// method performs and the write it sends, another writer may change the secret. A CAS
    /// mismatch means someone else won that race; the loop then re-reads the now-current version
    /// and re-applies <paramref name="transform"/> to it. <paramref name="transform"/> may
    /// therefore run more than once and must be safe to re-run — a transform with an external side
    /// effect (sending a notification, incrementing an outside counter) is not safe to pass here.
    /// </para>
    /// <para>
    /// <paramref name="transform"/> receives the whole <see cref="KvV2Secret"/>, not a bare data
    /// map, so the caller can see the version and the soft-delete <see cref="KvV2Secret.State"/>;
    /// it receives <see langword="null"/> when the secret does not exist yet. On exhaustion this
    /// method surfaces the server's own <c>BV-KV-003</c> rather than inventing a code for "retries
    /// exhausted" (D-M1c-25).
    /// </para>
    /// </remarks>
    /// <remarks>Wire as <see cref="ReadSecretAsync"/> then <see cref="WriteSecretAsync"/>. Never returns <see langword="null"/>. Conformance: Core (KV-012). Errors beyond the common set (ERR-061): <c>BV-INPUT-001</c> for <c>maxAttempts &lt; 1</c>, <c>BV-KV-003 CasMismatch</c> on exhaustion.</remarks>
    /// <spec>Kv.UpdateWithRetry — KV-012</spec>
    public async Task<KvV2VersionMetadata> UpdateWithRetryAsync(
        string path,
        Func<KvV2Secret?, IReadOnlyDictionary<string, JsonElement>> transform,
        string mount = DefaultMount,
        int maxAttempts = 3,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(transform);
        string logicalPath = DataPath(path, mount);
        if (maxAttempts < 1)
        {
            throw KvWire.InvalidArgument("maxAttempts", "must be at least 1", logicalPath);
        }

        BastionVaultException? lastMismatch = null;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            KvV2Secret? current = await ReadSecretAsync(path, mount, options: options, cancellationToken: cancellationToken).ConfigureAwait(false);
            IReadOnlyDictionary<string, JsonElement> next = transform(current);
            int cas = current?.Metadata.Version ?? 0;

            try
            {
                return await WriteSecretAsync(path, next, mount, new KvWriteOptions { Cas = cas }, options, cancellationToken).ConfigureAwait(false);
            }
            catch (BastionVaultException exception) when (exception.Code == ErrorCodes.KvCasMismatch)
            {
                lastMismatch = exception;
            }
        }

        throw lastMismatch!;
    }

    /// <summary>
    /// KV-013: <see cref="ReadSecretAsync"/> narrowed to one field (the <c>--field</c> equivalent).
    /// <see langword="null"/> for both "no secret" and "no such field" — use
    /// <see cref="GetFieldAsync"/> to tell them apart.
    /// </summary>
    /// <remarks>Wire as <see cref="ReadSecretAsync"/>; <c>field</c> selects a key from the response's <c>data</c> object client-side. Conformance: Core (KV-013). No error codes beyond the common set (ERR-061).</remarks>
    /// <spec>Kv.ReadField — KV-013</spec>
    public async Task<JsonElement?> ReadFieldAsync(
        string path,
        string field,
        string mount = DefaultMount,
        int? version = null,
        string? env = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        KvV2Secret? secret = await ReadSecretAsync(path, mount, version, env, options, cancellationToken).ConfigureAwait(false);
        return secret?.Data is { } data && data.TryGetValue(field, out JsonElement value) ? value.Clone() : null;
    }

    /// <summary>
    /// KV-013: <see cref="GetSecretAsync"/> narrowed to one field, raising <c>BV-KV-011
    /// FieldNotFound</c> — with the secret's available field names in <c>Details.fields</c> — when
    /// the field is absent. <c>BV-KV-001</c> and <c>BV-KV-007</c> propagate from
    /// <see cref="GetSecretAsync"/> unchanged.
    /// </summary>
    /// <remarks>Wire as <see cref="GetSecretAsync"/>; <c>field</c> selects a key client-side. Never returns <see langword="null"/>. Conformance: Core (KV-013). Errors beyond the common set (ERR-061): <c>BV-KV-011 FieldNotFound</c>, <c>BV-KV-001</c>, <c>BV-KV-007</c>.</remarks>
    /// <spec>Kv.GetField — KV-013</spec>
    public async Task<JsonElement> GetFieldAsync(
        string path,
        string field,
        string mount = DefaultMount,
        int? version = null,
        string? env = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        KvV2Secret secret = await GetSecretAsync(path, mount, version, env, options, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, JsonElement> data = secret.Data ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (data.TryGetValue(field, out JsonElement value))
        {
            return value.Clone();
        }

        throw KvWire.Engine(
            ErrorCodes.KvFieldNotFound,
            DataPath(path, mount),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["field"] = field,
                ["fields"] = data.Keys.ToList(),
            });
    }

    /// <summary>
    /// Backs the KV2-030 path helpers. <c>mount</c> gets the same <c>..</c>-segment guard as
    /// <c>path</c> (D-M4-11, RF-1): these helpers return a string a caller can hand to
    /// <c>Sys.Batch</c> or paste into a policy document, so a traversal in <c>mount</c> would
    /// otherwise propagate past the SDK entirely, never reaching a request the wire-level guards
    /// could catch.
    /// </summary>
    private static string Helper(string mount, string group, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(mount);
        ArgumentNullException.ThrowIfNull(path);
        string logicalPath = KvWire.LogicalPath(mount, group, path);
        KvWire.RequireSafePath(mount, "mount", logicalPath);
        return logicalPath;
    }

    /// <summary>RF-1: the same <c>mount</c> guard as <see cref="Helper"/>, for the <c>config</c> route.</summary>
    private static string ConfigRoute(string mount)
    {
        KvWire.RequireSafePath(mount, "mount", $"{mount.Trim('/')}/{ConfigGroup}");
        return $"{UrlBuilder.EncodePathFragment(mount.Trim('/'))}/{ConfigGroup}";
    }

    private static void WriteVersions(Utf8JsonWriter writer, IReadOnlyList<int> versions)
    {
        writer.WriteStartArray("versions");
        foreach (int version in versions)
        {
            writer.WriteNumberValue(version);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// KV2-001: the selectors, as an already-encoded query on an already-encoded path.
    /// <c>version = 0</c> and <see langword="null"/> both mean "latest" and are omitted.
    /// </summary>
    private static string EncodedDataRoute(string mount, string path, int? version, string? env)
    {
        StringBuilder route = new(KvWire.EncodedRoute(mount, DataGroup, path));
        List<string> query = [];
        if (version is { } selected && selected != 0)
        {
            query.Add($"version={selected.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrEmpty(env))
        {
            query.Add($"env={UrlBuilder.EncodeQueryValue(env)}");
        }

        if (query.Count > 0)
        {
            _ = route.Append('?').Append(string.Join('&', query));
        }

        return route.ToString();
    }

    /// <summary>KV2-002: <c>env</c> may contain neither <c>/</c> nor a control character, and must not be blank when supplied.</summary>
    private static string? ValidateEnv(string? env, string logicalPath)
    {
        if (env is null)
        {
            return null;
        }

        if (env.Length == 0 || string.IsNullOrWhiteSpace(env))
        {
            throw KvWire.InvalidArgument("env", "must be non-empty", logicalPath);
        }

        if (env.Contains('/', StringComparison.Ordinal))
        {
            throw KvWire.InvalidArgument("env", "must not contain `/`", logicalPath);
        }

        foreach (char character in env)
        {
            if (char.IsControl(character))
            {
                throw KvWire.InvalidArgument("env", "must not contain control characters", logicalPath);
            }
        }

        return env;
    }

    private string GuardDataOperation(string path, string mount, string? env)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string logicalPath = DataPath(path, mount);
        KvWire.RequireSafePath(path, "path", logicalPath);
        _ = ValidateEnv(env, logicalPath);
        GuardEnvironmentScope(env, logicalPath);
        return logicalPath;
    }

    /// <summary>
    /// KV2-022, D-M4-7: an environment-scoped credential must supply <c>env</c> on every v2
    /// <b>data</b> operation, or the server answers <c>403 Permission denied.</c>. The SDK fails
    /// fast with <c>BV-KV-009</c> instead, at a cost of zero requests.
    /// </summary>
    /// <remarks>
    /// The scope is read from the credential the client already holds — <c>AuthInfo.EnvironmentScope</c>,
    /// AUT-044's projection of the <c>approle_env_*</c> login metadata — and never from a second
    /// stored copy, which is why <see cref="EnvironmentScope"/> is derived rather than a field. A
    /// client whose token was configured or set outright has no recorded credential and so is never
    /// subject to this: "is this token environment-scoped" is a question about metadata a login
    /// produced, and answering it for a token the SDK did not receive would be a guess (D-M1c-25).
    /// KV1-004 puts <see cref="KvV1Operations"/> outside the check entirely.
    /// </remarks>
    private void GuardEnvironmentScope(string? env, string logicalPath)
    {
        if (!string.IsNullOrEmpty(env)
            || context.LastLogin?.EnvironmentScope is not { Scoped: true } scope)
        {
            return;
        }

        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.KvEnvironmentRequired);
        string hint = entry.Hint
            + $" This credential allows secret env(s) [{string.Join(", ", scope.SecretGlobs)}]"
            + $" and machine env(s) [{string.Join(", ", scope.MachineGlobs)}].";

        throw BastionVaultException.Request(
            ErrorCodes.KvEnvironmentRequired,
            entry.Category,
            entry.Message,
            hint,
            retryable: entry.Retryable,
            attempts: 0,
            path: logicalPath,
            details: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["secret_globs"] = scope.SecretGlobs,
                ["machine_globs"] = scope.MachineGlobs,
                ["path"] = logicalPath,
            });
    }

    /// <summary>KV2-006's "when <c>ReadMetadata</c> (if permitted) shows the secret exists".</summary>
    private async Task<bool> SecretExistsAsync(string path, string mount, RequestOptions? options, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadMetadataAsync(path, mount, options, cancellationToken).ConfigureAwait(false) is not null;
        }
        catch (BastionVaultException exception) when (exception.StatusCode == 403)
        {
            // Not permitted: the SDK cannot distinguish "no such secret" from "no such
            // environment", so KV2-006's fallback arm (BV-KV-001) applies.
            return false;
        }
    }

    private async Task SendVersionsAsync(
        string group,
        string path,
        IReadOnlyList<int> versions,
        string mount,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(mount);
        string logicalPath = KvWire.LogicalPath(mount, group, path);
        KvWire.RequireSafePath(path, "path", logicalPath);
        KvWire.RequireVersions(versions, logicalPath);

        _ = await logical.ExecuteShapedAsync(
            "POST",
            KvWire.EncodedRoute(mount, group, path),
            KvWire.Serialise(writer => WriteVersions(writer, versions)),
            options,
            defaultIdempotent: false,
            treatNotFoundEmptyAsAbsent: false,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);
    }

    private async Task<KvV2VersionMetadata> SendWriteAsync(
        string logicalPath,
        string mount,
        string path,
        IReadOnlyDictionary<string, JsonElement> data,
        int? cas,
        string? env,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? envs,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> body = KvWire.Serialise(writer =>
        {
            KvWire.WriteMap(writer, "data", data);
            if (cas is { } version)
            {
                writer.WriteStartObject("options");
                writer.WriteNumber("cas", version);
                writer.WriteEndObject();
            }

            if (env is not null)
            {
                writer.WriteString("env", env);
            }

            if (envs is not null)
            {
                writer.WriteStartObject("envs");
                foreach ((string name, IReadOnlyDictionary<string, JsonElement> overrides) in envs)
                {
                    KvWire.WriteMap(writer, name, overrides);
                }

                writer.WriteEndObject();
            }
        });

        Response? response = await logical.ExecuteShapedAsync(
            "POST",
            KvWire.EncodedRoute(mount, DataGroup, path),
            body,
            options,
            defaultIdempotent: false,
            treatNotFoundEmptyAsAbsent: false,
            cancellationToken,
            pathIsEncoded: true).ConfigureAwait(false);

        IReadOnlyDictionary<string, JsonElement> written = response?.Data
            ?? throw KvWire.EnvelopeMismatch(logicalPath, "data");
        return KvWire.ReadVersionMetadata(written, logicalPath);
    }
}
