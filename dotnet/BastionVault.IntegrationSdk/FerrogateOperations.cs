using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>AUT-052's machine-enrolment state, as an enum rather than a wire string.</summary>
/// <remarks>
/// <see cref="Unknown"/> is a <b>member of the server's own set</b>, not a parse failure marker:
/// AUT-052 lists it alongside the other four. A status string outside the five therefore also
/// reads as <see cref="Unknown"/>, which is the only answer that does not invent information
/// (D-M6-7).
/// </remarks>
public enum MachineIdentityStatus
{
    /// <summary>The enrolment is awaiting an administrator's decision (<c>BV-AUTH-012</c> on login).</summary>
    Pending,

    /// <summary>The machine is enrolled and may log in.</summary>
    Approved,

    /// <summary>The enrolment was refused (<c>BV-AUTH-013</c> on login).</summary>
    Rejected,

    /// <summary>A previously approved machine was revoked (<c>BV-AUTH-014</c> on login).</summary>
    Revoked,

    /// <summary>The server does not know this machine, or reported a status outside the set.</summary>
    Unknown,
}

/// <summary>AUT-051's <c>auth/{mount}/requirement</c> answer.</summary>
public sealed class FerrogateRequirement
{
    /// <summary>Whether this server requires a machine identity for AppID logins (AUT-051).</summary>
    public required bool RequireMachineIdentity { get; init; }

    /// <summary>The audience a child token must carry.</summary>
    public string? ExpectedAudience { get; init; }

    /// <summary>The SPIFFE trust domain the Machine Identity Agent issues under.</summary>
    public string? TrustDomain { get; init; }

    /// <summary>The Machine Identity Agent environment name.</summary>
    public string? MiaEnvironment { get; init; }
}

/// <summary>AUT-052's <c>auth/{mount}/status</c> answer.</summary>
public sealed class MachineStatus
{
    /// <summary>The enrolment state (AUT-052).</summary>
    public required MachineIdentityStatus Status { get; init; }

