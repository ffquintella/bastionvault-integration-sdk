namespace BastionVault.IntegrationSdk;

/// <summary>
/// Background token renewal configuration (<c>specifications/05-authentication.md#automatic-renewal</c>).
/// At milestone M1a this is materialised as a disabled value only
/// (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-13); the renewal loop is M2.
/// </summary>
public sealed record AutoRenewPolicy
{
    /// <summary>Default <see langword="false"/>. No environment variable resolves this setting.</summary>
    public bool Enabled { get; init; }
}
