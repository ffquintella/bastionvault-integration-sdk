namespace BastionVault.IntegrationSdk;

/// <summary>
/// One ACL verb from <c>06-system-api.md</c>'s capabilities table (SYS-051): a closed set of nine
/// wire values, plus <see cref="Other"/> for any string the server sends that is not one of them.
/// A <see langword="readonly record struct"/> rather than an <c>enum</c> because SYS-051 requires
/// the unrecognised case to <b>preserve</b> the string, which a CLR enum cannot hold; equality and
/// <see cref="ToString"/> are the wire spelling either way, so a caller that only compares against
/// <see cref="Read"/>/<see cref="Root"/>/etc. never has to know the difference.
/// </summary>
public readonly record struct Capability
{
    private readonly string wireValue;

    private Capability(string wireValue)
    {
        this.wireValue = wireValue;
    }

    /// <summary>Implies every other capability on the same path (SYS-053).</summary>
    public static readonly Capability Root = new("root");

    /// <summary>Overrides every other capability on the same path (SYS-053).</summary>
    public static readonly Capability Deny = new("deny");

    /// <summary>Implies <see cref="Connect"/> on the same path (SYS-053).</summary>
    public static readonly Capability Read = new("read");

    /// <summary>List the entries under a path.</summary>
    public static readonly Capability List = new("list");

    /// <summary>Create a new entry at a path.</summary>
    public static readonly Capability Create = new("create");

    /// <summary>Update an existing entry at a path.</summary>
    public static readonly Capability Update = new("update");

    /// <summary>Delete an entry at a path.</summary>
    public static readonly Capability Delete = new("delete");

    /// <summary>Perform a sudo-gated operation at a path.</summary>
    public static readonly Capability Sudo = new("sudo");

    /// <summary>Implied by <see cref="Read"/> and <see cref="Root"/> (SYS-053).</summary>
    public static readonly Capability Connect = new("connect");

    /// <summary>SYS-051: an unrecognised wire value, preserved verbatim rather than dropped.</summary>
    public static Capability Other(string wireValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(wireValue);
        return new Capability(wireValue);
    }

    /// <summary>The exact wire spelling this capability carries.</summary>
    public string WireValue => wireValue;

    private static readonly HashSet<string> NamedWireValues = new(StringComparer.Ordinal)
    {
        "root", "deny", "read", "list", "create", "update", "delete", "sudo", "connect",
    };

    /// <summary><see langword="true"/> when this is not one of the nine named capabilities (SYS-051).</summary>
    public bool IsOther => !NamedWireValues.Contains(wireValue);

    /// <summary>The wire spelling (e.g. <c>"read"</c>), never a CLR member name.</summary>
    public override string ToString()
    {
        return wireValue;
    }

    /// <summary>Parses one wire string, returning a named instance when it is one of the nine, else <see cref="Other"/>.</summary>
    internal static Capability FromWire(string wireValue)
    {
        return new Capability(wireValue);
    }
}

/// <summary>
/// <c>Sys.CapabilitiesSelf</c>'s result (SYS-050): the per-path capability lists, and the three
/// namespace facts the same response carries.
/// </summary>
public sealed class Capabilities
{
    /// <summary>SYS-050: read from the wire's <c>capabilities</c> map, never the duplicated top-level keys.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<Capability>> ByPath { get; init; }

    /// <summary>The wire <c>namespace_operable</c> field.</summary>
    public required bool NamespaceOperable { get; init; }

    /// <summary>The wire <c>token_namespace</c> field: the namespace the token is bound to.</summary>
    public required string TokenNamespace { get; init; }

    /// <summary>The wire <c>active_namespace</c> field: the namespace the request was made against.</summary>
    public required string ActiveNamespace { get; init; }

    /// <summary>
    /// SYS-053's convenience: whether <paramref name="capability"/> is granted on
    /// <paramref name="path"/> among this result's already-fetched capabilities. <see cref="Capability.Deny"/>
    /// overrides every other entry for the path; <see cref="Capability.Root"/> implies every
    /// capability including <paramref name="capability"/> itself. A path this result did not ask
    /// about, or one the server answered with no capabilities at all, is never operable.
    /// </summary>
    public bool Can(string path, Capability capability)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!ByPath.TryGetValue(path, out IReadOnlyList<Capability>? capabilities) || capabilities.Count == 0)
        {
            return false;
        }

        if (capabilities.Contains(Capability.Deny))
        {
            return false;
        }

        if (capabilities.Contains(Capability.Root))
        {
            return true;
        }

        if (capability == Capability.Connect && capabilities.Contains(Capability.Read))
        {
            return true;
        }

        return capabilities.Contains(capability);
    }
}
