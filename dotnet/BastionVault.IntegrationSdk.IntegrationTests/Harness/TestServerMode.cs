namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>ITG-001's <c>Mode</c>: the two rows of the provisioning mode table.</summary>
public enum TestServerMode
{
    /// <summary><c>BASTIONVAULT_TEST_ADDR</c> is set; the harness attaches to that server.</summary>
    External,

    /// <summary>The harness started, initialised, unsealed and owns the server for this run.</summary>
    Managed,
}
