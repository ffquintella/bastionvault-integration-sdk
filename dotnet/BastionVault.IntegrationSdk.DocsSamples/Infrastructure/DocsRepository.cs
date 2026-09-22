namespace BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

/// <summary>
/// Locates the repository on disk from the test assembly's output directory, so the sample
/// embedding check (see <c>SampleEmbeddingTests</c>) can read both <c>docs/**</c> and this
/// project's own <c>.cs</c> files at run time rather than at build time.
/// </summary>
internal static class DocsRepository
{
    /// <summary>The repository root: the nearest ancestor holding both markers.</summary>
    public static DirectoryInfo Root { get; } = FindRoot();

    /// <summary>The <c>docs/</c> tree, the home of D2-D11 and D13 (DR-0018 D-M11-2).</summary>
    public static DirectoryInfo Docs => new(Path.Combine(Root.FullName, "docs"));

    /// <summary>This project's directory: every sample region lives under it.</summary>
    public static DirectoryInfo SamplesProject =>
        new(Path.Combine(Root.FullName, "dotnet", "BastionVault.IntegrationSdk.DocsSamples"));

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            bool hasSpecifications = Directory.Exists(Path.Combine(directory.FullName, "specifications"));
            bool hasAgentsDocument = File.Exists(Path.Combine(directory.FullName, "agents.md"));
            if (hasSpecifications && hasAgentsDocument)
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No repository root (a directory holding both `specifications/` and `agents.md`) above '{AppContext.BaseDirectory}'.");
    }
}
