// `ActualError` is the harness's test-only, language-neutral comparison shape (M0), and the
// operation-handler signature returns it by value. Boxing it to satisfy a lint would change
// a signature the whole registry shares, for a type that is never on a hot path. Allowed
// here for the same reason and in the same words as `tests/harness/mod.rs`.
#![allow(clippy::result_large_err)]

//! D-M2-7's proof obligation, kept as standing tests.
//!
//! Both instruments were proven at M2a by seeding a violation and watching the suite go
//! red, then reverting (R-10, DR-0001). A seeded violation proves the instrument *once*;
//! these tests keep it proven, because "an instrument that has never failed has not been
//! tested" and this project has now shipped several gates that were trusted on their
//! record.
//!
//! Each test drives the **driver's own enforcement path** — not the instrument in
//! isolation — so it fails if the wiring in `FixtureDriver::run` is removed, which is what
//! a seeded violation actually tests.

mod harness;

use std::sync::Arc;

use harness::driver::{ActualError, ActualValue, DriverConfig, FixtureDriver, Instruments, OperationRegistry};
use harness::fixture::{Fixture, FixtureLoader};
use harness::transport::FakeTransport;

/// An operation that never asks what time it is. Registered against a fixture that declares
/// a `clock`, it is the exact shape of the defect D-M2-7 was written for: the fixture
/// declared a clock, the driver ignored it, and the fixture passed anyway — for four
/// milestones.
fn clock_ignoring_operation(
    _config: &DriverConfig,
    _instruments: &Instruments,
    _transport: &mut FakeTransport,
    _operation: &harness::fixture::Operation,
) -> Result<ActualValue, ActualError> {
    Ok(ActualValue::null())
}

/// An operation that logs a fixture token, which is the `TST-051` violation the capturing
/// logger exists to catch.
fn leaking_operation(
    _config: &DriverConfig,
    instruments: &Instruments,
    _transport: &mut FakeTransport,
    _operation: &harness::fixture::Operation,
) -> Result<ActualValue, ActualError> {
    use bastionvault_integration_sdk::ClientLogger;
    instruments
        .logger
        .warn("resolved token s.FAKEtoken0000000000000000 for this request");
    Ok(ActualValue::null())
}

fn fixture(id: &str) -> Fixture {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    loader
        .load_by_id(id)
        .expect("enumeration must work")
        .unwrap_or_else(|| panic!("{id} must be on disk"))
}

/// **Instrument one.** A fixture that declares a `clock` a driver ignores must **fail**,
/// not pass.
#[test]
fn a_declared_clock_that_the_operation_never_reads_fails_the_run_d_m2_7() {
    let fixture = fixture("auth.token.lookup-self-remaining-ttl");
    assert!(fixture.clock.is_some(), "this fixture is the one that declares a clock");

    let mut registry = OperationRegistry::empty();
    registry.register("Auth.Token.LookupSelf", clock_ignoring_operation);
    let error = FixtureDriver::with_registry(registry)
        .run(&fixture)
        .expect_err("an ignored clock must fail the run");
    assert!(error.contains("never read"), "{error}");
    assert!(error.contains("D-M2-7"), "{error}");

    // And the real operation does read it, which is what makes the fixture's `RemainingTtl`
    // assertion load-bearing rather than vacuous.
    let outcome = FixtureDriver::with_registry(OperationRegistry::m2a())
        .run(&fixture)
        .expect("the real operation reads the clock");
    assert!(matches!(outcome, harness::driver::RunOutcome::Ran { .. }));
}

/// A fixture that declares **no** clock is unaffected: the instrument defaults to a clock
/// frozen at the unix epoch, so no pre-M2a fixture changes meaning.
#[test]
fn a_fixture_with_no_clock_block_is_not_required_to_read_one_d_m2_7() {
    let fixture = fixture("auth.token.revoke-self-clears-token");
    assert!(fixture.clock.is_none());
    let mut registry = OperationRegistry::empty();
    registry.register("Auth.Token.RevokeSelf", clock_ignoring_operation);
    FixtureDriver::with_registry(registry)
        .run(&fixture)
        .expect("no clock block, no obligation");
}

