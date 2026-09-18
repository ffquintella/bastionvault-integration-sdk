using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// One ACL policy document (SYS-040). <see cref="Hcl"/> is filled from whichever key the surface
/// that answered used: <c>policy</c> on <c>sys/policies/acl/{name}</c>, <c>rules</c> on the legacy
/// <c>sys/policy/{name}</c>. A caller therefore never has to know which surface it read from,
/// which is the whole of SYS-040.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "06-system-api.md names this type `Policy` and it is the cross-language contract (SYS-040); the colliding `System.Security.Policy` is a .NET Framework namespace that does not exist on this target. Renaming would make .NET the only SDK of the three whose type name differs from the specification's. See DR-0012 D-M7-11.")]
public sealed class Policy
{
    /// <summary>The policy's name, as the server spelled it.</summary>
    public required string Name { get; init; }

    /// <summary>The policy document. Never <see langword="null"/>: a server that sends neither key sends an empty document.</summary>
    public required string Hcl { get; init; }
}

/// <summary>One entry of <c>Sys.PolicyHistory</c>'s <c>entries</c> array (SYS-040's history route).</summary>
public sealed class PolicyHistoryEntry
{
    /// <summary>The wire <c>ts</c> field, parsed as RFC 3339 UTC; <see langword="null"/> when the server omitted or malformed it.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>The wire <c>user</c> field.</summary>
    public string? User { get; init; }

    /// <summary>The wire <c>op</c> field.</summary>
    public string? Op { get; init; }

    /// <summary>The wire <c>before_raw</c> field: the document as it was, or <see langword="null"/> on a create.</summary>
    public string? BeforeRaw { get; init; }

    /// <summary>The wire <c>after_raw</c> field: the document as it became, or <see langword="null"/> on a delete.</summary>
    public string? AfterRaw { get; init; }
}

/// <summary>
/// One case of the policy dry-run request (SYS-045).
/// </summary>
/// <remarks>
/// ⚠️ <see cref="Policies"/> is <b>tri-state</b>, and the three states are three different
/// requests: <see langword="null"/> omits the key entirely and the server evaluates the draft
/// against <c>["default"]</c>; an <b>empty</b> list sends <c>[]</c> and evaluates the draft alone;
/// a populated list sends it and evaluates the draft plus those. A type that collapsed the first
/// two — a plain <c>string[]</c> defaulting to empty, say — would silently turn "use the default
/// policy" into "use no policy", which is the defect SYS-045 exists to prevent.
/// </remarks>
public sealed class PolicyTestCase
{
    /// <summary>The path to evaluate.</summary>
    public required string Path { get; init; }

    /// <summary>The capability to evaluate on <see cref="Path"/>.</summary>
    public required Capability Capability { get; init; }

    /// <summary>SYS-045's tri-state. See the remarks on <see cref="PolicyTestCase"/>.</summary>
    public IReadOnlyList<string>? Policies { get; init; }

    /// <summary>The optional <c>env</c> selector, omitted from the wire when <see langword="null"/>.</summary>
    public string? Env { get; init; }
}

/// <summary>
/// How a dry-run case matched a policy path (SYS-045's <c>match_kind</c>). A
/// <see langword="readonly record struct"/> with a preserved unknown value, for the same reason
/// <see cref="Capability"/> is one: the specification names four spellings, and a server that
/// grows a fifth must not be turned into a parse failure or silently flattened onto
/// <see cref="None"/>.
/// </summary>
public readonly record struct PolicyMatchKind
{
    private readonly string wireValue;

    private PolicyMatchKind(string wireValue)
    {
        this.wireValue = wireValue;
    }

    /// <summary>The policy path equalled the evaluated path.</summary>
    public static readonly PolicyMatchKind Exact = new("exact");

    /// <summary>A trailing-wildcard policy path covered the evaluated path.</summary>
    public static readonly PolicyMatchKind Prefix = new("prefix");

    /// <summary>A single-segment wildcard (<c>+</c>) covered the evaluated path.</summary>
    public static readonly PolicyMatchKind SegmentWildcard = new("segment_wildcard");

    /// <summary>No policy path matched.</summary>
    public static readonly PolicyMatchKind None = new("none");

    /// <summary>The exact wire spelling.</summary>
    public string WireValue => wireValue ?? "none";

    /// <summary><see langword="true"/> when this is not one of the four the specification names.</summary>
    public bool IsOther => !NamedWireValues.Contains(WireValue);

    private static readonly HashSet<string> NamedWireValues = new(StringComparer.Ordinal)
    {
        "exact", "prefix", "segment_wildcard", "none",
    };

    /// <summary>The wire spelling, never a CLR member name.</summary>
    public override string ToString()
    {
        return WireValue;
    }

    internal static PolicyMatchKind FromWire(string? wireValue)
    {
        return new PolicyMatchKind(string.IsNullOrEmpty(wireValue) ? "none" : wireValue);
    }
}

/// <summary>One element of the dry-run response's <c>results</c> array (SYS-045).</summary>
public sealed class PolicyTestCaseResult
{
    /// <summary>The evaluated path, echoed back.</summary>
    public required string Path { get; init; }

    /// <summary>The evaluated capability, echoed back.</summary>
    public required Capability Capability { get; init; }

    /// <summary>Whether the capability is granted.</summary>
    public required bool Allowed { get; init; }

    /// <summary>The policy path that matched, or <see langword="null"/> when none did.</summary>
    public string? MatchedPath { get; init; }

