using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// DOC-021's guard (DR-0018 D-M11-25 g2): 100% public symbol documentation is enforced as
/// compiler diagnostics (CS1591 via <c>GenerateDocumentationFile</c>, fatal via
/// <c>TreatWarningsAsErrors</c>), so this asserts the two csproj properties DOC-021 depends
/// on are still set, rather than re-enforcing coverage a second way.
/// </summary>
public sealed class DocumentationCoverageGuardTests
{
    private static string RepositoryRoot { get; } = new FixtureRepository().RepositoryRoot;

    private static string LibraryCsprojText { get; } = File.ReadAllText(
        Path.Combine(
            RepositoryRoot,
            "dotnet",
            "BastionVault.IntegrationSdk",
            "BastionVault.IntegrationSdk.csproj"));

    [Fact]
    [Trait("Requirement", "DOC-021")]
    public void LibraryCsproj_GeneratesDocumentationFile()
    {
        Assert.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", LibraryCsprojText);
    }

    [Fact]
    [Trait("Requirement", "DOC-021")]
    public void LibraryCsproj_TreatsWarningsAsErrors()
    {
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", LibraryCsprojText);
    }
}
