// `ActualError` is a test-only, language-neutral comparison shape (M0); returning it by
// value from a synthetic test handler is not a hot path worth boxing.
#![allow(clippy::result_large_err)]

mod harness;

use harness::driver::{compare_error, compare_result};
use harness::fixture::{Fixture, FixtureLoader};
use harness::transport::{FakeTransport, TransportFailure};
use serde_json::json;
use std::fs;

#[test]
fn validates_all_repository_fixtures_fix_001_tst_010_tst_012() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let fixtures = loader
        .load_all()
        .expect("all repository fixtures must validate");
    assert_eq!(fixtures.len(), 203);
}

#[test]
fn enumerates_and_filters_repository_fixtures_tst_010_tst_012_tst_013() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    assert_eq!(loader.enumerate().expect("enumeration must work").len(), 203);
    assert!(
        loader
            .filter_by_level("core")
            .expect("level filtering must work")
            .iter()
            .all(|fixture| fixture.level == "core")
    );
    assert!(
        loader
            .filter_by_sections(&["03"])
            .expect("section filtering must work")
            .iter()
            .all(|fixture| fixture.sections.iter().any(|section| section == "03"))
    );
    let fixture = loader
        .load_by_id("transport.method.list-verb")
        .expect("known fixture must load")
        .expect("known fixture must be present");
    assert_eq!(fixture.id, "transport.method.list-verb");
}

#[test]
fn repository_operations_are_pending_without_failing_tst_011() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let fixtures = loader
        .load_all()
        .expect("all repository fixtures must validate");
    // D-M1a-12: the registry Rust ships here is real-handlers-not-yet-registered, not
    // handlers-that-fail. `FixtureDriver::new()` starts with an empty registry, so every
    // fixture must still resolve as pending regardless of how many operations M1a (or a
    // later milestone) has since registered elsewhere.
    let driver = harness::driver::FixtureDriver::new();
    let pending = fixtures
        .iter()
        .filter(|fixture| {
            let outcome = driver
                .run(fixture)
                .expect("pending fixtures must not fail the run");
            match outcome {
                harness::driver::RunOutcome::Pending { operation } => {
                    assert_eq!(operation, fixture.operation.name);
                    true
                }
                harness::driver::RunOutcome::Ran { .. } => false,
            }
        })
        .count();
    println!("pending fixtures: {pending}");
    assert_eq!(pending, fixtures.len());
}

#[test]
fn client_construct_fixtures_run_against_real_sdk_code_tst_011_cfg_017() {
    // D-M1a-12/D-M1a-6: `Client.Construct` now resolves to a real handler, and the one
    // fixture that names it (`transport.headers.reserved-rejected`) must pass end to
    // end against the real `ClientConfigBuilder`/`Client`, not a test shim.
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let fixture = loader
        .load_by_id("transport.headers.reserved-rejected")
        .expect("fixture must load")
        .expect("fixture must exist");
    let driver = harness::driver::FixtureDriver::with_registry(
        harness::driver::OperationRegistry::m1a(),
    );
    let outcome = driver.run(&fixture).expect("run must not fail");
    let harness::driver::RunOutcome::Ran { result } = outcome else {
        panic!("Client.Construct must now be registered, not pending");
    };
    let error = result.expect_err("a reserved header must fail construction");
    let expected_error = fixture
        .expect
        .error
        .as_ref()
        .expect("fixture must expect an error");
    harness::driver::compare_error(expected_error, &error)
        .expect("the real SDK error must match the fixture expectation");
}

#[test]
fn driver_configures_client_environment_and_reports_pending_tst_011_tst_012() {
    let mut fixture = Fixture::synthetic_with_exchanges(Vec::new());
    fixture.client = Some(harness::fixture::ClientConfig {
        address: Some("https://vault.example.test".to_owned()),
        token: None,
        namespace: Some("team-a".to_owned()),
        api_prefix: Some("v2".to_owned()),
        cluster_discovery: Some(true),
        settings: Some(json!({"retry": 1})),
    });
    fixture.environment = Some(
        [("BV_REGION".to_owned(), "test".to_owned())]
            .into_iter()
            .collect(),
    );
    let config = harness::driver::configure(&fixture);
    assert_eq!(
        config.address.as_deref(),
        Some("https://vault.example.test")
    );
    assert_eq!(config.namespace.as_deref(), Some("team-a"));
    assert_eq!(config.api_prefix.as_deref(), Some("v2"));
    assert_eq!(
        config.environment.get("BV_REGION").map(String::as_str),
        Some("test")
    );
    let outcome = harness::driver::FixtureDriver::new()
        .run(&fixture)
        .expect("unregistered operation must not fail");
    assert_eq!(
        outcome,
        harness::driver::RunOutcome::Pending {
            operation: "Synthetic.Operation".to_owned()
        }
    );
}

