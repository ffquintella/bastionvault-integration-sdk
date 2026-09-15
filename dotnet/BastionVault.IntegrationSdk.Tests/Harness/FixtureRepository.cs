using System.Text.Json;
using Json.Schema;

namespace BastionVault.IntegrationSdk.Tests.Harness;

public sealed class FixtureValidationException : InvalidOperationException
{
    public FixtureValidationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class FixtureDocument
{
    internal FixtureDocument(string path, JsonElement document)
    {
        Path = path;
        Json = document.Clone();
        Id = Json.GetProperty("id").GetString()!;
        Level = Json.GetProperty("level").GetString()!;
        Sections = Json.GetProperty("sections").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
    }

    public string Id { get; }

    public string Level { get; }

    public IReadOnlyList<string> Sections { get; }

    public string Path { get; }

    public JsonElement Json { get; }

    public JsonElement GetRequired(string propertyName)
    {
        return Json.GetProperty(propertyName);
    }

    public bool TryGet(string propertyName, out JsonElement value)
    {
        return Json.TryGetProperty(propertyName, out value);
    }
}

public sealed class FixtureRepository
{
    private static readonly Lazy<JsonSchema> SharedSchema = new(LoadSchema);
    private readonly JsonSchema schema;
    private readonly string fixturesDirectory;

    public FixtureRepository(string? startingLocation = null)
    {
        RepositoryRoot = FindRepositoryRoot(startingLocation ?? typeof(FixtureRepository).Assembly.Location);
        fixturesDirectory = System.IO.Path.Combine(RepositoryRoot, "specifications", "fixtures");

        schema = SharedSchema.Value;
    }

    public string RepositoryRoot { get; }

    /// <summary>
    /// The one committed fixture schema, shared. Exposed so a test can validate a synthetic
    /// document against the <b>same</b> schema every fixture is validated against — and shared
    /// rather than reloaded because the document carries an <c>$id</c>, and building it twice
    /// fails on the schema registry rather than silently producing a second copy.
    /// </summary>
    public static JsonSchema Schema => SharedSchema.Value;

    public static string FindRepositoryRoot(string startingLocation)
    {
        string current = File.Exists(startingLocation)
            ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(startingLocation))!
            : System.IO.Path.GetFullPath(startingLocation);
        string marker = System.IO.Path.Combine("specifications", "fixtures", "schema", "fixture.schema.json");

        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(System.IO.Path.Combine(current, marker)))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{startingLocation}'. Expected '{marker}' in an ancestor directory.");
    }

    public IEnumerable<FixtureDocument> EnumerateAll()
    {
        string schemaPath = System.IO.Path.Combine(fixturesDirectory, "schema", "fixture.schema.json");
        return Directory.EnumerateFiles(fixturesDirectory, "*.json", SearchOption.AllDirectories)
            .Where(path => !string.Equals(System.IO.Path.GetFullPath(path), schemaPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(LoadPath);
    }

    public IEnumerable<FixtureDocument> FilterByLevel(string level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        return EnumerateAll().Where(fixture => string.Equals(fixture.Level, level, StringComparison.Ordinal));
    }

    public IEnumerable<FixtureDocument> FilterBySections(IEnumerable<string> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        HashSet<string> wanted = sections.ToHashSet(StringComparer.Ordinal);
        return EnumerateAll().Where(fixture => fixture.Sections.Any(wanted.Contains));
    }

    public IEnumerable<FixtureDocument> FilterBySections(params string[] sections)
    {
        return FilterBySections((IEnumerable<string>)sections);
    }

    public FixtureDocument LoadById(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        string? path = Directory.EnumerateFiles(fixturesDirectory, "*.json", SearchOption.AllDirectories)
            .Where(candidate => !candidate.Contains($"{System.IO.Path.DirectorySeparatorChar}schema{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(candidate => string.Equals(System.IO.Path.GetFileNameWithoutExtension(candidate), id, StringComparison.Ordinal)) ?? throw new FileNotFoundException($"Fixture with id '{id}' was not found under '{fixturesDirectory}'.");
        return LoadPath(path);
    }

    private FixtureDocument LoadPath(string path)
    {
        string fixtureId = System.IO.Path.GetFileNameWithoutExtension(path);
        try
        {
            string text = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement instance = document.RootElement.Clone();
            EvaluationResults results = schema.Evaluate(instance);
            if (!results.IsValid)
            {
                throw new FixtureValidationException(
                    $"Fixture '{fixtureId}' failed JSON Schema validation. Violated constraint: {results}");
            }

            return new FixtureDocument(path, instance);
        }
        catch (FixtureValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            throw new FixtureValidationException($"Fixture '{fixtureId}' could not be loaded or validated: {exception.Message}", exception);
        }
    }

    private static JsonSchema LoadSchema()
    {
        string schemaPath = System.IO.Path.Combine(
            FindRepositoryRoot(typeof(FixtureRepository).Assembly.Location),
            "specifications",
            "fixtures",
            "schema",
            "fixture.schema.json");
        try
        {
            return JsonSchema.FromText(File.ReadAllText(schemaPath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or JsonSchemaException)
        {
            throw new FixtureValidationException($"Could not load fixture schema at '{schemaPath}'.", exception);
        }
    }
}
