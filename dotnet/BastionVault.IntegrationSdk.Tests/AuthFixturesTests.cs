using BastionVault.IntegrationSdk.Tests.Harness;
using BastionVault.IntegrationSdk.Tests.Harness.Operations;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// Runs every <c>auth.*</c> fixture (Appendix C) through the real
/// <see cref="BastionVault.IntegrationSdk.BastionVaultClient"/> (DR-0006, M2a), and the
/// <c>errors.format.one-line</c> fixture that drives <c>Auth.Token.Lookup</c> and has been pending
/// since M1c waiting for it.
/// </summary>
public sealed class AuthFixturesTests
{
    /// <summary>
    /// The M2a fixtures that go green in this slice: the five section-05 token-store fixtures plus
    /// <c>errors.format.one-line</c>, whose operation (<c>Auth.Token.Lookup</c>) lands here.
    /// </summary>
    private static readonly string[] Green =
    [
        "auth.token.lookup-self-remaining-ttl",
        "auth.token.lookup-unknown-404",
        "auth.token.renew-self-uses-renew-path",
        "auth.token.create-reserved-meta-client-side",
        "auth.token.revoke-self-clears-token",
        "errors.format.one-line",
    ];

    /// <summary>
    /// Every <c>auth.*</c> fixture that stays <c>pending</c> after M2a, with the milestone that
    /// owns it. D-M2-10's rule holds throughout: a pending fixture's owner is the milestone that
    /// lands its <b>operation</b>, not the one that lands its requirements.
    /// </summary>
    /// <remarks>
    /// <c>auth.token.lookup-self-no-token-client-side</c> is the one entry justified by
    /// <i>behaviour</i> rather than by a missing operation, and it is the reason this dictionary
    /// carries reasons at all. Its operation <c>Auth.Token.LookupSelf</c> lands in M2a, so by
    /// D-M2-10's rule it would stop being pending here — one slice before the CFG-020/ERR-022
    /// preflight it asserts exists, which is M2b's. Left to run it would go red at M2a's handback,
    /// read as a regression, and the cheapest-looking fix would be to weaken it (CLA-004). M2b's
    /// exit removes this entry and the fixture goes green.
    /// </remarks>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal)
    {
        ["auth.token.lookup-self-no-token-client-side"] = "operation exists; asserted behaviour is M2b",
        ["auth.userpass.login-ok"] = "Auth.Userpass.Login is M2b",
        ["auth.userpass.totp-required"] = "Auth.Userpass.Login is M2b",
        ["auth.userpass.locked"] = "Auth.Userpass.Login is M2b",
        ["auth.userpass.login-200-rejected"] = "Auth.Userpass.Login is M2b",
        ["auth.appid.login-ok-with-machine-token-and-namespace"] = "Auth.AppId.Login is M2b",
        ["auth.appid.invalid-secret-id-400"] = "Auth.AppId.Login is M2b",
        ["auth.appid.machine-token-required"] = "Auth.AppId.Login is M2b",
        ["auth.appid.gated-403"] = "Auth.AppId.Login is M2b",
        ["auth.appid.env-scope-derived"] = "Auth.AppId.Login is M2b",
        ["auth.cert.disabled-server"] = "Auth.Cert.Login is M6 (D-M2-5, D-M2-8)",
    };

    public static IEnumerable<object[]> Ids => LoadIds().Select(id => new object[] { id });

    private static IReadOnlyList<string> LoadIds()
    {
        FixtureRepository repository = new();
        return repository.EnumerateAll()
            .Where(fixture => fixture.Id.StartsWith("auth.", StringComparison.Ordinal))
            .Select(fixture => fixture.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static FixtureDriver Driver()
    {
        OperationRegistry registry = new();
        ClientConstructOperation.Register(registry);
        LogicalFixtureOperations.Register(registry);
        AuthFixtureOperations.Register(registry);
        // Only the behaviour-held entry needs the driver's held-pending list: every other entry is
        // pending because its operation is not registered, which the driver already reports.
        return new FixtureDriver(
            registry,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth.token.lookup-self-no-token-client-side"] =
                    Pending["auth.token.lookup-self-no-token-client-side"],
            });
    }

    [Theory]
    [Requirement("AUT-020")]
    [Requirement("AUT-080")]
    [Requirement("AUT-081")]
    [Requirement("AUT-082")]
    [Requirement("AUT-083")]
    [Requirement("AUT-084")]
    [Requirement("AUT-014")]
    [Requirement("TST-011")]
    [Trait("Requirement", "AUT-080")]
    [MemberData(nameof(Ids))]
    public void Auth_fixture_passes_against_real_sdk_code(string fixtureId)
    {
        FixtureRepository repository = new();
        FixtureDocument fixture = repository.LoadById(fixtureId);

        FixtureRunResult result = Driver().Run(fixture);

        Assert.Equal(
            Pending.ContainsKey(fixtureId) ? FixtureRunStatus.Pending : FixtureRunStatus.Passed,
            result.Status);
    }

    [Fact]
    [Requirement("ERR-002")]
    [Requirement("ERR-003")]
    [Trait("Requirement", "ERR-003")]
    public void Errors_format_one_line_goes_green_now_that_Auth_Token_Lookup_exists()
    {
        FixtureRepository repository = new();

        FixtureRunResult result = Driver().Run(repository.LoadById("errors.format.one-line"));

        Assert.Equal(FixtureRunStatus.Passed, result.Status);
        Assert.Equal("Auth.Token.Lookup", result.OperationName);
    }

    [Fact]
    [Requirement("TST-011")]
    [Requirement("TST-013")]
    [Trait("Requirement", "TST-013")]
    public void The_six_M2a_fixtures_are_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(6, Green.Length);

        // Every auth.* fixture is accounted for: green, or pending with a stated reason.
        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"auth fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // D-M2-10's one behaviour-justified entry, by its exact recorded reason.
        Assert.Equal(
            "operation exists; asserted behaviour is M2b",
            Pending["auth.token.lookup-self-no-token-client-side"]);
    }

    /// <summary>
    /// D-M2-7: the TST-051 assertion "is exactly as strong as TST-050 compliance". It is a
    /// substring search, so a fixture whose secrets the harvester cannot see is a fixture the
    /// assertion silently passes. Every M2a fixture is therefore checked to carry at least one
    /// recognisable secret literal before the assertion on it is trusted.
    /// </summary>
    [Fact]
    [Requirement("TST-050")]
    [Requirement("TST-051")]
    [Trait("Requirement", "TST-051")]
    public void Every_M2a_fixture_carries_a_recognisable_fake_secret_so_the_TST_051_search_is_not_vacuous()
    {
        FixtureRepository repository = new();

        foreach (string id in Green)
        {
            IReadOnlyList<string> secrets = FixtureSecrets.Harvest(repository.LoadById(id));
            Assert.NotEmpty(secrets);
            Assert.All(secrets, secret => Assert.True(
                secret.Contains("s.FAKE", StringComparison.Ordinal) || secret.Contains("password-fixture", StringComparison.Ordinal),
                $"fixture '{id}' carries secret '{secret}', which does not follow TST-050's `s.FAKE…`/`password-fixture` convention."));
        }
    }
}
