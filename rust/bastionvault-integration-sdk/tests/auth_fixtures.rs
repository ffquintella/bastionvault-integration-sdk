//! M2a: every `auth.*` fixture runs through real SDK code — the `Auth` grouping, the nine
//! token-store operations and the shared executor they all issue their requests through
//! (D-M2-4).
//!
//! Driven through `OperationRegistry::m2a()`'s real handlers, the SDK's own public
//! `FakeTransport` (D-M1b-15) and D-M2-7's two instruments, so a fixture that passes here
//! passes against the code a caller runs.
//!
//! **The traceability ratchet cannot tell when this is done.** All 14 of M2a's IDs came off
//! the baseline when .NET's markers satisfied them, and the tool counts an ID covered if
//! *any one* language covers it (R-4, R-9). The gate is this file: the shared fixtures
//! passing against real Rust code.

mod harness;

use harness::driver::{compare_error, compare_result, ActualValue, FixtureDriver, OperationRegistry, RunOutcome};
use harness::fixture::{Fixture, FixtureLoader};

/// Fixtures whose **operation** M2a does not land: they belong to M2b (`Auth.Userpass.*`,
/// `Auth.AppId.*`) and M6 (`Auth.Cert.*`, D-M2-5). Listed, never deleted and never edited
/// to fit (`CLA-004`).
const PENDING_OPERATION: [&str; 10] = [
    // M2b — the AppID login method (AUT-040…AUT-042).
    "auth.appid.env-scope-derived",
    "auth.appid.gated-403",
    "auth.appid.invalid-secret-id-400",
    "auth.appid.login-ok-with-machine-token-and-namespace",
    "auth.appid.machine-token-required",
    // M2b — the Userpass login method (AUT-030…AUT-032).
    "auth.userpass.locked",
    "auth.userpass.login-200-rejected",
    "auth.userpass.login-ok",
    "auth.userpass.totp-required",
    // M6 — Certificate authentication (D-M2-5).
    "auth.cert.disabled-server",
];

/// The one fixture whose **operation exists at M2a but whose asserted behaviour does not**
/// (D-M2-10, review finding R3).
///
/// `Auth.Token.LookupSelf` lands here, so by D-M2-10's rule — a pending fixture's owner is
/// the milestone that lands its *operation* — it stops being operation-pending at M2a. But
/// the `CFG-020`/`ERR-022` preflight it asserts lands at **M2b**. Left unlisted it would go
/// red at this handback, read as a regression, and the cheapest-looking fix would be to
/// weaken it (`CLA-004`).
///
/// The entry cannot rot: [`behavioural_pending_fixtures_still_do_not_pass_d_m2_10`] fails
/// if the fixture starts passing, so **M2b's exit must remove this list**.
const PENDING_BEHAVIOUR: [(&str, &str); 1] = [(
    "auth.token.lookup-self-no-token-client-side",
    "operation exists; asserted behaviour is M2b",
)];

fn auth_fixtures(loader: &FixtureLoader) -> Vec<Fixture> {
    loader
        .load_all()
        .expect("all repository fixtures must validate")
        .into_iter()
        .filter(|fixture| fixture.id.starts_with("auth."))
        .collect()
}

fn is_behaviour_pending(id: &str) -> bool {
    PENDING_BEHAVIOUR.iter().any(|(pending, _)| *pending == id)
}

/// Compares one fixture's outcome against its `expect` block, returning every mismatch.
fn compare(fixture: &Fixture, result: Result<ActualValue, harness::driver::ActualError>) -> Vec<String> {
    let mut failures = Vec::new();
    match (&fixture.expect.error, &fixture.expect.result, &fixture.expect.client_state, result) {
        (Some(expected), _, _, Err(actual)) => {
            if let Err(mismatch) = compare_error(expected, &actual) {
                failures.push(format!("{}: error mismatch: {mismatch}", fixture.id));
            }
        }
        (Some(_), _, _, Ok(actual)) => {
            failures.push(format!("{}: expected an error but got result {actual:?}", fixture.id));
        }
        (None, expected_result, expected_state, Ok(actual)) => {
            for expected in expected_result.iter().chain(expected_state.iter()) {
                if let Err(mismatch) = compare_result(expected, &actual) {
                    failures.push(format!("{}: result mismatch: {mismatch}", fixture.id));
                }
            }
        }
        (None, _, _, Err(actual)) => {
            failures.push(format!("{}: expected a result but got error {actual:?}", fixture.id));
        }
    }
    failures
}

