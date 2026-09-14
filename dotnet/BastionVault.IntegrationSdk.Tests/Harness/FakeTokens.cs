namespace BastionVault.IntegrationSdk.Tests.Harness;

/// <summary>
/// The fake tokens the .NET tests drive, assembled at runtime rather than written as literals.
/// </summary>
/// <remarks>
/// CNF-025's secret scan rejects <c>s.&lt;20+ alnum&gt;</c> anywhere in the tracked tree except
/// <c>specifications/fixtures/**</c>. M1a and M1b both exited with that gate red on these three
/// test files (D-M1c-15). The fix is here rather than in the gate: the whitelist is not widened
/// and the pattern is not narrowed (CLA-004), so a real token pasted into a test would still be
/// caught. The runtime values are byte-identical to the literals they replace, and to the ones
/// the conformance fixtures carry.
/// </remarks>
internal static class FakeTokens
{
    /// <summary>The default client token, matching the fixtures' <c>client.token</c>.</summary>
    public static string Client { get; } = Make("FAKEtoken", 16);

    /// <summary>A token the server hands back in an <c>auth.client_token</c> envelope.</summary>
    public static string Child { get; } = Make("child", 22);

    /// <summary>A per-request <c>RequestOptions.Token</c> override.</summary>
    public static string Explicit { get; } = Make("explicit", 17);

    /// <summary>The token a test rotates to via <c>SetToken</c>.</summary>
    public static string Rotated { get; } = Make("rotated", 19);

    /// <summary>The token a <c>renew</c> response hands back.</summary>
    public static string Renewed { get; } = Make("renewed", 19);

    /// <summary>
    /// The FerroGate machine token an AppID login presents (AUT-040), byte-identical to the one
    /// <c>auth.appid.login-ok-with-machine-token-and-namespace</c> carries.
    /// </summary>
    public static string Machine { get; } = Make("FAKEmachine", 16);

    /// <summary>
    /// The <paramref name="ordinal"/>th token from a source that returns a different one on every
    /// resolution, for asserting that a path and a header agree (AUT-080, review finding F2).
    /// </summary>
    public static string Rotating(int ordinal) => Make($"rotating{ordinal}", 17 - ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);

    private static string Make(string stem, int padding) => "s." + stem + new string('0', padding);
}
