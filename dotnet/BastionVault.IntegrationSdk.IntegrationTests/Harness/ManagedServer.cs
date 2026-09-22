using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// Managed mode (mode table row 2): the harness starts a single-node server, initialises it with
/// the matrix's <c>initShares</c>/<c>initThreshold</c>, unseals it, captures the root token and
/// owns its teardown.
/// <para>
/// <b>Transport choice.</b> The managed server listens as plain HTTP on 127.0.0.1. CNF-035 allows
/// loopback HTTP without <c>AllowInsecureHttp</c>, so ITG-005's "MUST NOT need
/// <c>BASTIONVAULT_TEST_TLS_SKIP_VERIFY</c> in managed mode" is satisfied structurally rather than
/// by generating a CA and teaching every scenario to trust it. The rejected alternative - a
/// harness-generated self-signed CA - buys one thing the mock suite already covers offline
/// (TST-020's CA pinning, hostname mismatch, mTLS) at the cost of certificate plumbing in every
/// managed run. See D-M12-8.
/// </para>
/// </summary>
internal abstract class ManagedServer : IAsyncDisposable
{
    protected ManagedServer(string address, string workDirectory)
    {
        Address = address;
        WorkDirectory = workDirectory;
    }

    public string Address { get; }

    protected string WorkDirectory { get; }

    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Waits until the server answers <c>sys/health</c> at all. Any HTTP status counts: a fresh
    /// server answers 501 (uninitialised), and treating that as "not up yet" would time out every
    /// managed run.
    /// </summary>
    protected async Task WaitUntilListeningAsync(TimeSpan timeout, Func<string> diagnostics, CancellationToken cancellationToken)
    {
        using HttpClient probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Exception? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using HttpResponseMessage response = await probe.GetAsync(new Uri($"{Address}/v1/sys/health"), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                last = ex;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"managed server at {Address} did not accept a connection within " +
            $"{timeout.TotalSeconds:0} s (test-matrix.json managedServer.startupTimeoutSeconds).{Environment.NewLine}" +
            diagnostics(),
            last);
    }

    protected static int FreeLoopbackPort()
    {
        // test-matrix.json managedServer.listen is "127.0.0.1:0". The server writes no
        // port file, so the harness has to pick the port itself and hand it over: bind
        // :0, read what the kernel chose, release it, start the server on it.
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    protected static string BuildConfig(string dataPath, string listenAddress, string apiAddress)
    {
        string[] lines = new[]
        {
            "storage \"file\" {",
            "  path = \"" + dataPath + "\"",
            "}",
            string.Empty,
            "listener \"tcp\" {",
            "  address = \"" + listenAddress + "\"",
            "  tls_disable = true",
            "}",
            string.Empty,
            "api_addr = \"" + apiAddress + "\"",
            "disable_mlock = true",
            string.Empty,
        };

        return string.Join("\n", lines);
    }

    protected static void TryDeleteDirectory(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}

/// <summary>Managed mode over a local <c>bvault</c> binary (<c>BASTIONVAULT_TEST_BIN</c>).</summary>
internal sealed class ManagedBinaryServer : ManagedServer
{
    private readonly Process process;
    private readonly string logPath;

    private ManagedBinaryServer(Process process, string address, string workDirectory, string logPath)
        : base(address, workDirectory)
    {
        this.process = process;
        this.logPath = logPath;
    }

    public static async Task<ManagedBinaryServer> StartAsync(
        string binaryPath,
        ManagedServerSettings settings,
        string runId,
        CancellationToken cancellationToken)
    {
        string work = Path.Combine(Path.GetTempPath(), $"bvault-it-{runId}");
        _ = Directory.CreateDirectory(Path.Combine(work, "data"));

        int port = FreeLoopbackPort();
        string address = $"http://127.0.0.1:{port}";
        string configPath = Path.Combine(work, "server.hcl");
        File.WriteAllText(
            configPath,
            BuildConfig(Path.Combine(work, "data"), $"127.0.0.1:{port}", address));

        string logFile = Path.Combine(work, "server.log");
        ProcessStartInfo startInfo = new ProcessStartInfo(binaryPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = work,
        };
        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add($"--config={configPath}");
        startInfo.ArgumentList.Add("--log-level=warn");

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start '{binaryPath} server'");

        // The server log is written to a file and never to the test report: it is the one artefact
        // most likely to contain server-side material, and ITG-021 is a run-wide assertion.
        StreamWriter writer = new StreamWriter(logFile, append: true) { AutoFlush = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { writer.WriteLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { writer.WriteLine(e.Data); } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        ManagedBinaryServer server = new ManagedBinaryServer(process, address, work, logFile);
        try
        {
            await server.WaitUntilListeningAsync(
                settings.StartupTimeout,
                () => server.TailLog(),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return server;
    }

    public string TailLog()
    {
        try
        {
            string[] lines = File.ReadAllLines(logPath);
            return string.Join(Environment.NewLine, lines.TakeLast(20));
        }
        catch (IOException)
        {
            return "(server log unavailable)";
        }
    }

    public override ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already reaped.
        }
        finally
        {
            process.Dispose();
            TryDeleteDirectory(WorkDirectory);
        }

        return ValueTask.CompletedTask;
    }
}
