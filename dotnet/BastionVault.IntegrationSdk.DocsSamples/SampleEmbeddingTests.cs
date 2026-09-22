using System.Text;
using BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

namespace BastionVault.IntegrationSdk.DocsSamples;

/// <summary>
/// The drift check behind DOC-003 and DR-0018 D-M11-3: every fenced <c>csharp</c> block in
/// <c>docs/</c> is byte-identical to a region of compiled, executed sample source, and no block,
/// region or document escapes the check in either direction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Compare, do not silently regenerate.</b> The error catalogue's gate runs a generator and
/// then <c>git diff --exit-code</c>, which works because its source (Appendix B) is prose a human
/// owns and its output is code nobody edits by hand. Here both sides are hand-written and both
/// sides are read by humans, so a silent regeneration would resolve every disagreement in favour
/// of the code and quietly rewrite a guide's prose-adjacent example. The check therefore
/// <i>compares</i> and fails, and offers regeneration as an explicit, opt-in repair:
/// </para>
/// <code>BASTIONVAULT_DOCS_SAMPLES=update dotnet test dotnet/BastionVault.IntegrationSdk.DocsSamples/BastionVault.IntegrationSdk.DocsSamples.csproj</code>
/// <para>
/// CI never sets that variable, so on CI the behaviour is exactly the regenerate-then-diff gate's:
/// a mismatch is a failure, never an edit.
/// </para>
/// </remarks>
public sealed class SampleEmbeddingTests
{
    private const string UpdateVariable = "BASTIONVAULT_DOCS_SAMPLES";

    private static bool UpdateRequested =>
        string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "update", StringComparison.Ordinal);

    [Fact]
    public void Every_documented_sample_is_byte_identical_to_its_compiled_source()
    {
        Dictionary<string, SampleRegion> regions = [];
        foreach (SampleRegion region in SampleRegions.ReadRegions())
        {
            Assert.False(
                regions.ContainsKey(region.Id),
                $"Sample id '{region.Id}' is defined twice: {regions.GetValueOrDefault(region.Id)?.File} and {region.File}:{region.FirstLine}.");
            regions[region.Id] = region;
        }

        List<string> failures = [];
        HashSet<string> referenced = [];

        foreach (string document in SampleRegions.EnumerateDocuments())
        {
            IReadOnlyList<SampleAnchor> anchors = SampleRegions.ReadAnchors(document);
            IReadOnlyList<int> fences = SampleRegions.ReadCSharpFences(document);

            foreach (int fence in fences.Where(fence => anchors.All(anchor => anchor.FenceLine != fence)))
            {
                failures.Add(
                    $"{SampleRegions.Relative(document)}:{fence + 1}: a ```{SampleRegions.FenceLanguage} block with no `<!-- docs:sample <id> -->` anchor above it. "
                    + "DOC-003 admits no unexecuted C# in D1-D10: anchor it to a region of the docs-samples project, or make it a non-C# fence.");
            }

            bool rewritten = false;
            string[] lines = File.ReadAllLines(document);

            foreach (SampleAnchor anchor in anchors.OrderByDescending(anchor => anchor.FenceLine))
            {
                _ = referenced.Add(anchor.Id);

                if (!regions.TryGetValue(anchor.Id, out SampleRegion? region))
                {
                    failures.Add(
                        $"{anchor.File}:{anchor.AnchorLine}: `docs:sample {anchor.Id}` names no region. "
                        + $"Add `// docs:begin {anchor.Id}` / `// docs:end {anchor.Id}` around the sample in dotnet/BastionVault.IntegrationSdk.DocsSamples/Samples/.");
                    continue;
                }

                if (anchor.Text == region.Text)
                {
                    continue;
                }

                if (UpdateRequested)
                {
                    lines = [.. lines[..(anchor.FenceLine + 1)], .. region.Text.Split('\n'), .. lines[anchor.ClosingFenceLine..]];
                    rewritten = true;
                    continue;
                }

                failures.Add(Describe(anchor, region));
            }

            if (rewritten)
            {
                File.WriteAllText(document, string.Join('\n', lines) + '\n');
            }
        }

        foreach (SampleRegion region in regions.Values.Where(region => !referenced.Contains(region.Id)))
        {
            failures.Add(
                $"{region.File}:{region.FirstLine}: region `{region.Id}` is shown by no document. "
                + "A sample nobody reads is dead code; delete it or anchor it.");
        }

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    [Fact]
    public void Every_sample_region_sits_in_code_the_test_run_executes()
    {
        string[] sources = [.. SampleRegions.EnumerateSourceFiles().Select(File.ReadAllText)];
        List<string> failures = [];

        foreach (SampleRegion region in SampleRegions.ReadRegions())
        {
            if (region.EnclosingMethod is null)
            {
                failures.Add($"{region.File}:{region.FirstLine}: region `{region.Id}` is not inside a method.");
                continue;
            }

            if (region.EnclosingMethodIsTest)
            {
                continue;
            }

            int callSites = sources.Sum(source => Occurrences(source, region.EnclosingMethod + "("));
            if (callSites < 2)
            {
                failures.Add(
                    $"{region.File}:{region.FirstLine}: region `{region.Id}` sits in `{region.EnclosingMethod}`, which is neither a [Fact]/[Theory] "
                    + "nor called from anywhere. DOC-003 requires samples to be executed, not merely compiled.");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Describe(SampleAnchor anchor, SampleRegion region)
    {
        string[] documented = anchor.Text.Split('\n');
        string[] compiled = region.Text.Split('\n');
        StringBuilder message = new();
        _ = message.AppendLine(
            $"Sample `{anchor.Id}` has drifted: {anchor.File}:{anchor.FenceLine + 1} is not byte-identical to {region.File}:{region.FirstLine}.");

        for (int index = 0; index < Math.Max(documented.Length, compiled.Length); index++)
        {
            string? left = index < documented.Length ? documented[index] : null;
            string? right = index < compiled.Length ? compiled[index] : null;
            if (left == right)
            {
                continue;
            }

            _ = message.AppendLine($"  line {index + 1}");
            _ = message.AppendLine($"    markdown: {left ?? "<absent>"}");
            _ = message.AppendLine($"    source:   {right ?? "<absent>"}");
        }

        _ = message.Append(
            $"  Fix the source or the document, then re-run. To take the source as authoritative: {UpdateVariable}=update dotnet test dotnet/BastionVault.IntegrationSdk.DocsSamples/BastionVault.IntegrationSdk.DocsSamples.csproj");
        return message.ToString();
    }
}
