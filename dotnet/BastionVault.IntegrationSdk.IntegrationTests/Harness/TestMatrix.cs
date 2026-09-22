using System.Text.Json;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-002 / ITG-030: the pinned server matrix, read from <c>specifications/test-matrix.json</c>.
/// The file is the specification's, not the harness's: it is read and never written (D-M12-2).
/// </summary>
internal sealed class TestMatrix
{
    private TestMatrix(
        ServerVersion minimumVersion,
        string minimumImage,
        string latestImage,
        ManagedServerSettings managedServer)
    {
        MinimumVersion = minimumVersion;
        MinimumImage = minimumImage;
        LatestImage = latestImage;
        ManagedServer = managedServer;
    }

    public ServerVersion MinimumVersion { get; }

    public string MinimumImage { get; }

    public string LatestImage { get; }

    public ManagedServerSettings ManagedServer { get; }

    public static TestMatrix Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "test-matrix.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"ITG-002: the pinned server matrix is missing from the test output at '{path}'. " +
                "The project links specifications/test-matrix.json; a run without it cannot state " +
                "which server version it is conformant against.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        string? minimumVersionText = null;
        string minimumImage = string.Empty;
        string latestImage = string.Empty;
        foreach (JsonElement server in root.GetProperty("servers").EnumerateArray())
        {
            string? id = server.GetProperty("id").GetString();
            string image = server.TryGetProperty("image", out JsonElement img) ? img.GetString() ?? string.Empty : string.Empty;
            if (id == "minimum")
            {
                minimumVersionText = server.GetProperty("version").GetString();
                minimumImage = image;
            }
            else if (id == "latest")
            {
                latestImage = image;
            }
        }

        if (!ServerVersion.TryParse(minimumVersionText, out ServerVersion? minimum))
        {
            throw new InvalidOperationException(
                $"ITG-002: the matrix's 'minimum' entry is not a concrete version ('{minimumVersionText}'). " +
                "A moving tag cannot answer whether a resolved server is supported.");
        }

        JsonElement managed = root.GetProperty("managedServer");
        ManagedServerSettings settings = new ManagedServerSettings(
            Storage: managed.GetProperty("storage").GetString() ?? "file",
            InitShares: managed.GetProperty("initShares").GetInt32(),
            InitThreshold: managed.GetProperty("initThreshold").GetInt32(),
            Listen: managed.GetProperty("listen").GetString() ?? "127.0.0.1:0",
            StartupTimeout: TimeSpan.FromSeconds(managed.GetProperty("startupTimeoutSeconds").GetInt32()),
            DosDefaults: ReadDos(managed.GetProperty("dosConfigDefaults")));

        return new TestMatrix(minimum, minimumImage, latestImage, settings);
    }

    private static DosDefaults ReadDos(JsonElement element)
    {
        return new(
        element.GetProperty("window_secs").GetInt64(),
        element.GetProperty("max_requests").GetInt64(),
        element.GetProperty("auth_max_requests").GetInt64(),
        element.GetProperty("ban_secs").GetInt64());
    }
}

internal sealed record ManagedServerSettings(
    string Storage,
    int InitShares,
    int InitThreshold,
    string Listen,
    TimeSpan StartupTimeout,
    DosDefaults DosDefaults);

internal sealed record DosDefaults(long WindowSecs, long MaxRequests, long AuthMaxRequests, long BanSecs);
