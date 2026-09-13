namespace BastionVault.IntegrationSdk.Tests;

public class UnitTest1
{
    [Fact]
    public void ExposesExpectedSdkName()
    {
        Assert.Equal("BastionVault Integration SDK", SdkMetadata.Name);
    }
}
