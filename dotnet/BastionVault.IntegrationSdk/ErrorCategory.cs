namespace BastionVault.IntegrationSdk;

/// <summary>
/// The category of a <see cref="BastionVaultException"/>, matching the code ranges in
/// <c>specifications/04-error-model.md#categories-and-code-ranges</c>. Only <see cref="Configuration"/>
/// is populated by errors at milestone M1a (see <c>decisions/0003-m1a-configuration.md</c>, D-M1a-1);
/// the remaining members exist so the enum shape does not change again in M1c.
/// </summary>
public enum ErrorCategory
{
    /// <summary>Invalid or incomplete client configuration; detected before any request.</summary>
    Configuration,

    /// <summary>Invalid arguments to an SDK operation; detected before any request.</summary>
    Input,

    /// <summary>Network, TLS, timeout, cancellation, response too large.</summary>
    Transport,

    /// <summary>The server answered with something the SDK cannot interpret.</summary>
    Protocol,

    /// <summary>No token, invalid credentials, login failures, expired token.</summary>
    Authentication,

    /// <summary>Token valid but not permitted (policy, namespace binding, CIDR, gates).</summary>
    Authorization,

    /// <summary>Path, mount, secret, version, lease, user, role missing.</summary>
    NotFound,

    /// <summary>CAS mismatch, already exists/initialised, digest mismatch, brokered credential.</summary>
    Conflict,

    /// <summary>DoS guard ban, namespace request quota, client rate gate.</summary>
    RateLimit,

    /// <summary>Namespace capacity quota (507).</summary>
    Quota,

    /// <summary>Sealed, uninitialised, unhealthy cluster, standby, unsupported endpoint, internal error, API version mismatch.</summary>
    ServerState,

    /// <summary>SRV resolution, no healthy node, pinned node unavailable.</summary>
    Discovery,

    /// <summary>Engine-specific server messages worth a dedicated code (KV, Transit, PKI, SSH, TOTP, Identity, Rustion).</summary>
    Engine,
}
