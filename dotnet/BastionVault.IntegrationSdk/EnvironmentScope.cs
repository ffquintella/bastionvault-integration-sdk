namespace BastionVault.IntegrationSdk;

/// <summary>
/// AUT-044's derived view of an AppID login's <c>approle_env_*</c> metadata, so a KV caller can see
/// that an <c>env</c> is mandatory (<c>specifications/07-kv-engine.md#environments</c>) without
/// parsing <see cref="AuthInfo.Metadata"/> itself.
/// </summary>
/// <remarks>
/// <para>
/// Derived, never stored: <see cref="AuthInfo.EnvironmentScope"/> computes this from the metadata
/// the server sent. That is deliberate — a second stored copy is a second thing that can disagree
/// with <see cref="AuthInfo.Metadata"/>, and AUT-044 defines the scope *as* a projection of those
/// three keys rather than as an independent field on the wire.
/// </para>
/// <para>
/// A login from any other method (or a token created by <c>Auth.Token.Create</c>) carries none of
/// the three keys and therefore yields <see cref="Unscoped"/>. That is the specification's answer
/// and not a fallback guess (D-M1c-25): AUT-044 says the keys <i>may</i> be present, so their
/// absence means "not environment-scoped".
/// </para>
/// </remarks>
public sealed class EnvironmentScope
{
    /// <summary>The <c>approle_env_scoped</c> metadata key (AUT-044).</summary>
    internal const string ScopedKey = "approle_env_scoped";

    /// <summary>The <c>approle_env_secret</c> metadata key (AUT-044).</summary>
    internal const string SecretGlobsKey = "approle_env_secret";

    /// <summary>The <c>approle_env_machine</c> metadata key (AUT-044).</summary>
    internal const string MachineGlobsKey = "approle_env_machine";

    private EnvironmentScope(bool scoped, IReadOnlyList<string> secretGlobs, IReadOnlyList<string> machineGlobs)
    {
        Scoped = scoped;
        SecretGlobs = secretGlobs;
        MachineGlobs = machineGlobs;
    }

    /// <summary>The scope of a credential carrying no <c>approle_env_*</c> metadata.</summary>
    internal static EnvironmentScope Unscoped { get; } =
        new(false, Array.Empty<string>(), Array.Empty<string>());

    /// <summary>Whether the credential is environment-scoped, from <c>approle_env_scoped</c>.</summary>
    public bool Scoped { get; }

    /// <summary>The glob list from <c>approle_env_secret</c>, empty when the key is absent.</summary>
    public IReadOnlyList<string> SecretGlobs { get; }

    /// <summary>The glob list from <c>approle_env_machine</c>, empty when the key is absent.</summary>
    public IReadOnlyList<string> MachineGlobs { get; }

    /// <summary>
    /// Derives the scope from an <see cref="AuthInfo.Metadata"/> map (AUT-044). Metadata values are
    /// strings on the wire, so <c>approle_env_scoped</c> is compared against the boolean spellings
    /// CFG-003 already fixes for this project rather than a new set invented here.
    /// </summary>
    internal static EnvironmentScope From(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return Unscoped;
        }

        bool scoped = metadata.TryGetValue(ScopedKey, out string? scopedValue) && IsTrue(scopedValue);
        string[] secretGlobs = Globs(metadata, SecretGlobsKey);
        string[] machineGlobs = Globs(metadata, MachineGlobsKey);
        return scoped || secretGlobs.Length > 0 || machineGlobs.Length > 0
            ? new EnvironmentScope(scoped, secretGlobs, machineGlobs)
            : Unscoped;
    }

    private static bool IsTrue(string? value)
        => value is not null
            && (string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));

    private static string[] Globs(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
}
