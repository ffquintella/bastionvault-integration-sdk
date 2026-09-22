using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

namespace BastionVault.IntegrationSdk.DocsSamples.Samples;

/// <summary>
/// The executed sample shown by <c>docs/dotnet/errors.md</c> (D7)'s "Reading an error" section,
/// the .NET adaptation of guide 13 in <c>specifications/17-usage-guides.md</c>.
/// </summary>
public sealed class ErrorReferenceSamples : IClassFixture<MockVaultFixture>
{
    private readonly MockVaultFixture vault;

    public ErrorReferenceSamples(MockVaultFixture vault)
    {
        this.vault = vault;
    }

    [Fact]
    public async Task Reading_an_error()
    {
        vault.ServeReadDeniedByPolicy();
        using BastionVaultClient client = vault.CreateClient();

        BastionVaultException error = await Assert.ThrowsAsync<BastionVaultException>(async () =>
        {
            // docs:begin errors/reading-an-error
            try
            {
                await client.Kv.V2.ReadSecretAsync("app/db", mount: "secret");
            }
            catch (BastionVaultException e)
            {
                // e.ToString() is exactly ERR-002's one-line form; never build your own from
                // the parts, and never parse it back apart — switch on e.Code instead.
                Console.Error.WriteLine(e);

                Console.WriteLine($"code:       {e.Code} ({e.Category})");
                Console.WriteLine($"retryable:  {e.Retryable}, attempts: {e.Attempts}");
                Console.WriteLine($"server:     {e.ServerMessage}");
                Console.WriteLine($"request:    HTTP {e.StatusCode} {e.Method} {e.Path}");
                throw;
            }
            // docs:end errors/reading-an-error
        });

        Assert.Equal(ErrorCodes.AuthzPermissionDenied, error.Code);
        Assert.False(error.Retryable);
        Assert.Equal(403, error.StatusCode);
        Assert.Equal("permission denied", error.ServerMessage);
    }
}