    /// <summary>
    /// The whole <c>data</c> object, so a field the requirement does not name is still reachable
    /// without the SDK guessing a name for it (D-M1c-25).
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Raw { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

/// <summary>
/// AUT-052's <c>auth/{mount}/enroll</c> answer. <b>Enrolment never returns a token</b> — the
/// requirement says so explicitly, and this type has no member that could hold one.
/// </summary>
public sealed class EnrollResult
{
    /// <summary>The state the enrolment landed in, normally <see cref="MachineIdentityStatus.Pending"/>.</summary>
    public required MachineIdentityStatus Status { get; init; }

    /// <summary>The whole <c>data</c> object; see <see cref="MachineStatus.Raw"/>.</summary>
    public IReadOnlyDictionary<string, JsonElement> Raw { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

/// <summary>
/// The FerroGate machine-identity auth method (<c>05-authentication.md</c> §Method: FerroGate,
/// AUT-050…AUT-054), reached from <see cref="AuthOperations.Ferrogate"/>.
/// </summary>
/// <remarks>
/// <b>AUT-053: obtaining the child token is out of scope.</b> The SDK accepts it as an opaque
/// string and never derives, caches or refreshes one. Applications get it from the local Machine
/// Identity Agent; the documented bridge is <c>bvault ferrogate token --format json</c>.
/// </remarks>
public sealed class FerrogateOperations
{
    private const string DpopHeader = "DPoP";

    private readonly AuthEndpoint endpoint;
    private readonly LoginRunner runner;
    private readonly ClientContext context;

    internal FerrogateOperations(ClientContext context, string activeNamespace)
    {
        this.context = context;
        endpoint = new AuthEndpoint(context, activeNamespace);
        runner = new LoginRunner(context, activeNamespace);
        Admin = new FerrogateAdminOperations(context, activeNamespace);
    }

    /// <summary>AUT-054's root-level administration surface.</summary>
    public FerrogateAdminOperations Admin { get; }

    /// <summary>
    /// AUT-051: <c>GET auth/{mount}/requirement</c>, callable without a token (CFG-020).
    /// </summary>
    /// <param name="mount">The auth mount path segment. Default <c>ferrogate</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException"><c>BV-PROTOCOL-002</c> when the response carries no <c>data</c>.</exception>
    /// <remarks>Wire params: none. Returns a <see cref="FerrogateRequirement"/>, never <see langword="null"/> (throws instead). Conformance: Shared (AUT-051). Errors beyond the common set (ERR-061): <c>BV-PROTOCOL-002</c>.</remarks>
    /// <spec>Auth.Ferrogate.Requirement — AUT-051</spec>
    public async Task<FerrogateRequirement> RequirementAsync(
        string mount = "ferrogate",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string path = $"auth/{AuthEndpoint.Mount(mount)}/requirement";
        Response? response = await endpoint.ReadTokenlessAsync(path, options, cancellationToken).ConfigureAwait(false);
        if (response?.Data is not { } data)
        {
            throw endpoint.EnvelopeMismatch(path, "data");
        }

        FerrogateRequirement requirement = new()
        {
            RequireMachineIdentity = AuthEndpoint.ReadBool(data, "require_machine_identity"),
            ExpectedAudience = AuthEndpoint.ReadString(response, "expected_audience"),
            TrustDomain = AuthEndpoint.ReadString(response, "trust_domain"),
            MiaEnvironment = AuthEndpoint.ReadString(response, "mia_environment"),
        };

        context.MachineIdentityRequirement[ClientContext.MachineIdentityKey(endpoint.EffectiveNamespace(options), mount)] =
            requirement.RequireMachineIdentity;
        return requirement;
    }

    /// <summary>
    /// AUT-051's cached convenience: <see cref="RequirementAsync"/>'s
    /// <see cref="FerrogateRequirement.RequireMachineIdentity"/>, fetched once per
    /// <b>(namespace, mount)</b> and answered from memory thereafter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cache lives on the client, not on this short-lived view, so <c>Client.Auth</c> read
    /// twice still answers from one fetch. It has no expiry: the flag is a deployment-level
    /// property, AUT-051 names no lifetime, and a caller who needs the live answer calls
    /// <see cref="RequirementAsync"/>, which refreshes the cache as a side effect (D-M6-14).
    /// </para>
    /// <para>
    /// ⚠️ <b>The namespace is half the key</b> (D-M6-16). The client's state is shared by every
    /// <c>WithNamespace</c> view and <see cref="RequestOptions.Namespace"/> overrides it per call,
    /// so a mount-only key would answer one tenant's auth posture out of another's entry — and in
    /// the fail-open direction, telling a namespace that <i>does</i> require a machine identity
    /// that it does not.
    /// </para>
    /// </remarks>
    /// <param name="mount">The auth mount path segment. Default <c>ferrogate</c>.</param>
    /// <param name="options">Per-request options (CFG-060), used only if a fetch is needed.</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <remarks>HTTP call: none on a cache hit; otherwise <c>GET auth/{mount}/requirement</c> via <see cref="RequirementAsync"/>. Wire params: none. Returns a <see cref="bool"/>, never <see langword="null"/>. Conformance: Shared (AUT-051). Errors beyond the common set (ERR-061): <c>BV-PROTOCOL-002</c>, from a delegated fetch.</remarks>
    /// <spec>Auth.Ferrogate.IsMachineIdentityRequired — AUT-051</spec>
    public async Task<bool> IsMachineIdentityRequiredAsync(
        string mount = "ferrogate",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (context.MachineIdentityRequirement.TryGetValue(
                ClientContext.MachineIdentityKey(endpoint.EffectiveNamespace(options), mount),
                out bool cached))
        {
            return cached;
        }

        FerrogateRequirement requirement = await RequirementAsync(mount, options, cancellationToken).ConfigureAwait(false);
        return requirement.RequireMachineIdentity;
    }

    /// <summary>
    /// AUT-050: <c>POST auth/{mount}/login</c> with body
    /// <c>{"token": childToken, "dpop": proof?, "user_token": userToken?}</c>.
    /// </summary>
    /// <remarks>
    /// <b>The proof travels twice.</b> AUT-050 requires a supplied <paramref name="dpopProof"/> to
    /// be sent both as the <c>DPoP</c> header and as the <c>dpop</c> body field; this is not a
    /// redundancy the SDK may optimise away, because the server reads the two independently. The
    /// header is merged into <see cref="RequestOptions.Headers"/> rather than replacing it, and
    /// <c>DPoP</c> is not on TRN-012's reserved list, so a caller's own headers survive.
    /// </remarks>
    /// <param name="childToken">The opaque FerroGate child token (AUT-053).</param>
    /// <param name="dpopProof">The DPoP proof, sent in both places when supplied (AUT-050).</param>
    /// <param name="userToken">A user token to bind the machine login to.</param>
    /// <param name="mount">The auth mount path segment. Default <c>ferrogate</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <exception cref="BastionVaultException">
    /// <c>BV-AUTH-012</c> (enrolment pending), <c>BV-AUTH-013</c> (rejected), <c>BV-AUTH-014</c>
    /// (revoked) and the rest of AUT-011's refinements.
    /// </exception>
    /// <remarks>Wire params: <c>token</c>, <c>dpop</c> (optional), <c>user_token</c> (optional). Returns <see cref="AuthInfo"/>, never <see langword="null"/>. Conformance: Shared (AUT-050). Errors beyond the common set (ERR-061): <c>BV-AUTH-012</c>, <c>BV-AUTH-013</c>, <c>BV-AUTH-014</c>.</remarks>
    /// <spec>Auth.Ferrogate.Login — AUT-050</spec>
    public Task<AuthInfo> LoginAsync(
        SecretString childToken,
        string? dpopProof = null,
        SecretString? userToken = null,
        string mount = "ferrogate",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(childToken);
        return runner.LoginAsync(
            $"auth/{AuthEndpoint.Mount(mount)}/login",
            AuthEndpoint.JsonObject(
                ("token", childToken.Reveal()),
                ("dpop", dpopProof),
                ("user_token", userToken?.Reveal())),
            install: true,
            WithDpop(options, dpopProof),
            cancellationToken);
    }

    /// <summary>
    /// AUT-052: <c>POST auth/{mount}/status</c>, callable without a token, mapping the wire
    /// <c>status</c> onto <see cref="MachineIdentityStatus"/>.
    /// </summary>
    /// <param name="childToken">The opaque FerroGate child token (AUT-053).</param>
    /// <param name="dpopProof">The DPoP proof, sent in both places when supplied (AUT-050).</param>
    /// <param name="mount">The auth mount path segment. Default <c>ferrogate</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <remarks>Wire params: <c>token</c>, <c>dpop</c> (optional). Returns a <see cref="MachineStatus"/>, never <see langword="null"/>. Conformance: Shared (AUT-052). Errors beyond the common set (ERR-061): none beyond the status mapping in <see cref="MachineIdentityStatus"/>.</remarks>
    /// <spec>Auth.Ferrogate.Status — AUT-052</spec>
    public async Task<MachineStatus> StatusAsync(
        SecretString childToken,
        string? dpopProof = null,
        string mount = "ferrogate",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(childToken);
        Response? response = await endpoint.WriteTokenlessAsync(
            $"auth/{AuthEndpoint.Mount(mount)}/status",
            AuthEndpoint.JsonObject(("token", childToken.Reveal()), ("dpop", dpopProof)),
            WithDpop(options, dpopProof),
            cancellationToken).ConfigureAwait(false);

        return new MachineStatus
        {
            Status = ParseStatus(AuthEndpoint.ReadString(response, "status")),
            Raw = AuthEndpoint.ReadData(response),
        };
    }

    /// <summary>
    /// AUT-052: <c>POST auth/{mount}/enroll</c>, callable without a token (CFG-020).
    /// <b>Never returns a token</b> — enrolment requests a machine identity, it does not grant one.
    /// The machine becomes usable only once an administrator approves it
    /// (<see cref="FerrogateAdminOperations.ApproveAsync"/>).
    /// </summary>
    /// <param name="spiffeId">The SPIFFE id the Machine Identity Agent issued to this machine.</param>
    /// <param name="comment">A free-text note for the approving administrator.</param>
    /// <param name="mount">The auth mount path segment. Default <c>ferrogate</c>.</param>
    /// <param name="options">Per-request options (CFG-060).</param>
    /// <param name="cancellationToken">Runtime cancellation.</param>
    /// <remarks>Wire params: <c>spiffe_id</c>, <c>comment</c> (optional). Returns an <see cref="EnrollResult"/>, never <see langword="null"/>, and never a token. Conformance: Shared (AUT-052). Errors beyond the common set (ERR-061): none.</remarks>
    /// <spec>Auth.Ferrogate.Enroll — AUT-052</spec>
    public async Task<EnrollResult> EnrollAsync(
        string spiffeId,
        string? comment = null,
        string mount = "ferrogate",
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spiffeId);
        Response? response = await endpoint.WriteTokenlessAsync(
            $"auth/{AuthEndpoint.Mount(mount)}/enroll",
            AuthEndpoint.JsonObject(("spiffe_id", spiffeId), ("comment", comment)),
            options,
            cancellationToken).ConfigureAwait(false);

        return new EnrollResult
        {
            Status = ParseStatus(AuthEndpoint.ReadString(response, "status")),
            Raw = AuthEndpoint.ReadData(response),
        };
    }

    /// <summary>
    /// AUT-052's five-member map. Case-insensitive because the wire spelling is lower case and a
    /// server that capitalised one would otherwise silently read as <see cref="MachineIdentityStatus.Unknown"/>.
    /// </summary>
    internal static MachineIdentityStatus ParseStatus(string? wire)
    {
        return wire?.ToUpperInvariant() switch
        {
            "PENDING" => MachineIdentityStatus.Pending,
            "APPROVED" => MachineIdentityStatus.Approved,
            "REJECTED" => MachineIdentityStatus.Rejected,
            "REVOKED" => MachineIdentityStatus.Revoked,
            _ => MachineIdentityStatus.Unknown,
        };
    }

    /// <summary>
    /// AUT-050's header half. Returns <paramref name="options"/> unchanged when there is no proof,
    /// so a call without one sends no <c>DPoP</c> header at all rather than an empty one (OVR-007).
    /// </summary>
    private static RequestOptions? WithDpop(RequestOptions? options, string? dpopProof)
    {
        if (string.IsNullOrEmpty(dpopProof))
        {
            return options;
        }

        RequestOptions effective = options ?? new RequestOptions();
        Dictionary<string, string> headers = effective.Headers is { } existing
            ? new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        headers[DpopHeader] = dpopProof;
        return effective with { Headers = headers };
    }
}
