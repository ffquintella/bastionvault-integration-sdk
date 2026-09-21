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
        .warn(concat!("resolved token s.", "FAKEtoken0000000000000000 for this request"));
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
    assert!(!error.contains(harness::fake_tokens::CLIENT), "{error}");
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
            path: concat!("auth/token/lookup/s.", "FAKEtoken0000000000000000").to_owned(),
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
    // 253 = 247 at 0.13.0 + M9 slice a's three pki.* and slice d's three ssh.* fixtures.
    // R-25's fourth transcription site: this assertion was added on main while M9 was in
    // flight, so M9 updated the three it knew about and this one went red at the merge —
    // the tripwire's own failure mode, demonstrated across branches rather than in one tree.
    assert_eq!(fixtures.len(), 253, "expected exactly 253 fixtures on disk");

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

/// An operation that asks for a wait the fixture did not declare. `Clock::delay` records the
/// grant eagerly, before the returned future is polled, so dropping it still books the wait —
/// which is what lets a synchronous test drive the matcher.
fn over_waiting_operation(
    _config: &DriverConfig,
    instruments: &Instruments,
    _transport: &mut FakeTransport,
    _operation: &harness::fixture::Operation,
) -> Result<ActualValue, ActualError> {
    use bastionvault_integration_sdk::Clock;
    drop(instruments.clock.delay(std::time::Duration::from_millis(500)));
    Ok(ActualValue::null())
}

/// **D-M2-27 item 3.** `clock.expectWaits` is a positive claim, so a run that grants a
/// different schedule must go red. Without this the four-disjunct honour predicate would be
/// the loophole its own comment says it is not: `expectWaits` being *present* satisfies
/// honour, and only this matcher makes the presence mean anything.
#[test]
fn granted_waits_that_do_not_match_expect_waits_fail_the_run_d_m2_27() {
    let fixture = fixture("resilience.backoff.math-seeded");
    assert!(
        fixture.clock.as_ref().and_then(|clock| clock.expect_waits.as_ref()).is_some(),
        "this fixture is the one that declares expectWaits"
    );

    let mut registry = OperationRegistry::empty();
    registry.register("Logical.Read", over_waiting_operation);
    let error = FixtureDriver::with_registry(registry)
        .run(&fixture)
        .expect_err("a wait schedule that does not match expectWaits must fail the run");
    assert!(error.contains("do not match it"), "{error}");
    assert!(error.contains("D-M2-27"), "{error}");
}

/// **RES-003, end to end.** The real operation, the real retry policy and the fixture's
/// seeded jitter produce exactly the declared schedule: `min(0.15, 0.1) x 0.8 = 80 ms`, then
/// `min(0.15, 0.2) x 1.2 = 180 ms` — the second wait clipped by `MaxBackoff` *before* jitter.
///
/// This is the assertion the harness could not make until `MaxBackoff`,
/// `BackoffMultiplier`, `Jitter` and `settings.__jitter` were wired: the policy stayed at its
/// defaults, so the waits came out at 100 ms and 200 ms and nothing noticed.
#[test]
fn the_seeded_backoff_schedule_matches_res_003_exactly() {
    let fixture = fixture("resilience.backoff.math-seeded");
    FixtureDriver::with_registry(OperationRegistry::m2a())
        .run(&fixture)
        .expect("the real operation must grant exactly the declared schedule");
}

/// `settings.__jitter` is a sequence, not a PRNG seed: consumed in order, then the midpoint.
/// A seed would produce different sequences in .NET, Rust and Python and make the fixture
/// silently non-parity (D-M5-14).
#[test]
fn the_seeded_jitter_source_is_consumed_in_order_then_falls_back_to_the_midpoint_d_m5_14() {
    use bastionvault_integration_sdk::JitterSource;
    let jitter = harness::instruments::SequenceJitter::new(vec![0.0, 1.0]);
    assert_eq!(jitter.next_f64(), 0.0);
    assert_eq!(jitter.next_f64(), 1.0);
    assert_eq!(jitter.next_f64(), 0.5, "exhausted means the midpoint, not a repeat or a panic");
    assert_eq!(jitter.next_f64(), 0.5);
}

/// A fixture that seeds no jitter still gets a deterministic source. The SDK's own default is
/// seeded from the system clock, so a fixture left on it cannot assert a wait at all.
#[test]
fn an_unseeded_fixture_gets_the_deterministic_midpoint_jitter_source() {
    use bastionvault_integration_sdk::JitterSource;
    let jitter = harness::instruments::FixtureJitter;
    assert_eq!(jitter.next_f64(), 0.5);
    assert_eq!(jitter.next_f64(), 0.5, "and it does not drift between calls");
}
