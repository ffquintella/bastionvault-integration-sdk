//! M1c: Appendix B's catalogue invariants, the ERR-002/ERR-003 string rules, the
//! ERR-034/ERR-040 enrichment as a caller meets it, and ERR-050's warning log.
//!
//! Everything here is driven through the public surface and a real [`Client`]. The
//! recognition and enrichment rules are deliberately **not** public (D-M1c-7), so they
//! are exercised where a caller meets them; their own edge cases are unit-tested beside
//! the code in `src/recognition.rs`, `src/enrichment.rs` and `src/mapping.rs`.

mod harness;

use std::sync::{Arc, Mutex};

use bastionvault_integration_sdk::{
    Client, ClientConfigBuilder, ClientLogger, DetailValue, EnvironmentSource, ErrorCatalog,
    ErrorCatalogEntry, ErrorCategory, Error, FakeTransport, RateGate, RetryPolicy,
    TransportFailureKind, TransportResponse, error_codes,
};
use harness::fake_tokens;
use harness::fixture::FixtureLoader;

const ADDRESS: &str = "https://vault.example.com:8200";

// --------------------------------------------------------------------------- //
// Catalogue invariants (ERR-036, ERR-037, ERR-010, ERR-006, ERR-030, ERR-031,
// ERR-033, ERR-005, ERR-004).
// --------------------------------------------------------------------------- //

#[test]
fn catalogue_is_exhaustive_and_every_message_is_unique_err_036_err_037() {
    let all = ErrorCatalog::all();
    assert!(!all.is_empty());

    let mut codes = all.iter().map(ErrorCatalogEntry::code).collect::<Vec<_>>();
    let mut messages = all.iter().map(ErrorCatalogEntry::message).collect::<Vec<_>>();
    let total = all.len();
    codes.sort_unstable();
    codes.dedup();
    messages.sort_unstable();
    messages.dedup();

    assert_eq!(codes.len(), total, "no two codes may share a code");
    assert_eq!(messages.len(), total, "no two codes may share a message");

    for entry in all {
        assert!(!entry.message().trim().is_empty(), "{}", entry.code());
        assert!(!entry.hint().trim().is_empty(), "{}", entry.code());
        assert!(!entry.name().trim().is_empty(), "{}", entry.code());
    }
}

#[test]
fn catalogue_matches_the_generator_intermediate_row_for_row_and_in_appendix_order_err_036_err_010() {
    // Compared against `tools/error-catalogue/catalogue.json` — the generator's
    // checked-in intermediate — not against a second markdown parser written in Rust.
    // The repo-gates regeneration job ties that file to Appendix B; a second parser here
    // would be a third chance to read the appendix differently, which is exactly what
    // D-M1c-1 removes.
    let loader = FixtureLoader::new().expect("repository root must be discoverable");
    let path = loader
        .repository_root()
        .join("tools/error-catalogue/catalogue.json");
    let intermediate: serde_json::Value =
        serde_json::from_slice(&std::fs::read(&path).expect("catalogue.json must be readable"))
            .expect("catalogue.json must be JSON");
    let rows = intermediate["codes"].as_array().expect("codes must be an array");

    assert_eq!(rows.len(), ErrorCatalog::all().len());
    for (row, entry) in rows.iter().zip(ErrorCatalog::all()) {
        assert_eq!(row["code"].as_str(), Some(entry.code()));
        assert_eq!(row["name"].as_str(), Some(entry.name()));
        assert_eq!(row["message"].as_str(), Some(entry.message()));
        assert_eq!(row["hint"].as_str(), Some(entry.hint()));
        assert_eq!(row["retryable"].as_bool(), Some(entry.retryable()));

        // ERR-010: three digits within a category.
        let (prefix, number) = entry.code().rsplit_once('-').expect("BV-<CATEGORY>-<NNN>");
        assert!(prefix.starts_with("BV-"), "{}", entry.code());
        assert_eq!(number.len(), 3, "{}", entry.code());
        assert!(number.chars().all(|c| c.is_ascii_digit()), "{}", entry.code());
    }
}