    /// <summary>How <see cref="MatchedPath"/> matched.</summary>
    public required PolicyMatchKind MatchKind { get; init; }

    /// <summary>Whether an explicit <c>deny</c> produced the refusal.</summary>
    public required bool DeniedByDeny { get; init; }

    /// <summary>The policies that granted the capability.</summary>
    public required IReadOnlyList<string> GrantingPolicies { get; init; }

    /// <summary>Every policy the server evaluated for this case.</summary>
    public required IReadOnlyList<string> EvaluatedPolicies { get; init; }

    /// <summary>The named policies the server could not find.</summary>
    public required IReadOnlyList<string> MissingPolicies { get; init; }

    /// <summary>Whether the draft alone, without the named policies, would have allowed the case.</summary>
    public required bool DraftOnlyAllowed { get; init; }
}

/// <summary>The policy dry-run response (SYS-045).</summary>
public sealed class PolicyTestResult
{
    /// <summary>Whether the draft document parsed.</summary>
    public required bool ParseOk { get; init; }

    /// <summary>The parse errors, empty when <see cref="ParseOk"/>.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>One result per requested case, in request order.</summary>
    public required IReadOnlyList<PolicyTestCaseResult> Results { get; init; }
}

/// <summary>
/// SYS-043's HCL emitter: <c>path "…" { capabilities = [...] }</c> blocks with the four optional
/// lists the requirement names, plus a top-level <c>metadata {}</c> block.
/// </summary>
/// <remarks>
/// <para>
/// Every emitted string is escaped (<c>\</c>, <c>"</c>, and the three control characters that
/// cannot appear inside an HCL quoted string), so a caller-supplied path can never close its own
/// block and open another. That is the property SYS-043's "MUST escape quotes" is protecting, and
/// it is asserted against a hostile path, not only a well-formed one.
/// </para>
/// <para>
/// Output is deterministic: blocks in the order they were added, keys inside a block in the order
/// the requirement lists them, metadata entries in the order they were set. Determinism is what
/// makes the round-trip assertion (build → <c>WritePolicy</c> → <c>ReadPolicy</c> → the same
/// document) meaningful rather than incidental.
/// </para>
/// </remarks>
public sealed class PolicyBuilder
{
    private readonly List<string> blocks = [];
    private readonly List<KeyValuePair<string, string>> metadata = [];

    /// <summary>
    /// Adds one <c>path</c> block. <paramref name="capabilities"/> reuses SYS-051's
    /// <see cref="Capability"/>, so an unrecognised verb a caller needs is still expressible
    /// through <see cref="Capability.Other"/> and is emitted verbatim.
    /// </summary>
    public PolicyBuilder AddPath(
        string path,
        IReadOnlyList<Capability> capabilities,
        IReadOnlyList<string>? requiredParameters = null,
        IReadOnlyList<string>? allowedParameters = null,
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<string>? groups = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(capabilities);

        StringBuilder builder = new();
        _ = builder.Append(CultureInfo.InvariantCulture, $"path \"{Escape(path)}\" {{\n");
        _ = builder.Append(CultureInfo.InvariantCulture, $"  capabilities = {List(capabilities.Select(capability => capability.WireValue))}\n");
        AppendOptional(builder, "required_parameters", requiredParameters);
        AppendOptional(builder, "allowed_parameters", allowedParameters);
        AppendOptional(builder, "scopes", scopes);
        AppendOptional(builder, "groups", groups);
        _ = builder.Append("}\n");
        blocks.Add(builder.ToString());
        return this;
    }

    /// <summary>Sets one <c>metadata</c> entry. Setting the same key twice replaces the value and keeps the original position.</summary>
    public PolicyBuilder WithMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        int existing = metadata.FindIndex(pair => string.Equals(pair.Key, key, StringComparison.Ordinal));
        if (existing >= 0)
        {
            metadata[existing] = new KeyValuePair<string, string>(key, value);
        }
        else
        {
            metadata.Add(new KeyValuePair<string, string>(key, value));
        }

        return this;
    }

    /// <summary>Emits the document. An empty builder emits the empty string, never a stray blank block.</summary>
    public string Build()
    {
        StringBuilder builder = new();
        foreach (string block in blocks)
        {
            if (builder.Length > 0)
            {
                _ = builder.Append('\n');
            }

            _ = builder.Append(block);
        }

        if (metadata.Count > 0)
        {
            if (builder.Length > 0)
            {
                _ = builder.Append('\n');
            }

            _ = builder.Append("metadata {\n");
            foreach (KeyValuePair<string, string> pair in metadata)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"  {Escape(pair.Key)} = \"{Escape(pair.Value)}\"\n");
            }

            _ = builder.Append("}\n");
        }

        return builder.ToString();
    }

    private static void AppendOptional(StringBuilder builder, string name, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        _ = builder.Append(CultureInfo.InvariantCulture, $"  {name} = {List(values)}\n");
    }

    private static string List(IEnumerable<string> values)
    {
        return "[" + string.Join(", ", values.Select(value => $"\"{Escape(value)}\"")) + "]";
    }

    /// <summary>SYS-043's escaping. <c>\</c> first, so the escapes this method adds are not themselves re-escaped.</summary>
    private static string Escape(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            _ = c switch
            {
                '\\' => builder.Append("\\\\"),
                '"' => builder.Append("\\\""),
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                '\t' => builder.Append("\\t"),
                _ => builder.Append(c),
            };
        }

        return builder.ToString();
    }
}
