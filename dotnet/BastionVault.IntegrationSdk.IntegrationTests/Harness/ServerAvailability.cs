using System.Diagnostics;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-003: decides, <b>without starting anything</b>, whether either provisioning mode of the
/// mode table (<c>specifications/15-testing-requirements.md</c> lines 140-143) is possible.
/// <para>
/// The probe is cheap and side-effect free on purpose: <see cref="IntegrationFactAttribute"/>
/// evaluates it at xUnit <i>discovery</i> time, which is what turns "no server" into a real,
/// counted skip with the exact specified reason rather than a failure or a silent pass.
/// </para>
/// </summary>
internal static class ServerAvailability
{
    /// <summary>The exact reason string ITG-003 mandates. Not to be reworded.</summary>
    public const string UnavailableReason = "no BastionVault test server available";

    private static readonly Lazy<Probe> Cached = new(Detect, isThreadSafe: true);

    public static Probe Current => Cached.Value;

    internal sealed record Probe(TestServerMode? Mode, string? BinaryPath, string? ContainerRuntime)
    {
        public bool IsAvailable => Mode is not null;
    }

    private static Probe Detect()
    {
        if (TestEnvironment.Flag(TestEnvironment.ForceUnavailable))
        {
            return new Probe(null, null, null);
        }

        if (TestEnvironment.Get(TestEnvironment.Addr) is not null)
        {
            return new Probe(TestServerMode.External, null, null);
        }

        // Managed mode. Preference order, and the reason for it:
        //  1. BASTIONVAULT_TEST_BIN  - an operator naming a binary has named the server they mean.
        //  2. a container runtime    - reproducible and version-pinned by the matrix image.
        //  3. `bvault` on PATH       - the mode table says "a bvault binary ... is available";
        //     a binary on PATH is available, and refusing it would skip a suite that could run.
        string? named = TestEnvironment.Get(TestEnvironment.Bin);
        if (named is not null && File.Exists(named))
        {
            return new Probe(TestServerMode.Managed, named, null);
        }

        string? runtime = FindContainerRuntime();
        if (runtime is not null)
        {
            return new Probe(TestServerMode.Managed, null, runtime);
        }

        string? onPath = FindOnPath("bvault");
        return onPath is not null
            ? new Probe(TestServerMode.Managed, onPath, null)
            : new Probe(null, null, null);
    }

    private static string? FindContainerRuntime()
    {
        foreach (string candidate in new[] { "docker", "podman" })
        {
            string? exe = FindOnPath(candidate);
            if (exe is null)
            {
                continue;
            }

            // A CLI on PATH is not a runtime: Docker Desktop leaves the CLI behind with the daemon
            // stopped, which is exactly the state this machine was in when M12 was framed
            // (DR-0019). Only a responding daemon counts.
            if (RunQuietly(exe, "info", TimeSpan.FromSeconds(10)))
            {
                return exe;
            }
        }

        return null;
    }

    private static bool RunQuietly(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    internal static string? FindOnPath(string command)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), command);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