#[test]
fn retryable_is_true_for_exactly_the_specified_set_err_006() {
    let mut expected = vec![
        error_codes::TRANSPORT_CONNECTION_FAILED,
        error_codes::TRANSPORT_TIMEOUT,
        error_codes::TRANSPORT_TLS_ERROR,
        error_codes::SERVER_UNAVAILABLE,
        error_codes::SERVER_STANDBY,
        error_codes::RATE_NAMESPACE_RATE_QUOTA_EXCEEDED,
        error_codes::DISCOVERY_NODE_UNAVAILABLE,
    ];
    let mut actual = ErrorCatalog::all()
        .iter()
        .filter(|entry| entry.retryable())
        .map(ErrorCatalogEntry::code)
        .collect::<Vec<_>>();
    expected.sort_unstable();
    actual.sort_unstable();
    assert_eq!(actual, expected);
}

#[test]
fn every_category_matches_its_code_prefix_err_004() {
    for (code, category) in [
        (error_codes::CONFIG_INVALID_ADDRESS, ErrorCategory::Configuration),
        (error_codes::INPUT_SERVER_REJECTED_REQUEST, ErrorCategory::Input),
        (error_codes::TRANSPORT_TLS_ERROR, ErrorCategory::Transport),
        (error_codes::PROTOCOL_UNEXPECTED_RESPONSE, ErrorCategory::Protocol),
        (error_codes::AUTH_NO_TOKEN, ErrorCategory::Authentication),
        (error_codes::AUTHZ_PERMISSION_DENIED, ErrorCategory::Authorization),
        (error_codes::NOT_FOUND_PATH_NOT_FOUND, ErrorCategory::NotFound),
        (error_codes::CONFLICT_RECORDING_DIGEST_MISMATCH, ErrorCategory::Conflict),
        (error_codes::RATE_NAMESPACE_RATE_QUOTA_EXCEEDED, ErrorCategory::RateLimit),
        (error_codes::QUOTA_NAMESPACE_QUOTA_EXCEEDED, ErrorCategory::Quota),
        (error_codes::SERVER_SEALED, ErrorCategory::ServerState),
        (error_codes::DISCOVERY_NODE_UNAVAILABLE, ErrorCategory::Discovery),
        (error_codes::KV_CAS_MISMATCH, ErrorCategory::Engine),
        (error_codes::RUSTION_ENVELOPE_REPLAY, ErrorCategory::Engine),
    ] {
        assert_eq!(ErrorCatalog::get(code).expect(code).category(), category, "{code}");
    }
}

#[test]
fn get_returns_none_for_an_unknown_code_and_never_panics_err_036() {
    assert!(ErrorCatalog::get("BV-NOPE-999").is_none());
    assert!(ErrorCatalog::get("").is_none());
    assert!(ErrorCatalog::get(error_codes::SERVER_SEALED).is_some());
}

#[test]
fn an_entry_is_a_readable_comparable_value_err_036() {
    let entry = ErrorCatalog::get(error_codes::SERVER_SEALED).expect("BV-SERVER-001");
    let same = ErrorCatalog::get("BV-SERVER-001").expect("BV-SERVER-001");

    assert_eq!(entry, same);
    assert_eq!(entry.name(), "Sealed");
    assert!(!entry.retryable());
    assert!(format!("{entry:?}").contains("BV-SERVER-001"));
    assert_ne!(entry, ErrorCatalog::get(error_codes::SERVER_UNAVAILABLE).expect("BV-SERVER-002"));
}

