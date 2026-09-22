namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// The environment-variable contract of <c>specifications/test-matrix.json</c> (<c>environment</c>
/// block) and the mode table at <c>specifications/15-testing-requirements.md</c> lines 140-143.
/// Every variable the harness reads is named here once; nothing else reads
/// <see cref="Environment.GetEnvironmentVariable(string)"/> for a BASTIONVAULT_TEST_* name.
/// </summary>
internal static class TestEnvironment
{
    public const string Addr = "BASTIONVAULT_TEST_ADDR";
    public const string RootToken = "BASTIONVAULT_TEST_ROOT_TOKEN";
    public const string UnsealKeys = "BASTIONVAULT_TEST_UNSEAL_KEYS";
    public const string CaCert = "BASTIONVAULT_TEST_CACERT";
    public const string Namespace = "BASTIONVAULT_TEST_NAMESPACE";
    public const string TlsSkipVerify = "BASTIONVAULT_TEST_TLS_SKIP_VERIFY";
    public const string Bin = "BASTIONVAULT_TEST_BIN";
    public const string Image = "BASTIONVAULT_TEST_IMAGE";

    /// <summary>
    /// Not part of the matrix contract. Forces the ITG-003 "no server" path so it can be
    /// exercised on demand (acceptance criterion 2) without unsetting a developer's whole
    /// environment or hiding the <c>bvault</c> binary.
    /// </summary>
    public const string ForceUnavailable = "BASTIONVAULT_TEST_FORCE_UNAVAILABLE";

    /// <summary>
    /// Not part of the matrix contract. Downgrades ITG-002's below-minimum <b>failure</b> to a
    /// loud NON-CONFORMANT banner so the suite can be developed against a server older than the
    /// matrix minimum. It never makes an old server "supported": the run summary states that
    /// conformance is not claimed. See D-M12-9.
    /// </summary>
    public const string AllowUnsupportedVersion = "BASTIONVAULT_TEST_ALLOW_UNSUPPORTED_VERSION";

    public static string? Get(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static bool Flag(string name)
    {
        string? value = Get(name);
        return value is "1" or "true" or "TRUE" or "True" or "yes";
    }
}
