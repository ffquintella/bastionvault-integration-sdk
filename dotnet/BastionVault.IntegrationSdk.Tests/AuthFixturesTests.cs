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
    /// Every section-05 fixture green after M2b: M2a's five token-store fixtures plus
    /// <c>errors.format.one-line</c>, and M2b's ten — the four Userpass and five AppID login
    /// fixtures, and <c>auth.token.lookup-self-no-token-client-side</c>, whose CFG-020/ERR-022
    /// preflight this slice lands.
    /// </summary>
    private static readonly string[] Green =
    [
        "auth.token.lookup-self-remaining-ttl",
        "auth.token.lookup-unknown-404",
        "auth.token.renew-self-uses-renew-path",
        "auth.token.create-reserved-meta-client-side",
        "auth.token.revoke-self-clears-token",
        "errors.format.one-line",
        "auth.token.lookup-self-no-token-client-side",
        "auth.userpass.login-ok",
        "auth.userpass.totp-required",
        "auth.userpass.locked",
        "auth.userpass.login-200-rejected",
        "auth.appid.login-ok-with-machine-token-and-namespace",
        "auth.appid.invalid-secret-id-400",
        "auth.appid.machine-token-required",
        "auth.appid.gated-403",
        "auth.appid.env-scope-derived",
        // M2c: AUT-090…AUT-094's renewal loop, on the virtual clock D-M2-27 ruled.
        "auth.autorenew.schedule-and-renew",
        "auth.autorenew.stops-on-403",
        // M6: the section-05 remainder. `auth.cert.disabled-server` leaves the pending list it has
        // been on since M2a, and the two FerroGate fixtures Appendix C line 113 requires are
        // authored by this slice.
        "auth.cert.disabled-server",
        "auth.ferrogate.requirement-unauthenticated",
        "auth.ferrogate.enrolment-pending",
    ];

    /// <summary>
    /// Every <c>auth.*</c> fixture that stays <c>pending</c> after M6, with the milestone that owns
    /// it. D-M2-10's rule holds throughout: a pending fixture's owner is the milestone that lands
    /// its <b>operation</b>, not the one that lands its requirements.
    /// </summary>
    /// <remarks>
    /// <b>Empty.</b> M6 lands <c>Auth.Cert.Login</c>, which was the one remaining entry, so every
    /// section-05 fixture on disk now runs against real SDK code. The dictionary stays rather than
    /// being deleted because the exhaustiveness assertion below reads it, and because a later
    /// milestone that adds a section-05 fixture ahead of its operation needs somewhere to say so
    /// with a reason instead of quietly omitting it.
    /// </remarks>
    private static readonly Dictionary<string, string> Pending = new(StringComparer.Ordinal);

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
        // No held-pending list any more. M2a needed one for the single fixture whose operation
        // existed before its asserted behaviour did; M2b landed that behaviour, and the one
        // remaining pending fixture is pending because its operation is not registered, which the
        // driver already reports on its own.
        return new FixtureDriver(registry);
    }

    [Theory]
    [Requirement("AUT-020")]
    [Requirement("AUT-035")]
    [Requirement("AUT-050")]
    [Requirement("AUT-051")]
    [Requirement("AUT-070")]
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
    public void The_twenty_one_section_05_fixtures_are_green_and_the_pending_list_is_exhaustive_and_reasoned()
    {
        FixtureRepository repository = new();
        FixtureDriver driver = Driver();

        Assert.All(Green, id => Assert.Equal(FixtureRunStatus.Passed, driver.Run(repository.LoadById(id)).Status));
        Assert.Equal(21, Green.Length);

        // Every auth.* fixture is accounted for: green, or pending with a stated reason.
        IReadOnlyList<string> ids = LoadIds();
        Assert.All(ids, id => Assert.True(
            Pending.ContainsKey(id) || Green.Contains(id, StringComparer.Ordinal),
            $"auth fixture '{id}' is neither green nor on the reasoned pending list."));
        Assert.All(Pending.Values, reason => Assert.NotEmpty(reason));

        // Nothing is pending any more: every `auth.*` fixture on disk is driven by real SDK code.
        // The claim this stands for is precise and is *not* "section 05 is finished": section 05
        // has no unimplemented MUST left **except AUT-060's second one** — the loopback-redirect
        // recipe in the usage guides (`specifications/17-usage-guides.md`), which ROADMAP assigns
        // to M11 and which no fixture or test marker can see, because it is a prose MUST about a
        // document rather than about behaviour (DR-0011 revision 2, R1).
        Assert.Empty(Pending);
        Assert.Equal(0, driver.PendingCount);
    }

    /// <summary>
    /// The green fixtures whose TST-051 leak assertion is <b>vacuous</b>, because the fixture
    /// carries no harvestable credential at all — each with the reason it carries none.
    /// </summary>
    /// <remarks>
    /// Recorded rather than left implicit. D-M2-7's point is that TST-051 is a substring search, so
    /// a fixture whose secrets the harvester cannot see is one the assertion silently passes; the
    /// exhaustiveness check below therefore fails if a <i>new</i> fixture joins this set without a
    /// stated reason, which is the failure mode that would otherwise hide.
    /// </remarks>
    private static readonly Dictionary<string, string> WithoutHarvestableSecrets = new(StringComparer.Ordinal)
    {
        ["auth.token.lookup-self-no-token-client-side"] =
            "CFG-020/ERR-022: the fixture's subject is that the client holds no token, so there is no credential to leak.",
        ["auth.appid.invalid-secret-id-400"] =
            "AUT-012: the credentials are one-character placeholders under the API's own argument names (`roleId`, `secretId`), and the fixture declares no `expectRequest.body`, so nothing wire-shaped is harvestable.",
        ["auth.appid.machine-token-required"] =
            "AUT-012: same shape as `auth.appid.invalid-secret-id-400` — placeholder credentials and no declared request body.",
        ["auth.cert.disabled-server"] =
            "AUT-070: certificate authentication presents its credential at the TLS layer (CFG-044), so the login body is `{}` and the fixture carries no credential at all.",
        ["auth.ferrogate.requirement-unauthenticated"] =
            "AUT-051/CFG-020: the fixture's subject is that the call needs no token, and it supplies none — there is nothing to leak.",
    };

    /// <summary>
    /// The green fixtures that carry a harvestable credential which does <b>not</b> use TST-050's
    /// <c>s.FAKE…</c> / <c>password-fixture</c> spelling — a specification-fixture gap, recorded
    /// here because these files live under <c>specifications/</c> and the Engineering tree does not
    /// edit them (ENG-008). Reported as an open question at handback.
    /// </summary>
    private static readonly Dictionary<string, string> OutsideTst050Convention = new(StringComparer.Ordinal)
    {
        ["auth.appid.gated-403"] =
            "Its only harvestable credential is the UUID-shaped `secret_id` `22222222-…`. Distinctive enough for TST-051's search, but not in TST-050's spelling.",
    };

    /// <summary>
    /// D-M2-7: the TST-051 assertion "is exactly as strong as TST-050 compliance". It is a
    /// substring search, so every green fixture must either carry a credential the harvester can
    /// see — at least one of them in TST-050's distinctive spelling, and none so short that the
    /// search would match unrelated text — or be on the reasoned
    /// <see cref="WithoutHarvestableSecrets"/> list.
    /// </summary>
    [Fact]
    [Requirement("TST-050")]
    [Requirement("TST-051")]
    [Trait("Requirement", "TST-051")]
    public void Every_green_fixture_either_carries_a_recognisable_fake_secret_or_is_a_reasoned_exception()
    {
        FixtureRepository repository = new();

        foreach (string id in Green)
        {
            IReadOnlyList<string> secrets = FixtureSecrets.Harvest(repository.LoadById(id));
            if (WithoutHarvestableSecrets.ContainsKey(id))
            {
                Assert.Empty(secrets);
                continue;
            }

            Assert.NotEmpty(secrets);
            // Long enough that a substring search over surfaced strings means something: TST-050's
            // `s.FAKE…`/`password-fixture` spellings and the fixtures' UUID-shaped `secret_id`s all
            // clear this comfortably, and a one-character placeholder does not.
            Assert.All(secrets, secret => Assert.True(
                secret.Length >= 8,
                $"fixture '{id}' carries secret '{secret}', too short for TST-051's substring search to be meaningful."));

            bool conventional = secrets.Any(secret =>
                secret.Contains("s.FAKE", StringComparison.Ordinal)
                || secret.Contains("password-fixture", StringComparison.Ordinal));
            Assert.Equal(!OutsideTst050Convention.ContainsKey(id), conventional);
        }

        // Exhaustive in both directions: no fixture is on either list without being in Green, and
        // every entry states a reason.
        Assert.All(WithoutHarvestableSecrets.Keys, id => Assert.Contains(id, Green));
        Assert.All(WithoutHarvestableSecrets.Values, reason => Assert.NotEmpty(reason));
        Assert.All(OutsideTst050Convention.Keys, id => Assert.Contains(id, Green));
        Assert.All(OutsideTst050Convention.Values, reason => Assert.NotEmpty(reason));
    }
}