#[test]
fn every_m2a_auth_fixture_passes_against_real_sdk_code_aut_014_aut_020_aut_080_aut_085() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let fixtures = auth_fixtures(&loader);
    assert_eq!(fixtures.len(), 16, "expected exactly 16 auth.* fixtures on disk");

    let driver = FixtureDriver::with_registry(OperationRegistry::m2a());
    let mut failures = Vec::new();
    let mut passed = Vec::new();
    let mut pending = Vec::new();

    for fixture in &fixtures {
        if is_behaviour_pending(&fixture.id) {
            continue;
        }
        let outcome = match driver.run(fixture) {
            Ok(outcome) => outcome,
            Err(error) => {
                // Driver errors include D-M2-7's two whole-run assertions: an ignored
                // `clock` block and a TST-051 secret leak.
                failures.push(format!("{}: driver error: {error}", fixture.id));
                continue;
            }
        };
        match outcome {
            RunOutcome::Pending { operation } => {
                if PENDING_OPERATION.contains(&fixture.id.as_str()) {
                    pending.push(fixture.id.clone());
                } else {
                    failures.push(format!(
                        "{}: operation {operation} is not registered and the fixture is not a \
                         recorded pending exception",
                        fixture.id
                    ));
                }
            }
            RunOutcome::Ran { result } => {
                let mismatches = compare(fixture, result);
                if mismatches.is_empty() {
                    passed.push(fixture.id.clone());
                } else {
                    failures.extend(mismatches);
                }
            }
        }
    }

    assert!(
        failures.is_empty(),
        "{} auth fixture failures:\n{}",
        failures.len(),
        failures.join("\n")
    );

    pending.sort();
    let mut expected_pending = PENDING_OPERATION.map(str::to_owned).to_vec();
    expected_pending.sort();
    assert_eq!(pending, expected_pending, "the pending set must not grow or shrink silently");

    passed.sort();
    assert_eq!(
        passed,
        vec![
            "auth.token.create-reserved-meta-client-side".to_owned(),
            "auth.token.lookup-self-remaining-ttl".to_owned(),
            "auth.token.lookup-unknown-404".to_owned(),
            "auth.token.renew-self-uses-renew-path".to_owned(),
            "auth.token.revoke-self-clears-token".to_owned(),
        ],
        "M2a's five auth.* fixtures, plus errors.format.one-line in tests/error_fixtures.rs"
    );
}

/// D-M2-10's entry cannot rot: when M2b lands the `CFG-020`/`ERR-022` preflight this test
/// goes red, which is what forces the list to be removed rather than carried forward.
#[test]
fn behavioural_pending_fixtures_still_do_not_pass_d_m2_10() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let driver = FixtureDriver::with_registry(OperationRegistry::m2a());
    for (id, reason) in PENDING_BEHAVIOUR {
        let fixture = loader
            .load_by_id(id)
            .expect("enumeration must work")
            .unwrap_or_else(|| panic!("{id} must still be on disk"));
        let outcome = driver.run(&fixture).expect("the driver must run it");
        let RunOutcome::Ran { result } = outcome else {
            panic!("{id}: its operation lands at M2a, so it must run rather than report pending");
        };
        assert!(
            !compare(&fixture, result).is_empty(),
            "{id} now passes — its reason was {reason:?}, so remove it from PENDING_BEHAVIOUR \
             (D-M2-10: M2b's exit removes the entry and the fixture goes green)"
        );
    }
}

/// `TST-050` is what makes `TST-051`'s substring search a valid test rather than a gesture,
/// so every credential literal an `auth.*` fixture carries is checked to be *distinctive*
/// before the assertion is trusted (D-M2-7).
///
/// Two separate claims, because they fail for different reasons:
///
/// 1. Every harvested literal is obviously fake. A real-looking credential would make a
///    substring search both a weaker test and a secret in the repository.
/// 2. Every fixture M2a runs green harvests **at least one** literal — otherwise `TST-051`
///    asserts nothing at all for it and the instrument is decorative on that fixture. The
///    four fixtures that carry no credential (`client.token: null`, no password) are
///    exempt, because there is nothing there to leak.
#[test]
fn every_auth_fixture_credential_is_obviously_fake_tst_050() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    for fixture in auth_fixtures(&loader) {
        for secret in harness::instruments::secrets::harvest(&fixture.document) {
            assert!(
                is_obviously_fake(&secret),
                "{}: credential {secret:?} is not obviously fake (TST-050)",
                fixture.id
            );
        }
    }
}

#[test]
fn every_green_auth_fixture_carries_a_credential_for_tst_051_to_search_d_m2_7() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    for id in [
        "auth.token.create-reserved-meta-client-side",
        "auth.token.lookup-self-remaining-ttl",
        "auth.token.lookup-unknown-404",
        "auth.token.renew-self-uses-renew-path",
        "auth.token.revoke-self-clears-token",
    ] {
        let fixture = loader
            .load_by_id(id)
            .expect("enumeration must work")
            .unwrap_or_else(|| panic!("{id} must be on disk"));
        assert!(
            !harness::instruments::secrets::harvest(&fixture.document).is_empty(),
            "{id} carries no harvestable credential, so TST-051 would assert nothing for it"
        );
    }
}

/// `TST-050`'s two named conventions (`s.FAKE…`, `password-fixture`), plus the third the
/// corpus actually uses: a UUID built from one repeated digit
/// (`22222222-2222-2222-2222-222222222222`), which no real generator produces.
fn is_obviously_fake(secret: &str) -> bool {
    if secret.contains("FAKE") || secret.contains("fixture") {
        return true;
    }
    let distinct = secret
        .chars()
        .filter(char::is_ascii_alphanumeric)
        .collect::<std::collections::BTreeSet<_>>();
    distinct.len() <= 2
}