#[test]
fn every_code_is_reachable_as_a_constant_and_as_its_literal_string_err_005() {
    // The literal form correlates logs across SDKs without referencing the constant.
    assert_eq!(error_codes::AUTHZ_PERMISSION_DENIED, "BV-AUTHZ-001");
    assert_eq!(error_codes::RATE_NAMESPACE_RATE_QUOTA_EXCEEDED, "BV-RATE-002");
    assert_eq!(error_codes::INPUT_SERVER_REJECTED_REQUEST, "BV-INPUT-100");
    for code in [
        error_codes::AUTHZ_PERMISSION_DENIED,
        error_codes::RATE_NAMESPACE_RATE_QUOTA_EXCEEDED,
        error_codes::INPUT_SERVER_REJECTED_REQUEST,
    ] {
        assert!(ErrorCatalog::get(code).is_some(), "{code}");
    }
}

#[tokio::test]
async fn every_canonical_field_is_present_and_populated_on_a_request_scoped_error_err_001() {
    let error = fail(500, "boom", "secret/data/x", |b| b).await;

    assert_eq!(error.code(), error_codes::SERVER_INTERNAL_ERROR);
    assert_eq!(error.category(), ErrorCategory::ServerState);
    assert!(!error.message().is_empty());
    assert!(!error.hint().is_empty());
    assert_eq!(error.server_message(), Some("boom"));
    assert!(error.server_errors().is_empty());
    assert_eq!(error.status_code(), Some(500));
    assert_eq!(error.retry_after(), None);
    assert!(!error.retryable());
    assert_eq!(error.method(), Some("GET"));
    assert_eq!(error.path(), Some("secret/data/x"));
    assert_eq!(error.address(), Some(ADDRESS));
    assert_eq!(error.attempts(), 1);
    assert!(error.details().contains_key("path"));
    assert!(error.timestamp() <= std::time::SystemTime::now());
}

#[test]
fn every_message_is_one_sentence_in_the_present_tense_err_030() {
    for entry in ErrorCatalog::all() {
        assert!(entry.message().ends_with('.'), "{}", entry.code());
        assert_eq!(sentence_count(entry.message()), 1, "{}", entry.code());
        assert!(!entry.message().contains(" will "), "{}", entry.code());
        assert!(!entry.message().starts_with("Please"), "{}", entry.code());
    }
}

#[test]
fn every_hint_is_at_most_two_sentences_err_031() {
    // D-M1c-13: Appendix B carried a three-sentence hint (BV-RATE-001) from M0 to M1c
    // with no gate on it. The generator now hard-fails on one; this is the same
    // invariant asserted against the compiled catalogue.
    for entry in ErrorCatalog::all() {
        assert!(
            sentence_count(entry.hint()) <= 2,
            "{}: {} sentences",
            entry.code(),
            sentence_count(entry.hint())
        );
    }
    assert_eq!(
        sentence_count(ErrorCatalog::get(error_codes::RATE_LIMITED_BY_DOS_GUARD).expect("BV-RATE-001").hint()),
        2
    );
    assert_eq!(sentence_count("Use `Sys.Batch` or `*-info` pages."), 1);
    assert_eq!(sentence_count("Do this. Then do that."), 2);
}

#[test]
fn no_hint_offers_disabling_a_security_control_as_its_first_suggestion_err_033() {
    for entry in ErrorCatalog::all() {
        for knob in ["TlsSkipVerify", "AllowInsecureHttp", "InsecureSkipVerify"] {
            let Some(index) = entry.hint().find(knob) else {
                continue;
            };
            assert!(
                sentence_count(&entry.hint()[..index]) >= 1,
                "{}: {knob} is the first suggestion",
                entry.code()
            );
            let lower = entry.hint().to_lowercase();
            assert!(
                lower.contains("never in production")
                    || lower.contains("only for isolated test networks")
                    || lower.contains("diagnostic"),
                "{}: {knob} is mentioned without a warning",
                entry.code()
            );
        }
    }
}

