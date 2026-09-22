using System.Text.RegularExpressions;

namespace BastionVault.IntegrationSdk.DocsSamples.Infrastructure;

/// <summary>One <c>docs:begin</c> … <c>docs:end</c> region in a sample source file.</summary>
/// <param name="Id">The sample id, <c>&lt;document&gt;/&lt;name&gt;</c>.</param>
/// <param name="File">The source file, repository-relative.</param>
/// <param name="FirstLine">1-based line number of the <c>docs:begin</c> marker.</param>
/// <param name="Text">The region, dedented, with no trailing newline.</param>
/// <param name="EnclosingMethod">The name of the method the region sits in, or <see langword="null"/>.</param>
/// <param name="EnclosingMethodIsTest">Whether that method carries <c>[Fact]</c> or <c>[Theory]</c>.</param>
public sealed record SampleRegion(
    string Id,
    string File,
    int FirstLine,
    string Text,
    string? EnclosingMethod,
    bool EnclosingMethodIsTest);

/// <summary>One <c>&lt;!-- docs:sample id --&gt;</c> anchor and the fenced block under it.</summary>
/// <param name="Id">The sample id the anchor names.</param>
/// <param name="File">The markdown file, repository-relative.</param>
/// <param name="AnchorLine">1-based line number of the anchor comment.</param>
/// <param name="FenceLine">0-based index of the opening fence line.</param>
/// <param name="ClosingFenceLine">0-based index of the closing fence line.</param>
/// <param name="Text">The block's body, with no trailing newline.</param>
public sealed record SampleAnchor(
    string Id,
    string File,
    int AnchorLine,
    int FenceLine,
    int ClosingFenceLine,
    string Text);

/// <summary>
/// The parser behind the sample-embedding contract (DOC-003, DR-0018 D-M11-3): it reads the
/// regions out of this project's C# and the anchors out of <c>docs/**/*.md</c>, so a test can
/// assert the two are byte-identical.
/// </summary>
/// <remarks>
/// The syntax is fixed here and documented in <c>docs/README.md</c>. Later slices use it as it
/// stands rather than inventing a second one.
/// </remarks>
public static partial class SampleRegions
{
    /// <summary>The fence language every executed sample uses.</summary>
    public const string FenceLanguage = "csharp";

    [GeneratedRegex(@"^\s*//\s*docs:begin\s+(?<id>[A-Za-z0-9][A-Za-z0-9/._-]*)\s*$")]
    private static partial Regex BeginMarker();

    [GeneratedRegex(@"^\s*//\s*docs:end\s+(?<id>[A-Za-z0-9][A-Za-z0-9/._-]*)\s*$")]
    private static partial Regex EndMarker();

    [GeneratedRegex(@"^<!--\s*docs:sample\s+(?<id>[A-Za-z0-9][A-Za-z0-9/._-]*)\s*-->\s*$")]
    private static partial Regex AnchorComment();