#[test]
fn operation_registry_is_empty_until_explicit_registration_tst_011() {
    fn synthetic_handler(
        _config: &harness::driver::DriverConfig,
        _transport: &mut harness::transport::FakeTransport,
        _operation: &harness::fixture::Operation,
    ) -> Result<harness::driver::ActualValue, harness::driver::ActualError> {
        Ok(harness::driver::ActualValue::null())
    }

    let mut registry = harness::driver::OperationRegistry::empty();
    assert!(registry.resolve("Synthetic.Operation").is_pending());
    registry.register("Synthetic.Operation", synthetic_handler);
    assert!(matches!(
        registry.resolve("Synthetic.Operation"),
        harness::driver::OperationResolution::Registered(_)
    ));
}

#[test]
fn coverage_configuration_has_line_region_and_publish_outputs_tst_030_tst_031() {
    // D-M0-14: branch coverage is unavailable on the installed stable toolchain
    // (cargo-llvm-cov's --branch requires a nightly compiler). The Coverage
    // measurement section of specifications/15-testing-requirements.md permits line +
    // region coverage, both at 95 %, as the documented substitute — a recorded
    // Rust-only parity exception under CLA-003. The floor stays at 95 %.
    let manifest = env!("CARGO_MANIFEST_DIR");
    let config =
        fs::read_to_string(format!("{manifest}/.cargo/config.toml")).expect("coverage config");
    assert!(!config.contains("--branch"));
    assert!(!config.contains("--fail-under-branches"));
    assert!(config.contains("--fail-under-lines"));
    assert!(config.contains("95"));
    assert!(config.contains("--fail-under-regions"));
    assert!(config.contains("coverage/html"));
    assert!(config.contains("coverage/cobertura.xml"));
}

#[test]
fn library_has_no_coverage_exclusion_pragma_tst_030() {
    let manifest = env!("CARGO_MANIFEST_DIR");
    let library = fs::read_to_string(format!("{manifest}/src/lib.rs")).expect("library source");
    for forbidden in [
        "ExcludeFromCodeCoverage",
        "pragma: no cover",
        "cfg(not(coverage))",
    ] {
        assert!(
            !library.contains(forbidden),
            "library contains forbidden coverage exclusion {forbidden}"
        );
    }
}

#[test]
fn request_comparison_positive_rules_fix_002_tst_011() {
    let expected = json!({
        "method": "LIST",
        "url": "https://vault.example.test/v1/items",
        "headers": {"x-request-id": "abc", "Accept": "application/json"},
        "absentHeaders": ["X-Forbidden"],
        "body": {"z": 2, "a": [true, {"b": 1, "a": 0}]}
    });
    let actual = harness::driver::RequestSnapshot::new(
        "LIST",
        "https://vault.example.test/v1/items",
        [
            ("X-REQUEST-ID", "abc"),
            ("accept", "application/json"),
            ("X-Extra", "unchecked"),
        ],
        json!({"a": [true, {"a": 0, "b": 1}], "z": 2}),
    );
    harness::driver::compare_request(&expected, &actual, false).expect("request should match");
}

#[test]
fn request_comparison_negative_rules_fix_002_tst_011() {
    let expected = json!({
        "method": "POST",
        "url": "https://vault.example.test/v1/items",
        "headers": {"X-Key": "expected"},
        "absentHeaders": ["X-Forbidden"],
        "body": {"a": 1}
    });
    let actual = harness::driver::RequestSnapshot::new(
        "GET",
        "https://vault.example.test/v1/other",
        [
            ("x-key", "wrong"),
            ("X-Forbidden", "present"),
            ("X-Unlisted", "present"),
        ],
        json!({"a": 2}),
    );
    let error = harness::driver::compare_request(&expected, &actual, true)
        .expect_err("method, URL, headers, absent header and body must be checked");
    assert!(error.contains("method"));
    assert!(error.contains("url"));
    assert!(error.contains("X-Key"));
    assert!(error.contains("X-Forbidden"));
    assert!(error.contains("body"));
    assert!(error.contains("x-unlisted"));
}