/// Sentences, ignoring anything inside backticks and decimal points — counted the same
/// way the generator's own ERR-031 invariant counts them.
fn sentence_count(text: &str) -> usize {
    let mut masked = String::with_capacity(text.len());
    let mut inside_backticks = false;
    for character in text.chars() {
        if character == '`' {
            if !inside_backticks {
                masked.push('X');
            }
            inside_backticks = !inside_backticks;
            continue;
        }
        masked.push(if inside_backticks { 'X' } else { character });
    }

    let bytes = masked.as_bytes();
    let mut count = 0_usize;
    let mut current_has_content = false;
    for (index, character) in masked.char_indices() {
        if matches!(character, '.' | '!' | '?') {
            // A decimal point inside a number does not end a sentence.
            let previous_is_digit = index > 0 && bytes[index - 1].is_ascii_digit();
            let next = masked[index + character.len_utf8()..].chars().next();
            if previous_is_digit && next.is_some_and(|c| c.is_ascii_digit()) {
                continue;
            }
            if next.is_none() || next.is_some_and(char::is_whitespace) {
                if current_has_content {
                    count += 1;
                }
                current_has_content = false;
                continue;
            }
        }
        if !character.is_whitespace() {
            current_has_content = true;
        }
    }
    count + usize::from(current_has_content)
}

// --------------------------------------------------------------------------- //
// ERR-002 / ERR-003: the string form, as a caller meets it.
// --------------------------------------------------------------------------- //

#[tokio::test]
async fn path_redaction_replaces_only_the_token_segments_err_003() {
    for (path, expected) in [
        ("auth/token/lookup/secret-token", "auth/token/lookup/<redacted>"),
        ("auth/token/renew/secret-token", "auth/token/renew/<redacted>"),
        ("auth/token/revoke/secret-token", "auth/token/revoke/<redacted>"),
        ("auth/token/revoke-orphan/secret-token", "auth/token/revoke-orphan/<redacted>"),
        ("secret/data/app", "secret/data/app"),
        ("auth/token/lookup/", "auth/token/lookup/"),
    ] {
        let error = fail(500, "unexplained", path, |builder| builder).await;
        assert_eq!(error.path(), Some(expected), "{path}");
    }
}

