using System.Text.Json;
using BastionVault.IntegrationSdk.Internal;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// SYS-040's legacy policy surface, reached from <see cref="SysOperations.Legacy"/>:
/// <c>sys/policy</c> and <c>sys/policy/{name}</c>, which share handlers with
/// <c>sys/policies/acl</c> but return the document under <c>rules</c> rather than <c>policy</c>.
/// </summary>
/// <remarks>
/// <para>
/// The specification exposes this surface as a <b>MAY</b> ("the SDK MUST use the
/// <c>policies/acl</c> surface and MAY expose the legacy one as <c>Sys.Legacy.*</c>"), so nothing
/// here is a preferred route: <see cref="SysOperations.ReadPolicyAsync"/> already fills
/// <see cref="Policy.Hcl"/> from whichever key is present, and a caller has no reason to reach for
/// this class except to read a document a legacy tool wrote.
/// </para>
/// <para>
/// ⚠️ Reads only. See DR-0012 D-M7-14: the legacy <i>write</i> body is not specified anywhere —
/// Appendix A's note names the <c>rules</c> field for the response — and a write that guessed the
/// request key would fail silently, storing nothing under a name the caller believes it wrote.
/// <see cref="SysOperations.WritePolicyAsync"/> and <see cref="SysOperations.DeletePolicyAsync"/>
/// act on the same policies through the specified surface.
/// </para>
/// </remarks>
public sealed class LegacyPolicyOperations
{
    private readonly LogicalOperations logical;

    internal LegacyPolicyOperations(ClientContext context, string activeNamespace)
    {
        logical = new LogicalOperations(context, activeNamespace);
    }

    /// <summary>SYS-040: <c>GET sys/policy</c> → <c>{"keys": [...]}</c>, the same listing <see cref="SysOperations.ListPoliciesAsync"/> returns.</summary>
    /// <remarks>Wire params: none. Returns a list of policy names, never <see langword="null"/> (empty when there are none). Conformance: Complete (SYS-040). Errors beyond the common set (ERR-061): none.</remarks>
    /// <spec>Sys.Legacy.ListPolicies — SYS-040</spec>
    public async Task<IReadOnlyList<string>> ListPoliciesAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Response? response = await logical.ExecuteShapedAsync(
            "GET", "sys/policy", null, options, defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken).ConfigureAwait(false);
        return SysWire.ReadKeys(response?.Data);
    }

    /// <summary>
    /// SYS-040: <c>GET sys/policy/{name}</c> → a <see cref="Policy"/> whose <see cref="Policy.Hcl"/>
    /// is filled from the legacy <c>rules</c> key, or <see langword="null"/> when there is no such
    /// policy. This is the difference SYS-040 exists to hide, and it is hidden by sharing
    /// <c>SysWire.ToPolicy</c> with the <c>policies/acl</c> surface rather than by a second parser.
    /// </summary>
    /// <remarks>Wire params: none. Returns a <see cref="Policy"/>, or <see langword="null"/> when no policy has this name. Conformance: Complete (SYS-040). Errors beyond the common set (ERR-061): none.</remarks>
    /// <spec>Sys.Legacy.ReadPolicy — SYS-040</spec>
    public async Task<Policy?> ReadPolicyAsync(string name, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string policyName = MountPaths.ToWire(name, "name");
        Response? response;
        try
        {
            response = await logical.ExecuteShapedAsync(
                "GET", $"sys/policy/{UrlBuilder.EncodePathSegment(policyName)}", null, options,
                defaultIdempotent: true, treatNotFoundEmptyAsAbsent: true, cancellationToken, pathIsEncoded: true).ConfigureAwait(false);
        }
        catch (BastionVaultException failure) when (string.Equals(failure.Code, ErrorCodes.NotFoundPolicyNotFound, StringComparison.Ordinal))
        {
            return null;
        }

        return response?.Data is { } data ? SysWire.ToPolicy(data, policyName) : null;
    }
}
