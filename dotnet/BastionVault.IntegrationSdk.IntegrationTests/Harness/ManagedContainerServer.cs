using System.Diagnostics;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// Managed mode over a container runtime. The image is the matrix's pinned <c>minimum</c> entry
/// unless <c>BASTIONVAULT_TEST_IMAGE</c> overrides it, which is how ITG-002's "use the version
/// pinned in test-matrix.json" is met when a registry is reachable.
/// <para>
/// <b>Not exercised.</b> This path has never been run: the only machine M12 was developed on has
/// the docker CLI with the daemon stopped, and <c>ghcr.io/ffquintella/bastionvault:0.42.0</c>
/// answers <c>denied</c> without credentials (DR-0019's probe table). It is written to the same
/// contract as the binary path so slice 7's CI job has something to call, and it is reported as
/// untested rather than as working.
/// </para>
/// </summary>
internal sealed class ManagedContainerServer : ManagedServer
{
    private readonly string runtime;
    private readonly string containerName;

    private ManagedContainerServer(string runtime, string containerName, string address, string workDirectory)
        : base(address, workDirectory)
    {
        this.runtime = runtime;
        this.containerName = containerName;
    }

    public static async Task<ManagedContainerServer> StartAsync(
        string runtime,
        string image,
        ManagedServerSettings settings,
        string runId,
        CancellationToken cancellationToken)
    {
        string work = Path.Combine(Path.GetTempPath(), $"bvault-it-{runId}");
        _ = Directory.CreateDirectory(Path.Combine(work, "data"));

        int port = FreeLoopbackPort();
        string address = $"http://127.0.0.1:{port}";
        string configPath = Path.Combine(work, "server.hcl");
        File.WriteAllText(configPath, BuildConfig("/bvault/data", "0.0.0.0:8200", address));

        string name = $"bvault-it-{runId}";
        string[] arguments = new[]
        {
            "run", "--detach", "--name", name,
            "--publish", $"127.0.0.1:{port}:8200",
            "--volume", $"{work}:/bvault",
            image,
            "server", "--config=/bvault/server.hcl", "--log-level=warn",
        };

        (int exitCode, string? output) = await RunAsync(runtime, arguments, TimeSpan.FromMinutes(3), cancellationToken)
            .ConfigureAwait(false);
        if (exitCode != 0)
        {
            TryDeleteDirectory(work);
            throw new InvalidOperationException(
                $"'{runtime} run {image}' failed with exit code {exitCode}: {output}");
        }

        ManagedContainerServer server = new ManagedContainerServer(runtime, name, address, work);
        try
        {
            await server.WaitUntilListeningAsync(
                settings.StartupTimeout,
                () => server.Logs(),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return server;
    }

    public string Logs()
    {
        (int _, string? output) = RunAsync(runtime, ["logs", "--tail", "20", containerName], TimeSpan.FromSeconds(15), CancellationToken.None)
            .GetAwaiter().GetResult();
        return output;
    }

    public override async ValueTask DisposeAsync()
    {
        _ = await RunAsync(runtime, ["rm", "--force", containerName], TimeSpan.FromSeconds(60), CancellationToken.None)
            .ConfigureAwait(false);
        TryDeleteDirectory(WorkDirectory);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start '{fileName}'");

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        string stdout = await process.StandardOutput.ReadToEndAsync(timeoutSource.Token).ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync(timeoutSource.Token).ConfigureAwait(false);
        await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);

        return (process.ExitCode, (stdout + stderr).Trim());
    }
}