#[test]
fn result_comparison_positive_extra_ignored_and_sentinels_fix_003_tst_011() {
    let expected = json!({
        "name": "item",
        "optional": "$absent",
        "anything": "$any",
        "redacted": "$redacted",
        "nested": [{"keep": 1}]
    });
    let actual = harness::driver::ActualValue::object([
        ("name", harness::driver::ActualValue::string("item")),
        ("anything", harness::driver::ActualValue::number(9)),
        (
            "redacted",
            harness::driver::ActualValue::redacted("sensitive-value"),
        ),
        (
            "nested",
            harness::driver::ActualValue::array([harness::driver::ActualValue::object([
                ("keep", harness::driver::ActualValue::number(1)),
                ("extra", harness::driver::ActualValue::boolean(true)),
            ])]),
        ),
        ("extra", harness::driver::ActualValue::null()),
    ]);
    compare_result(&expected, &actual).expect("result should match");
}

#[test]
fn result_comparison_negative_rules_fix_003_tst_011() {
    let expected =
        json!({"required": 1, "present": "$any", "redacted": "$redacted", "none": "$absent"});
    let actual = harness::driver::ActualValue::object([
        ("required", harness::driver::ActualValue::number(2)),
        (
            "redacted",
            harness::driver::ActualValue::string("sensitive-value"),
        ),
        ("none", harness::driver::ActualValue::string("not-null")),
    ]);
    let error =
        compare_result(&expected, &actual).expect_err("all result rules must reject mismatches");
    assert!(error.contains("required"));
    assert!(error.contains("present"));
    assert!(error.contains("redacted"));
    assert!(error.contains("none"));
}

#[test]
fn error_comparison_positive_rules_fix_004_tst_011() {
    let expected = json!({
        "code": "BV-TRANSPORT-001",
        "statusCode": 503,
        "retryable": true,
        "attempts": 2,
        "retryAfter": 5,
        "detailsKeys": ["requestId", "region"],
        "hintContains": ["CONTACT", "operator"],
        "serverMessage": "sealed"
    });
    let actual = harness::driver::ActualError::new("BV-TRANSPORT-001")
        .status_code(Some(503))
        .retryable(true)
        .attempts(2)
        .retry_after(Some(5))
        .detail("requestId", json!("id"))
        .detail("region", json!("test"))
        .hint("Please CONTACT the operator")
        .server_message("sealed");
    compare_error(&expected, &actual).expect("error should match");
}

#[test]
fn error_comparison_negative_rules_fix_004_tst_011() {
    let expected = json!({
        "code": "BV-TRANSPORT-001",
        "statusCode": 503,
        "retryable": true,
        "attempts": 2,
        "retryAfter": 5,
        "detailsKeys": ["requestId", "region"],
        "hintContains": ["operator"],
        "serverMessage": "sealed"
    });
    let actual = harness::driver::ActualError::new("BV-OTHER-999")
        .status_code(Some(500))
        .retryable(false)
        .attempts(1)
        .retry_after(None)
        .detail("requestId", json!("id"))
        .hint("unhelpful")
        .server_message("different");
    let error = compare_error(&expected, &actual).expect_err("all error fields must be checked");
    assert!(error.contains("code"));
    assert!(error.contains("statusCode"));
    assert!(error.contains("retryable"));
    assert!(error.contains("attempts"));
    assert!(error.contains("retryAfter"));
    assert!(error.contains("region"));
    assert!(error.contains("operator"));
    assert!(error.contains("serverMessage"));
}

#[test]
fn fake_transport_honours_respond_in_order_tst_011() {
    let fixture = Fixture::synthetic_with_exchanges(vec![
        json!({"respond": {"status": 200, "body": {"step": 1}}}),
        json!({"respond": {"status": 204}}),
    ]);
    let mut transport = FakeTransport::from_fixture(&fixture).expect("script must parse");
    assert_eq!(
        transport.next().expect("first exchange").status(),
        Some(200)
    );
    assert_eq!(
        transport.next().expect("second exchange").status(),
        Some(204)
    );
    assert!(transport.next().is_err(), "exhausted scripts must fail");
}

#[test]
fn fake_transport_honours_each_fail_mode_tst_011() {
    for mode in [
        "connection_refused",
        "timeout",
        "tls_verify",
        "tls_handshake",
        "reset",
        "dns",
    ] {
        let fixture = Fixture::synthetic_with_exchanges(vec![json!({"fail": mode})]);
        let mut transport = FakeTransport::from_fixture(&fixture).expect("failure must parse");
        let outcome = transport.next().expect("failure mode must be returned");
        assert_eq!(
            outcome,
            harness::transport::ScriptedOutcome::Fail(
                TransportFailure::parse(mode).expect("known mode")
            )
        );
    }
}
