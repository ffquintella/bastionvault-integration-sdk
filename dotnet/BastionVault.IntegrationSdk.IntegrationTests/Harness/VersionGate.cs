namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-002 (the run is against the pinned version) and ITG-031 (a scenario needing a newer
/// endpoint skips rather than fails).
/// </summary>
internal static class VersionGate
{
    /// <summary>
    /// Returns whether the resolved server meets the matrix minimum. Records, never decides:
    /// <see cref="Enforce"/> is what stops the run.
    /// </summary>
    public static bool Evaluate(TestServer server, TestMatrix matrix, RunReport report)
    {
        ServerVersion? parsed = server.ParsedVersion;
        if (parsed is null)
        {
            report.RecordWarning(
                $"sys/info reported version '{server.Version}', which is not a comparable version. " +
                "ITG-002's comparison against the matrix minimum cannot be made.");
            return false;
        }

        return parsed >= matrix.MinimumVersion;
    }

    /// <summary>
    /// The loud outcome. A server below <c>test-matrix.json</c>'s minimum fails the whole run:
    /// it is not an ITG-003 skip, because a server <i>is</i> available - the environment is simply
    /// not one the specification supports, and a suite that quietly passed against it would be
    /// claiming a conformance nobody measured (CLA-004). There is no override: the development
    /// escape hatch this used to have was removed once the server it was written for was gone
    /// (Strategic Orchestrator ruling, DR-0019 D-M12-15 Ruling B).
    /// </summary>
    public static void Enforce(TestServer server, TestMatrix matrix, bool conformant)
    {
        if (conformant)
        {
            return;
        }

        throw new ServerVersionNotSupportedException(
            $"ITG-002: the server at {server.Address} reports version '{server.Version}', which is " +
            $"below the minimum supported version '{matrix.MinimumVersion}' pinned in " +
            "specifications/test-matrix.json. The integration suite will not run against it: an " +
            "unsupported server cannot demonstrate conformance, and the specification is not the " +
            "side that changes (DR-0019 D-M12-2). Provide a supported server via " +
            $"{TestEnvironment.Addr}, {TestEnvironment.Bin} or {TestEnvironment.Image}.");
    }

    /// <summary>
    /// ITG-031. Called by a scenario that needs an endpoint introduced after the minimum version;
    /// skips with the mandated <c>requires server &gt;= X</c> reason and counts the skip.
    /// </summary>
    public static void Require(IntegrationHarness.Context context, string testName, string minimumVersion)
    {
        ServerVersion required = ServerVersion.Parse(minimumVersion);
        ServerVersion? actual = context.Server.ParsedVersion;
        if (actual is not null && actual >= required)
        {
            return;
        }

        context.Report.RecordVersionSkip(testName, minimumVersion, context.Server.Version);
        Skip.If(true, $"requires server >= {minimumVersion}");
    }
}

/// <summary>Thrown when ITG-002's version comparison refuses the run.</summary>
public sealed class ServerVersionNotSupportedException(string message) : Exception(message);
