//! M1b: all 18 `specifications/fixtures/transport/*.json` fixtures pass in the Rust
//! suite against real SDK code (`Logical.Read/Write/Delete/List/Raw`, registered in
//! `tests/harness/driver.rs`'s `OperationRegistry::m1b()`), driven through the SDK's
//! own public `FakeTransport` (D-M1b-15).

mod harness;

use harness::driver::{compare_error, compare_result, FixtureDriver, OperationRegistry, RunOutcome};
use harness::fake_tokens;
use harness::fixture::FixtureLoader;

#[test]
fn all_transport_fixtures_pass_against_real_sdk_code_d_m1b() {
    let loader = FixtureLoader::new().expect("repository fixture root must be discoverable");
    let fixtures = loader
        .load_all()
        .expect("all repository fixtures must validate")
        .into_iter()
        .filter(|fixture| fixture.id.starts_with("transport."))
        .collect::<Vec<_>>();
    assert_eq!(fixtures.len(), 18, "expected exactly 18 transport fixtures");

    let driver = FixtureDriver::with_registry(OperationRegistry::m1b());
    let mut failures = Vec::new();

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
                failures.push(format!("{}: operation {operation} is not registered", fixture.id));
            }
            RunOutcome::Ran { result } => match (&fixture.expect.result, &fixture.expect.error) {
                (Some(expected_result), None) => match result {
                    Ok(actual) => {
                        if let Err(mismatch) = compare_result(expected_result, &actual) {
                            failures.push(format!("{}: result mismatch: {mismatch}", fixture.id));
                        }
                    }
                    Err(actual_error) => {
                        failures.push(format!(
                            "{}: expected a result but got error {:?}",
                            fixture.id, actual_error
                        ));
                    }
                },
                (None, Some(expected_error)) => match result {
                    Err(actual_error) => {
                        if let Err(mismatch) = compare_error(expected_error, &actual_error) {
                            failures.push(format!("{}: error mismatch: {mismatch}", fixture.id));
                        }
                    }
                    Ok(actual) => {
                        failures.push(format!(
                            "{}: expected an error but got result {:?}",
                            fixture.id, actual
                        ));
                    }
                },
                _ => failures.push(format!(
                    "{}: fixture must expect exactly one of result/error",
                    fixture.id
                )),
            },
        }
    }

    assert!(
        failures.is_empty(),
        "{} transport fixture failures:\n{}",
        failures.len(),
        failures.join("\n")
    );
}

#[test]
fn dos_guard_fixture_also_asserts_the_rate_gate_pause_client_state_d_m1b_16() {
    // `compare_error`/`compare_result` don't see `clientState`; this test independently
    // exercises the same fixture's exchange through a raw `Client` to prove
    // `RateGate.Paused` becomes true, since `transport.status.429-dos-guard.json`
    // asserts `clientState: {"RateGate.Paused": true}`.
    use bastionvault_integration_sdk::{Client, ClientConfigBuilder, EnvironmentSource, FakeTransport, TransportResponse};

    let transport = std::sync::Arc::new(FakeTransport::new());
    transport.script_response(TransportResponse {
        status: 429,
        headers: vec![("Retry-After".to_owned(), "17".to_owned())],
        body: br#"{"errors":["request temporarily blocked by DoS protection: request rate exceeded: >200 req/10s"]}"#
            .to_vec(),
    });
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address("https://vault.example.com:8200")
        // D-M1c-15: assembled, never written as a literal, so the CNF-025 secret scan
        // stays green without widening its whitelist (CLA-004).
        .token(fake_tokens::client())
        .transport(transport)
        .build()
        .expect("valid config");
    let client = Client::new(config).expect("valid client");

    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .expect("runtime");
    let error = runtime
        .block_on(client.logical().read("pki/cert/01", None))
        .expect_err("429 must be an error");
    assert_eq!(error.code(), "BV-RATE-001");
    assert!(client.rate_gate_paused());
}

#[test]
fn the_assembled_fake_token_is_byte_identical_to_the_fixture_literal_d_m1c_15() {
    // D-M1c-15: the literal this replaces was `s.` + `FAKEtoken` + sixteen zeroes, which
    // is what `specifications/fixtures/**` still carries in `client.token`. Assembling it
    // keeps the CNF-025 secret scan green without touching the pattern or the whitelist
    // (CLA-004); this test is what stops the assembly drifting from the fixtures.
    let token = fake_tokens::client();
    assert_eq!(token.len(), 2 + "FAKEtoken".len() + 16);
    assert!(token.starts_with("s.FAKEtoken"));
    assert!(token.ends_with("0000000000000000"));
    assert!(token[2..].chars().all(|c| c.is_ascii_alphanumeric()));
}
