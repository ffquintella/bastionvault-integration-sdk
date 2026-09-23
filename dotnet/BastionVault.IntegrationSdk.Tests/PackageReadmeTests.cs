using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// DOC-031's guard (DR-0018 D-M11-2, D-M11-25 g2): the package-registry README MUST be the
/// same D1 file, single source — not a copy that could drift. This checks the csproj
/// wiring rather than running <c>dotnet pack</c> itself (that is CNF-... packaging
/// territory, already exercised by the release checklist), so a missing property or a
/// duplicated README fails fast in the ordinary test run instead of only at pack time.
/// </summary>
public sealed class PackageReadmeTests
{
    private static string RepositoryRoot { get; } = new FixtureRepository().RepositoryRoot;

    private static string LibraryCsprojText { get; } = File.ReadAllText(
        Path.Combine(
            RepositoryRoot,
            "dotnet",
            "BastionVault.IntegrationSdk",
            "BastionVault.IntegrationSdk.csproj"));

    [Fact]
    [Trait("Requirement", "DOC-031")]
    public void LibraryCsproj_DeclaresPackageReadmeFile()
    {
        Assert.Contains("<PackageReadmeFile>README.md</PackageReadmeFile>", LibraryCsprojText);
    }

    [Fact]
    [Trait("Requirement", "DOC-031")]
    public void LibraryCsproj_PacksTheD1FileDirectly_NotACopy()
    {
        // The single-source requirement: the packed item points at ../README.md (D1,
        // one directory up — DR-0018 D-M11-2), never at a same-directory duplicate.
        Assert.Contains("<None Include=\"../README.md\" Pack=\"true\"", LibraryCsprojText);
        Assert.False(
            File.Exists(Path.Combine(RepositoryRoot, "dotnet", "BastionVault.IntegrationSdk", "README.md")),
            "a README.md next to the csproj would be a second source D1 could drift from");
    }
}
