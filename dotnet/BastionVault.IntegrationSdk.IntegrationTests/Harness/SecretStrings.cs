namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// <see cref="SecretString.Reveal"/> is nullable-returning, and every place the harness needs the
/// value it needs a real one. This turns "the secret was empty" into a harness error at the point
/// of use rather than a null flowing into a request body.
/// </summary>
internal static class SecretStrings
{
    public static string Value(this SecretString secret, string what)
    {
        return secret.Reveal() ?? throw new InvalidOperationException($"{what} is empty");
    }
}
