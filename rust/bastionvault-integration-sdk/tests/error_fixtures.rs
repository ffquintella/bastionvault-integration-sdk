//! M1c: every `errors.*` fixture (Appendix C) runs through real SDK code — the 124
//! generated `errors.recognition.*` fixtures (one per Appendix B §2 rule, D-M1c-10), the
//! four hand-authored recognition fixtures that predate the generator, the five
//! `errors.enrichment.*` fixtures and `errors.format.one-line`.
//!
//! Driven through `OperationRegistry::m1b()`'s real `Logical.*` handlers and the SDK's
//! own public `FakeTransport` (D-M1b-15), so a fixture that passes here passes against
//! the code a caller runs.

mod harness;

use harness::driver::{compare_error, FixtureDriver, OperationRegistry, RunOutcome};
use harness::fixture::FixtureLoader;

/// The three fixtures that stay `pending` after M1c, each with a named owning milestone.
/// They are listed, never deleted and never edited to fit (D-M1c-10/D-M1c-14 item 10,
/// CLA-004): the driver reports them pending because the typed operation they drive is
/// not registered yet.
const PENDING: [&str; 3] = [
    // Drives `Auth.Token.Lookup` — M2 (D-M1c-10).
    "errors.format.one-line",
    // ERR-022 is a typed-layer guard; the fixture drives `Kv.V2.ReadSecret` — M2.
    "errors.recognition.missing-token-client-side",
    // Needs the `Sys.ListMounts` cache to know the mount is KV v2 — M4 (D-M1c-5).
    "errors.enrichment.404-kv2-hint",
];

#[test]
fn every_error_fixture_passes_against_real_sdk_code_err_020_err_035_err_040_cnf_043() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let fixtures = loader
        .load_all()
        .expect("all repository fixtures must validate")
        .into_iter()
        .filter(|fixture| fixture.id.starts_with("errors."))
        .collect::<Vec<_>>();

    // 124 generated recognition + 4 hand-authored recognition + 5 enrichment + 1 format.
    assert_eq!(fixtures.len(), 134, "expected exactly 134 errors.* fixtures");

    let driver = FixtureDriver::with_registry(OperationRegistry::m1b());
    let mut failures = Vec::new();
    let mut passed = 0_usize;
    let mut pending = Vec::new();

    for fixture in &fixtures {
        let outcome = match driver.run(fixture) {
            Ok(outcome) => outcome,
            Err(error) => {
                failures.push(format!("{}: driver error: {error}", fixture.id));
                continue;
            }
        };
        match outcome {
            RunOutcome::Pending { operation } => {
                if PENDING.contains(&fixture.id.as_str()) {
                    pending.push(fixture.id.clone());
                } else {
                    failures.push(format!(
                        "{}: operation {operation} is not registered and the fixture is not a \
                         recorded D-M1c-14 item 10 exception",
                        fixture.id
                    ));
                }
                continue;
            }
            RunOutcome::Ran { result } => {
                let Some(expected_error) = fixture.expect.error.as_ref() else {
                    failures.push(format!("{}: an errors.* fixture must expect an error", fixture.id));
                    continue;
                };
                match result {
                    Err(actual) => match compare_error(expected_error, &actual) {
                        Ok(()) => passed += 1,
                        Err(mismatch) => {
                            failures.push(format!("{}: error mismatch: {mismatch}", fixture.id));
                        }
                    },
                    Ok(actual) => failures.push(format!(
                        "{}: expected an error but got result {actual:?}",
                        fixture.id
                    )),
                }
            }
        }
    }

    assert!(
        failures.is_empty(),
        "{} error fixture failures:\n{}",
        failures.len(),
        failures.join("\n")
    );

    pending.sort();
    let mut expected_pending = PENDING.map(str::to_owned).to_vec();
    expected_pending.sort();
    assert_eq!(pending, expected_pending, "the pending set must not grow or shrink silently");
    assert_eq!(passed, fixtures.len() - PENDING.len());
}

#[test]
fn the_generated_recognition_set_covers_every_appendix_b_row_fix_001() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let ids = loader
        .enumerate()
        .expect("enumeration must work")
        .into_iter()
        .map(|fixture| fixture.id)
        .filter(|id| id.starts_with("errors."))
        .collect::<Vec<_>>();

    assert_eq!(ids.len(), 134);
    assert_eq!(
        ids.iter().filter(|id| id.starts_with("errors.recognition.bv-")).count(),
        124
    );
    assert_eq!(ids.iter().filter(|id| id.starts_with("errors.enrichment.")).count(), 5);
    for pending in PENDING {
        assert!(ids.iter().any(|id| id == pending), "{pending} must still be on disk");
    }
}
