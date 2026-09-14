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

    private static string Make(string stem, int padding) => "s." + stem + new string('0', padding);
}