/// **Instrument two.** A fixture token in a captured log line fails the run (`TST-051`).
#[test]
fn a_fixture_token_in_a_captured_log_line_fails_the_run_tst_051() {
    let fixture = fixture("auth.token.revoke-self-clears-token");
    let mut registry = OperationRegistry::empty();
    registry.register("Auth.Token.RevokeSelf", leaking_operation);
    let error = FixtureDriver::with_registry(registry)
        .run(&fixture)
        .expect_err("a logged token must fail the run");
    assert!(error.contains("TST-051"), "{error}");
    assert!(error.contains("leaked secret material"), "{error}");
    // The report itself is masked: a test failure must not be the leak.
    assert!(!error.contains("s.FAKEtoken0000000000000000"), "{error}");
}

/// **Instrument two, the half D-M2-7 named explicitly.** A fixture token in an *observer
/// event* fails the run — this, not the log, is where the leak .NET actually shipped.
#[test]
fn a_fixture_token_in_a_captured_observer_event_fails_the_run_tst_051_cfg_080() {
    use bastionvault_integration_sdk::{RequestEvent, RequestObserver};
    fn leaking_observer_operation(
        _config: &DriverConfig,
        instruments: &Instruments,
        _transport: &mut FakeTransport,
        _operation: &harness::fixture::Operation,
    ) -> Result<ActualValue, ActualError> {
        // The unredacted path AUT-080 and Auth.Token.Lookup put a live token into.
        instruments.observer.on_request_completed(&RequestEvent {
            method: "GET".to_owned(),
            path: "auth/token/lookup/s.FAKEtoken0000000000000000".to_owned(),
            namespace: String::new(),
            status_code: Some(200),
            duration: std::time::Duration::from_millis(1),
            request_id: "req-1".to_owned(),
            attempt: 1,
            error_code: None,
        });
        Ok(ActualValue::null())
    }

    let fixture = fixture("auth.token.revoke-self-clears-token");
    let mut registry = OperationRegistry::empty();
    registry.register("Auth.Token.RevokeSelf", leaking_observer_operation);
    let error = FixtureDriver::with_registry(registry)
        .run(&fixture)
        .expect_err("an observer event carrying a token must fail the run");
    assert!(error.contains("TST-051"), "{error}");
}

/// The instruments are attached to **every** fixture run, in every language, rather than
/// opted into per fixture — `TST-051` is a whole-run assertion. Asserted by running the
/// whole corpus through the M2a registry and requiring no driver error.
#[test]
fn every_repository_fixture_runs_under_both_instruments_tst_051_d_m2_7() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let driver = FixtureDriver::with_registry(OperationRegistry::m2a());
    let fixtures = loader.load_all().expect("all repository fixtures must validate");
    assert_eq!(fixtures.len(), 208, "expected exactly 208 fixtures on disk");

    let mut failures = Vec::new();
    for fixture in &fixtures {
        if let Err(error) = driver.run(fixture) {
            failures.push(format!("{}: {error}", fixture.id));
        }
    }
    assert!(
        failures.is_empty(),
        "{} fixtures failed an instrument assertion:\n{}",
        failures.len(),
        failures.join("\n")
    );
}

/// The `Arc` the driver hands the SDK is the same object the assertion reads afterwards —
/// if it were a copy, every capture would be silently empty and both instruments would pass
/// vacuously. Asserted directly, because "the instrument was wired but captured nothing" is
/// the failure mode a green suite cannot distinguish from "nothing leaked".
#[test]
fn the_capturing_instruments_actually_capture_d_m2_7() {
    let fixture = fixture("auth.token.lookup-self-remaining-ttl");
    let instruments = Instruments::for_fixture(&fixture).expect("the fixture's clock parses");
    let observer =
        Arc::clone(&instruments.observer) as Arc<dyn bastionvault_integration_sdk::RequestObserver>;
    let logger = Arc::clone(&instruments.logger) as Arc<dyn bastionvault_integration_sdk::ClientLogger>;
    logger.warn("a line");
    observer.on_request_completed(&bastionvault_integration_sdk::RequestEvent {
        method: "GET".to_owned(),
        path: "secret/data/x".to_owned(),
        namespace: String::new(),
        status_code: Some(200),
        duration: std::time::Duration::from_millis(1),
        request_id: "req-1".to_owned(),
        attempt: 1,
        error_code: None,
    });
    assert_eq!(instruments.logger.lines(), vec!["a line".to_owned()]);
    assert_eq!(instruments.observer.events().len(), 1);
    assert!(instruments.clock.is_scripted());
}