#[tokio::test]
async fn the_one_line_form_has_no_newline_even_when_the_server_message_carries_one_err_002() {
    let token = fake_tokens::client();
    let error = fail(
        500,
        "line one\nline two\r\nline three",
        &format!("auth/token/lookup/{token}"),
        |builder| builder,
    )
    .await;

    let line = error.to_string();

    assert!(!line.contains('\n'));
    assert!(!line.contains('\r'));
    assert!(!line.contains(&token), "ERR-003: no token may reach the string form");
    assert!(line.starts_with("BV-SERVER-005: "));
    assert!(line.contains(" — "));
    assert!(line.contains("[HTTP 500 GET auth/token/lookup/<redacted>]"));
    assert!(line.contains(r#"(server: "line one line two line three")"#));
}

#[test]
fn the_one_line_form_omits_the_bracket_and_the_server_clause_when_there_is_nothing_to_show_err_002() {
    let entry = ErrorCatalog::get(error_codes::CONFIG_INVALID_ADDRESS).expect("BV-CONFIG-001");
    let error = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address("not a url")
        .build()
        .expect_err("an invalid address must fail construction");

    assert_eq!(
        error.to_string(),
        format!("{}: {} — {}", entry.code(), entry.message(), entry.hint())
    );
}

#[test]
fn configuration_errors_quote_the_generated_catalogue_verbatim_err_036() {
    for (code, build) in [
        (
            error_codes::CONFIG_INVALID_ADDRESS,
            Box::new(|b: ClientConfigBuilder| b.address("not a url")) as Box<dyn Fn(ClientConfigBuilder) -> ClientConfigBuilder>,
        ),
        (
            error_codes::CONFIG_INSECURE_HTTP_NOT_ALLOWED,
            Box::new(|b: ClientConfigBuilder| b.address("http://vault.example.com:8200")),
        ),
        (
            error_codes::CONFIG_INVALID_NAMESPACE,
            Box::new(|b: ClientConfigBuilder| b.address(ADDRESS).namespace("/bad namespace")),
        ),
        (
            error_codes::CONFIG_RESERVED_HEADER,
            Box::new(|b: ClientConfigBuilder| {
                b.address(ADDRESS)
                    .headers(vec![("X-BastionVault-Token".to_owned(), "x".to_owned())])
            }),
        ),
    ] {
        let expected = ErrorCatalog::get(code).expect(code);
        let error = build(ClientConfigBuilder::new().with_environment(EnvironmentSource::None))
            .build()
            .expect_err(code);

        assert_eq!(error.code(), code);
        assert_eq!(error.message(), expected.message());
        assert_eq!(error.hint(), expected.hint());
        assert_eq!(error.category(), expected.category());
    }
}

// --------------------------------------------------------------------------- //
// ERR-034 / ERR-040 through the request path.
// --------------------------------------------------------------------------- //

#[tokio::test]
async fn a_hint_that_points_at_details_path_names_the_path_the_sdk_sent_err_034() {
    let not_found = fail(404, "unmapped", "secret/app", |builder| builder).await;

    assert_eq!(not_found.code(), error_codes::NOT_FOUND_PATH_NOT_FOUND);
    assert!(not_found.hint().contains("Details.path"));
    assert!(not_found.hint().contains("The path as sent was `secret/app`."));
    assert_eq!(
        not_found.details().get("path"),
        Some(&DetailValue::Str("secret/app".to_owned()))
    );

    // A hint that does not mention `Details.path` is left alone.
    let sealed = fail(503, "BastionVault is sealed.", "secret/app", |builder| builder).await;
    assert_eq!(sealed.code(), error_codes::SERVER_SEALED);
    assert!(!sealed.hint().contains("The path as sent was"));
}

#[tokio::test]
async fn enrichment_row_403_no_namespace_fires_only_under_auth_or_secret_err_021_err_040() {
    const NOTE: &str = "No namespace is set";

    assert!(fail(403, "Permission denied.", "secret/data/x", |b| b).await.hint().contains(NOTE));
    assert!(
        fail(403, "Permission denied.", "auth/userpass/users/bob", |b| b)
            .await
            .hint()
            .contains(NOTE)
    );
    assert!(!fail(403, "Permission denied.", "sys/health", |b| b).await.hint().contains(NOTE));
    assert!(
        !fail(403, "Permission denied.", "secret/data/x", |b| b.namespace("dti"))
            .await
            .hint()
            .contains(NOTE)
    );

    // ERR-021: a 403 is authorization, never authentication.
    assert_eq!(
        fail(403, "Permission denied.", "secret/data/x", |b| b).await.code(),
        error_codes::AUTHZ_PERMISSION_DENIED
    );
}

#[tokio::test]
async fn enrichment_rows_for_api_version_rate_gate_sealed_and_unsupported_path_err_040_cnf_043() {
    assert!(
        fail(400, "API version mismatch: not on this version.", "identity/entity-info", |b| b)
            .await
            .hint()
            .contains("Pin this call to `/v2` (RequestOptions.ApiVersion = 2).")
    );

    let throttled = fail_with_headers(
        429,
        "request temporarily blocked by DoS protection",
        "secret/data/x",
        vec![("Retry-After".to_owned(), "17".to_owned())],
    )
    .await;
    assert_eq!(throttled.code(), error_codes::RATE_LIMITED_BY_DOS_GUARD);
    assert!(throttled.hint().contains("paused for `17`s"));

    // A 429 without `Retry-After` gets no rate-gate note.
    assert!(!fail(429, "too many requests", "secret/data/x", |b| b).await.hint().contains("paused for"));

    assert!(
        fail(503, "BastionVault is sealed.", "secret/data/x", |b| b)
            .await
            .hint()
            .contains("Run `bvault operator unseal` on the node or wait for auto-unseal; the SDK will not retry.")
    );
    assert!(
        !fail(503, "cluster node is unhealthy", "secret/data/x", |b| b)
            .await
            .hint()
            .contains("bvault operator unseal")
    );

    // CNF-043 / D-M1c-6: one recognition row, no bespoke code path.
    let unsupported = fail(500, "Logical backend path not supported.", "pki/certs-info", |b| b).await;
    assert_eq!(unsupported.code(), error_codes::SERVER_UNSUPPORTED_BY_SERVER);
    assert!(unsupported.hint().contains("does not have this endpoint"));
}

#[tokio::test]
async fn enrichment_rows_for_tls_without_a_ca_and_connection_refused_to_the_default_address_err_040() {
    const TLS_NOTE: &str = "Provide the server's CA bundle via `CaCertPath` or `BASTIONVAULT_CACERT`.";
    const ADDRESS_NOTE: &str = "No `Address` was configured; the default is `https://127.0.0.1:8200`.";

    assert!(
        fail_transport(TransportFailureKind::TlsVerify, |b| b).await.hint().contains(TLS_NOTE)
    );
    assert!(
        !fail_transport(TransportFailureKind::TlsVerify, |b| b.ca_cert_pem(self_signed_ca_pem()))
            .await
            .hint()
            .contains(TLS_NOTE)
    );

    assert!(
        fail_transport(TransportFailureKind::ConnectionRefused, |b| b
            .address("https://127.0.0.1:8200"))
        .await
        .hint()
        .contains(ADDRESS_NOTE)
    );
    assert!(
        !fail_transport(TransportFailureKind::ConnectionRefused, |b| b)
            .await
            .hint()
            .contains(ADDRESS_NOTE)
    );
}

#[tokio::test]
async fn the_two_deferred_enrichment_rows_are_absent_not_stubbed_err_040() {
    // D-M1c-5: no branch exists for `Details.namespace_operable == false` (M3) or for a
    // 404 on a KV v2 mount (M4), so neither can fire early and neither is dead code the
    // CNF-010 floor would have to excuse.
    let not_found = fail(404, "unmapped", "secret/app", |b| b).await;

    assert!(!not_found.hint().contains("This mount is KV v2"));
    assert!(!not_found.hint().contains("child-visible"));
}

// --------------------------------------------------------------------------- //
// ERR-050.
// --------------------------------------------------------------------------- //

#[derive(Debug, Default)]
struct RecordingLogger {
    lines: Mutex<Vec<String>>,
}

impl RecordingLogger {
    fn lines(&self) -> Vec<String> {
        self.lines.lock().expect("logger mutex").clone()
    }
}

impl ClientLogger for RecordingLogger {
    fn warn(&self, message: &str) {
        self.lines.lock().expect("logger mutex").push(message.to_owned());
    }
}

#[tokio::test]
async fn server_warnings_are_surfaced_and_logged_at_warning_level_and_never_become_errors_err_050() {
    let logger = Arc::new(RecordingLogger::default());
    let transport = Arc::new(FakeTransport::new());
    transport.script_response(TransportResponse {
        status: 200,
        headers: vec![("Content-Type".to_owned(), "application/json".to_owned())],
        body: br#"{"data":{"k":"v"},"warnings":["ttl was capped","policy is deprecated"]}"#.to_vec(),
    });

    let client = build_client(transport, |builder| builder.logger(logger.clone()));
    let response = client
        .logical()
        .read("x", None)
        .await
        .expect("a warning is never turned into an error")
        .expect("a body was returned");

    assert_eq!(response.warnings, vec!["ttl was capped", "policy is deprecated"]);
    let lines = logger.lines();
    assert_eq!(lines.len(), 2);
    assert!(lines.iter().all(|line| line.starts_with("BastionVault server warning: ")));
}

#[tokio::test]
async fn warnings_default_to_an_empty_list_and_non_string_entries_are_kept_verbatim_err_050() {
    let logger = Arc::new(RecordingLogger::default());
    let transport = Arc::new(FakeTransport::new());
    for body in [
        br#"{"data":{"k":"v"}}"#.to_vec(),
        br#"{"data":{"k":"v"},"warnings":"not-an-array"}"#.to_vec(),
        br#"{"data":{"k":"v"},"warnings":[{"detail":"structured"},"",null]}"#.to_vec(),
    ] {
        transport.script_response(TransportResponse {
            status: 200,
            headers: vec![("Content-Type".to_owned(), "application/json".to_owned())],
            body,
        });
    }

    let client = build_client(transport, |builder| builder.logger(logger.clone()));
    let logical = client.logical();

    assert!(logical.read("x", None).await.unwrap().unwrap().warnings.is_empty());
    assert!(logical.read("x", None).await.unwrap().unwrap().warnings.is_empty());
    let mixed = logical.read("x", None).await.unwrap().unwrap().warnings;

    assert_eq!(mixed, vec![r#"{"detail":"structured"}"#, "null"]);
    assert_eq!(logger.lines().len(), 2);
}

// --------------------------------------------------------------------------- //

fn build_client(
    transport: Arc<FakeTransport>,
    configure: impl FnOnce(ClientConfigBuilder) -> ClientConfigBuilder,
) -> Client {
    let builder = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address(ADDRESS)
        .token(fake_tokens::client())
        .transport(transport)
        .rate_gate(RateGate {
            rate_per_second: 0,
            ..RateGate::default()
        })
        .retry_policy(RetryPolicy {
            max_attempts: 1,
            ..RetryPolicy::default()
        });
    Client::new(configure(builder).build().expect("valid config")).expect("valid client")
}

async fn fail(
    status: u16,
    server_message: &str,
    path: &str,
    configure: impl FnOnce(ClientConfigBuilder) -> ClientConfigBuilder,
) -> Error {
    fail_inner(status, server_message, path, Vec::new(), configure).await
}

async fn fail_with_headers(
    status: u16,
    server_message: &str,
    path: &str,
    headers: Vec<(String, String)>,
) -> Error {
    fail_inner(status, server_message, path, headers, |builder| builder).await
}

async fn fail_inner(
    status: u16,
    server_message: &str,
    path: &str,
    mut headers: Vec<(String, String)>,
    configure: impl FnOnce(ClientConfigBuilder) -> ClientConfigBuilder,
) -> Error {
    let transport = Arc::new(FakeTransport::new());
    headers.push(("Content-Type".to_owned(), "application/json".to_owned()));
    transport.script_response(TransportResponse {
        status,
        headers,
        body: serde_json::to_vec(&serde_json::json!({ "error": server_message }))
            .expect("serialisable"),
    });

    build_client(transport, configure)
        .logical()
        .read(path, None)
        .await
        .expect_err("the scripted status must be an error")
}

async fn fail_transport(
    kind: TransportFailureKind,
    configure: impl FnOnce(ClientConfigBuilder) -> ClientConfigBuilder,
) -> Error {
    let transport = Arc::new(FakeTransport::new());
    transport.script_failure(kind);

    build_client(transport, configure)
        .logical()
        .read("secret/data/x", None)
        .await
        .expect_err("a transport failure must be an error")
}

/// A throwaway self-signed CA, so the TLS enrichment row's "a CA *is* configured" side
/// is exercised against real PEM the SDK parses rather than a stub.
fn self_signed_ca_pem() -> String {
    let key = rcgen::KeyPair::generate().expect("key pair");
    let mut params =
        rcgen::CertificateParams::new(vec!["bastionvault-m1c-test-ca".to_owned()]).expect("params");
    params.not_before = rcgen::date_time_ymd(2020, 1, 1);
    params.not_after = rcgen::date_time_ymd(2100, 1, 1);
    params
        .self_signed(&key)
        .expect("self-signed certificate")
        .pem()
}