    [GeneratedRegex(@"^\s*(?:public|internal|private|protected)[^;=]*\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex MethodSignature();

    /// <summary>Every region in every <c>.cs</c> file of the docs-samples project.</summary>
    public static IReadOnlyList<SampleRegion> ReadRegions()
    {
        List<SampleRegion> regions = [];
        foreach (string path in EnumerateSourceFiles())
        {
            regions.AddRange(ReadRegions(path));
        }

        return regions;
    }

    /// <summary>Every <c>.cs</c> file of the docs-samples project, excluding build output.</summary>
    public static IEnumerable<string> EnumerateSourceFiles()
    {
        return Directory
            .EnumerateFiles(DocsRepository.SamplesProject.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    /// <summary>Every markdown file under <c>docs/</c>.</summary>
    public static IEnumerable<string> EnumerateDocuments()
    {
        if (!DocsRepository.Docs.Exists)
        {
            return [];
        }

        return Directory
            .EnumerateFiles(DocsRepository.Docs.FullName, "*.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static IEnumerable<SampleRegion> ReadRegions(string path)
    {
        string[] lines = File.ReadAllLines(path);
        string relative = Relative(path);
        List<SampleRegion> regions = [];

        for (int index = 0; index < lines.Length; index++)
        {
            Match begin = BeginMarker().Match(lines[index]);
            if (!begin.Success)
            {
                continue;
            }

            string id = begin.Groups["id"].Value;
            int end = -1;
            for (int scan = index + 1; scan < lines.Length; scan++)
            {
                Match candidate = EndMarker().Match(lines[scan]);
                if (!candidate.Success)
                {
                    continue;
                }

                if (candidate.Groups["id"].Value != id)
                {
                    throw new InvalidOperationException(
                        $"{relative}:{scan + 1}: `docs:end {candidate.Groups["id"].Value}` closes `docs:begin {id}` opened at line {index + 1}. Markers must pair by id.");
                }

                end = scan;
                break;
            }

            if (end < 0)
            {
                throw new InvalidOperationException($"{relative}:{index + 1}: `docs:begin {id}` has no matching `docs:end {id}`.");
            }

            (string? method, bool isTest) = FindEnclosingMethod(lines, index);
            regions.Add(new SampleRegion(
                id,
                relative,
                index + 1,
                Dedent(lines[(index + 1)..end]),
                method,
                isTest));
            index = end;
        }

        return regions;
    }

    private static (string? Name, bool IsTest) FindEnclosingMethod(string[] lines, int regionStart)
    {
        for (int index = regionStart - 1; index >= 0; index--)
        {
            Match signature = MethodSignature().Match(lines[index]);
            if (!signature.Success)
            {
                continue;
            }

            bool isTest = false;
            for (int attribute = index - 1; attribute >= 0; attribute--)
            {
                string text = lines[attribute].Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (!text.StartsWith('['))
                {
                    break;
                }

                if (text.Contains("Fact", StringComparison.Ordinal) || text.Contains("Theory", StringComparison.Ordinal))
                {
                    isTest = true;
                }
            }

            return (signature.Groups["name"].Value, isTest);
        }

        return (null, false);
    }

    /// <summary>Every anchored sample block in one markdown file.</summary>
    public static IReadOnlyList<SampleAnchor> ReadAnchors(string path)
    {
        string[] lines = File.ReadAllLines(path);
        string relative = Relative(path);
        List<SampleAnchor> anchors = [];

        for (int index = 0; index < lines.Length; index++)
        {
            Match anchor = AnchorComment().Match(lines[index]);
            if (!anchor.Success)
            {
                continue;
            }

            string id = anchor.Groups["id"].Value;
            int fence = index + 1;
            if (fence >= lines.Length || lines[fence].TrimEnd() != $"```{FenceLanguage}")
            {
                throw new InvalidOperationException(
                    $"{relative}:{index + 1}: `docs:sample {id}` must be followed immediately by an opening ```{FenceLanguage} fence.");
            }

            int closing = -1;
            for (int scan = fence + 1; scan < lines.Length; scan++)
            {
                if (lines[scan].TrimEnd() == "```")
                {
                    closing = scan;
                    break;
                }
            }

            if (closing < 0)
            {
                throw new InvalidOperationException($"{relative}:{fence + 1}: the fence opened for `{id}` is never closed.");
            }

            anchors.Add(new SampleAnchor(id, relative, index + 1, fence, closing, string.Join('\n', lines[(fence + 1)..closing])));
            index = closing;
        }

        return anchors;
    }

    /// <summary>Every fenced <c>csharp</c> block in one file, by the 0-based index of its opening fence.</summary>
    public static IReadOnlyList<int> ReadCSharpFences(string path)
    {
        string[] lines = File.ReadAllLines(path);
        List<int> fences = [];
        bool inside = false;
        for (int index = 0; index < lines.Length; index++)
        {
            string text = lines[index].TrimEnd();
            if (inside)
            {
                if (text == "```")
                {
                    inside = false;
                }

                continue;
            }

            if (!text.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            inside = true;
            if (text == $"```{FenceLanguage}")
            {
                fences.Add(index);
            }
        }

        return fences;
    }

    /// <summary>Repository-relative, forward-slashed.</summary>
    public static string Relative(string path) =>
        Path.GetRelativePath(DocsRepository.Root.FullName, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Dedent(IReadOnlyList<string> lines)
    {
        int indent = int.MaxValue;
        foreach (string line in lines)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            indent = Math.Min(indent, line.Length - line.TrimStart().Length);
        }

        if (indent == int.MaxValue)
        {
            indent = 0;
        }

        return string.Join(
            '\n',
            lines.Select(line => line.Trim().Length == 0 ? string.Empty : line[indent..].TrimEnd()));
    }
}
