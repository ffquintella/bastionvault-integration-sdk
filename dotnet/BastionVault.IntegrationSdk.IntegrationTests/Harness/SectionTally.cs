using System.Collections.Concurrent;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>ITG-023's per-section operation count, built from observed HTTP calls.</summary>
/// <remarks>
/// A "typed operation" is approximated as a distinct (section, verb, path-shape) signature, since
/// the harness only observes method and path (<see cref="RequestEvent"/>), not the SDK's
/// canonical operation name. <see cref="RegisterMount"/> lets <see cref="ResourceLedger"/> pin a
/// mount it created to the section its engine type belongs to.
/// </remarks>
internal sealed class SectionTally
{
    private static readonly (string Prefix, string Section)[] StaticMap =
    [
        ("sys/identity", "12-other-engines-and-identity"),
        ("sys", "06-system-api"),
        ("auth", "05-authentication"),
        ("identity", "12-other-engines-and-identity"),
        ("resource-group", "12-other-engines-and-identity"),
        ("resources", "12-other-engines-and-identity"),
        ("files", "12-other-engines-and-identity"),
        ("rustion", "12-other-engines-and-identity"),
        ("secret", "07-kv-engine"),
    ];

    private static readonly Dictionary<string, string> EngineTypeToSection = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kv-v2"] = "07-kv-engine",
        ["kv"] = "07-kv-engine",
        ["transit"] = "08-transit-engine",
        ["pki"] = "09-pki-engine",
        ["ssh"] = "10-ssh-engine",
        ["totp"] = "11-totp-engine",
        ["userpass"] = "05-authentication",
        ["approle"] = "05-authentication",
        ["app-id"] = "05-authentication",
        ["ferrogate"] = "05-authentication",
        ["oidc"] = "05-authentication",
        ["saml"] = "05-authentication",
        ["ldap"] = "12-other-engines-and-identity",
        ["resource-group"] = "12-other-engines-and-identity",
        ["resources"] = "12-other-engines-and-identity",
        ["resource"] = "12-other-engines-and-identity", // SYS-021's mount-type spelling is singular; the ledger mounts by this string.
        ["files"] = "12-other-engines-and-identity",
    };

    private readonly ConcurrentDictionary<string, string> mountSections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> seenSignatures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> counts = new(StringComparer.Ordinal);

    // Called by ResourceLedger when it mounts `type` at `path`, so the tally can classify calls
    // against that mount by its actual engine rather than falling back to "unclassified".
    public void RegisterMount(string path, string type)
    {
        string section = EngineTypeToSection.TryGetValue(type, out string? mapped) ? mapped : "unclassified";
        mountSections[path.Trim('/')] = section;
    }

    // Classifies one observed call and counts it once per distinct (section, verb, shape).
    public void Record(string method, string path)
    {
        string section = Classify(path);
        string signature = string.Concat(section, "|", method, "|", OperationShape(path));
        if (seenSignatures.TryAdd(signature, 0))
        {
            _ = counts.AddOrUpdate(section, 1, static (_, c) => c + 1);
        }
    }

    /// <summary>The tally so far: specification section to distinct operations exercised.</summary>
    public IReadOnlyDictionary<string, int> Counts => counts;

    private string Classify(string path)
    {
        string trimmed = path.TrimStart('/');
        foreach (KeyValuePair<string, string> mount in mountSections)
        {
            if (trimmed == mount.Key || trimmed.StartsWith(mount.Key + "/", StringComparison.Ordinal))
            {
                return mount.Value;
            }
        }

        foreach ((string prefix, string section) in StaticMap)
        {
            if (trimmed == prefix || trimmed.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return section;
            }
        }

        return "unclassified";
    }

    private static string OperationShape(string path)
    {
        string[] parts = path.TrimStart('/').Split('/');
        return parts.Length <= 1 ? path : string.Join('/', parts.Skip(1));
    }
}
