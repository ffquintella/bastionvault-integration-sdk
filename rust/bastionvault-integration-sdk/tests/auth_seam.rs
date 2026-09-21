//! M2a: the executor-to-`TokenSource` seam (D-M2-9), asserted through the real request
//! path rather than through the source in isolation.
//!
//! Five of this project's defects were found by reading the request path and none by
//! running a suite, so these tests are deliberately about the path: which code a failed
//! resolution produces and in what order the arms are read (D-M2-18 item 1), what the
//! observer is handed (`CFG-080`/`TST-051`), and what a request in flight keeps when the
//! token changes under it (`CFG-070`).

use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use bastionvault_integration_sdk::{
    Client, ClientConfigBuilder, EnvironmentSource, Error, FakeTransport, RequestEvent, RequestObserver,
    RequestOptions, RetryPolicy, SecretString, TokenCallback, TokenFuture, TokenSource, TokenSourceKind,
    Transport, TransportRequest, TransportResponse,
};

/// What a scripted `TokenCallback` answers with.
#[derive(Debug, Clone, Copy)]
enum Answer {
    Token,
    /// An application error with no BastionVault code of its own — arm 3, `BV-AUTH-017`.
    Plain,
    /// The application reporting a cancellation — arm 1, `BV-TRANSPORT-005`.
    Cancelled,
    /// An error that already carries a code — arm 2, passed through unwrapped.
    Coded,
}

#[derive(Debug)]
struct ScriptedCallback {
    calls: Arc<AtomicU32>,
    answer: Answer,
}

impl TokenCallback for ScriptedCallback {
    fn resolve(&self) -> TokenFuture {
        let call = self.calls.fetch_add(1, Ordering::SeqCst) + 1;
        let answer = self.answer;
        Box::pin(async move {
            match answer {
                Answer::Token => Ok(SecretString::new(format!("s.FAKEresolved{call:08}"))),
                Answer::Plain => Err(Box::new(std::io::Error::other("the KMS is unreachable"))
                    as Box<dyn std::error::Error + Send + Sync>),
                Answer::Cancelled => Err(Box::new(std::io::Error::new(
                    std::io::ErrorKind::Interrupted,
                    "the caller cancelled",
                )) as Box<dyn std::error::Error + Send + Sync>),
                // `Error` is not publicly constructible, so the coded case is produced the
                // way a real source produces one: by making a request that fails. This one
                // is a client-side `BV-INPUT-006` from an unsupported option (TRN-017).
                Answer::Coded => {
                    let error = coded_error().await;
                    Err(Box::new(error) as Box<dyn std::error::Error + Send + Sync>)
                }
            }
        })
    }
}

/// Produces a real, coded [`Error`] the way M2b's `Login` source will: out of the SDK's own
/// request path. `BV-INPUT-006` is raised client-side, so no transport exchange is needed.
async fn coded_error() -> Error {
    let transport = Arc::new(FakeTransport::new());
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address("https://vault.example.com:8200")
        .transport(transport)
        .build()
        .expect("valid config");
    let client = Client::new(config).expect("valid client");
    let options = RequestOptions {
        wrap_ttl: Some(Duration::from_secs(60)),
        ..RequestOptions::default()
    };
    client
        .logical()
        .read("secret/data/x", Some(options))
        .await
        .expect_err("BV-INPUT-006 is raised client-side")
}

fn callback_source(answer: Answer) -> (Arc<TokenSource>, Arc<AtomicU32>) {
    let calls = Arc::new(AtomicU32::new(0));
    let source = Arc::new(TokenSource::callback(Arc::new(ScriptedCallback {
        calls: Arc::clone(&calls),
        answer,
    })));
    (source, calls)
}

fn client_with_source(source: Arc<TokenSource>, transport: Arc<dyn Transport>) -> Client {
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address("https://vault.example.com:8200")
        .transport(transport)
        .token_source(source)
        .retry_policy(RetryPolicy {
            max_attempts: 3,
            ..RetryPolicy::default()
        })
        .build()
        .expect("valid config");
    Client::new(config).expect("valid client")
}

#[tokio::test]
async fn a_source_failure_on_the_request_path_is_auth_017_with_no_request_issued_d_m2_16() {
    let transport = Arc::new(FakeTransport::new());
    let (source, calls) = callback_source(Answer::Plain);
    let client = client_with_source(source, transport.clone());

    let error = client
        .logical()
        .read("secret/data/x", None)
        .await
        .expect_err("a broken source must fail the request");
    assert_eq!(error.code(), "BV-AUTH-017");
    assert_eq!(error.attempts(), 0, "no request was issued");
    assert!(!error.retryable(), "ERR-006's retryable set is closed and does not list BV-AUTH-017");
    // ERR-020/TRN-054: the error carries the request-scoped fields, so it is a complete
    // SDK error and not a bare runtime failure escaping the path.
    assert_eq!(error.method(), Some("GET"));
    assert_eq!(error.path(), Some("secret/data/x"));
    assert_eq!(error.address(), Some("https://vault.example.com:8200"));
    // D-M2-16: the source's own error is preserved as the cause.
    assert!(
        std::error::Error::source(&error)
            .expect("a cause")
            .to_string()
            .contains("the KMS is unreachable")
    );
    assert_eq!(calls.load(Ordering::SeqCst), 1);
    assert!(transport.recorded_requests().is_empty(), "nothing may reach the wire");
}

/// **D-M2-18 item 1: the arm order is load-bearing.** .NET reads cancellation *before* the
/// generic guard, and Rust has no exception filters, so the order is written out in
/// `token_source::classify_resolution_failure`. Inverted, this test reports `BV-AUTH-017`.
#[tokio::test]
async fn a_cancelled_resolution_on_the_request_path_is_transport_005_not_auth_017_d_m2_18_1() {
    let transport = Arc::new(FakeTransport::new());
    let (source, _) = callback_source(Answer::Cancelled);
    let client = client_with_source(source, transport.clone());

    let error = client
        .logical()
        .read("secret/data/x", None)
        .await
        .expect_err("a cancelled resolution must fail the request");
    assert_eq!(
        error.code(),
        "BV-TRANSPORT-005",
        "cancellation is read before the generic guard; inverted, this is BV-AUTH-017"
    );
    assert_eq!(error.attempts(), 0);
    assert!(transport.recorded_requests().is_empty());
}

/// D-M2-18 item 3: an `Error` that already carries a code is **not** wrapped, so M2b's
/// `Login` source keeps its recogniser codes — `AUT-003`'s replay keys on `BV-AUTHZ-001`
/// specifically, and wrapping would break it outright.
#[tokio::test]
async fn a_coded_source_error_reaches_the_caller_unwrapped_d_m2_18_3() {
    let transport = Arc::new(FakeTransport::new());
    let (source, _) = callback_source(Answer::Coded);
    let client = client_with_source(source, transport);

    let error = client
        .logical()
        .read("secret/data/x", None)
        .await
        .expect_err("must fail");
    assert_eq!(error.code(), "BV-INPUT-006", "a coded error is passed through, not re-coded");
}

/// `CFG-020`'s first MUST (D-M1c-24), on the new seam: a login path carries no token
/// header, and resolution is **skipped entirely** rather than resolved-and-discarded — so a
/// `Login` source cannot recurse into a login in order to send one.
#[tokio::test]
async fn a_login_path_skips_resolution_entirely_d_m2_9() {
    let transport = Arc::new(FakeTransport::new());
    transport.script_response(TransportResponse {
        status: 200,
        headers: Vec::new(),
        body: br#"{"auth":{"client_token":"s.FAKEissued0000000000000"}}"#.to_vec(),
    });
    // A source that would *fail* if it were resolved at all: reaching it is the defect.
    let (source, calls) = callback_source(Answer::Plain);
    let client = client_with_source(source, transport.clone());

    client
        .logical()
        .write("auth/userpass/login/alice", None, None)
        .await
        .expect("a login must not resolve the token source");
    assert_eq!(calls.load(Ordering::SeqCst), 0, "resolution must be skipped, not attempted");
    let sent = &transport.recorded_requests()[0];
    assert!(
        !sent.headers.iter().any(|(name, _)| name.eq_ignore_ascii_case("X-BastionVault-Token")),
        "TRN-015: a login carries no token header"
    );
}

/// `RequestOptions.token` is honoured ahead of the source (`CFG-060`), which is what makes
/// `AUT-080`'s single resolution in `renew_self` possible.
#[tokio::test]
async fn an_explicit_per_call_token_is_used_without_resolving_the_source_cfg_060() {
    let transport = Arc::new(FakeTransport::new());
    transport.script_response(TransportResponse {
        status: 200,
        headers: Vec::new(),
        body: b"{}".to_vec(),
    });
    let (source, calls) = callback_source(Answer::Plain);
    let client = client_with_source(source, transport.clone());
    let options = RequestOptions {
        token: Some(SecretString::new("s.FAKEpinned0000000000000")),
        ..RequestOptions::default()
    };

    client
        .logical()
        .read("secret/data/x", Some(options))
        .await
        .expect("the pinned token is used as-is");
    assert_eq!(calls.load(Ordering::SeqCst), 0);
    let sent = &transport.recorded_requests()[0];
    assert!(
        sent.headers
            .iter()
            .any(|(name, value)| name.eq_ignore_ascii_case("X-BastionVault-Token")
                && value == "s.FAKEpinned0000000000000")
    );
}

/// A transport that parks the first request until it is released, so a test can act on the
/// client **while a request is in flight** — which is the only way `CFG-070`'s second
/// sentence is assertable at all.
#[derive(Debug)]
struct ParkingTransport {
    inner: Arc<FakeTransport>,
    in_flight: Arc<AtomicBool>,
    release: Arc<AtomicBool>,
}

impl Transport for ParkingTransport {
    fn send<'a>(
        &'a self,
        request: TransportRequest,
    ) -> std::pin::Pin<Box<dyn std::future::Future<Output = Result<TransportResponse, Error>> + Send + 'a>> {
        Box::pin(async move {
            self.in_flight.store(true, Ordering::SeqCst);
            while !self.release.load(Ordering::SeqCst) {
                tokio::task::yield_now().await;
            }
            self.inner.send(request).await
        })
    }

    fn supports_custom_verbs(&self) -> bool {
        true
    }
}

/// **`CFG-070`, re-opened onto the baseline by D-M2-11(c) and owned by M2a.**
///
/// Its M1a justification was "a reference read is atomic, no lock is needed", which D-M2-9
/// invalidated by making resolution asynchronous and side-effecting. What the requirement
/// actually promises is that **in-flight requests keep the token they started with**, and
/// that rests on each pass resolving exactly once, above the retry loop (D-M1b-9). Asserted
/// here under real concurrency: a `set_token` landing mid-flight must not change the token
/// the in-flight request sends, and must not cause a second resolution.
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn an_in_flight_request_keeps_the_token_it_started_with_cfg_070_d_m1b_9() {
    let inner = Arc::new(FakeTransport::new());
    inner.script_response(TransportResponse {
        status: 200,
        headers: Vec::new(),
        body: b"{}".to_vec(),
    });
    let in_flight = Arc::new(AtomicBool::new(false));
    let release = Arc::new(AtomicBool::new(false));
    let transport: Arc<dyn Transport> = Arc::new(ParkingTransport {
        inner: Arc::clone(&inner),
        in_flight: Arc::clone(&in_flight),
        release: Arc::clone(&release),
    });
    let (source, calls) = callback_source(Answer::Token);
    let client = Arc::new(client_with_source(source, transport));

    let reader = {
        let client = Arc::clone(&client);
        tokio::spawn(async move { client.logical().read("secret/data/x", None).await })
    };
    // The request has been handed to the transport, so its token is already snapshotted.
    while !in_flight.load(Ordering::SeqCst) {
        tokio::task::yield_now().await;
    }
    client.set_token(SecretString::new("s.FAKEswapped0000000000000"));
    release.store(true, Ordering::SeqCst);
    reader.await.expect("no panic").expect("the request succeeds");

    let sent = &inner.recorded_requests()[0];
    let token = sent
        .headers
        .iter()
        .find(|(name, _)| name.eq_ignore_ascii_case("X-BastionVault-Token"))
        .map(|(_, value)| value.clone())
        .expect("the request carried a token");
    assert_eq!(
        token, "s.FAKEresolved00000001",
        "CFG-070: an in-flight request keeps the token it started with, not the swapped-in one"
    );
    assert_eq!(calls.load(Ordering::SeqCst), 1, "one pass resolves exactly once (D-M1b-9)");
    // AUT-001: the write replaced the source with a Static one, and it is visible afterwards.
    assert_eq!(client.auth().token_source().kind(), TokenSourceKind::Static);
    assert_eq!(
        client.auth().current_token().map(|token| token.reveal().to_owned()),
        Some("s.FAKEswapped0000000000000".to_owned())
    );
}

#[derive(Debug, Default)]
struct RecordingObserver {
    events: Mutex<Vec<RequestEvent>>,
}

impl RequestObserver for RecordingObserver {
    fn on_request_completed(&self, event: &RequestEvent) {
        self.events.lock().expect("not poisoned").push(event.clone());
    }
}

/// **The leak .NET shipped and its TST-051 instrument caught** (DR-0006 addendum, defect 1).
///
/// `AUT-080`'s `auth/token/renew/{token}` and `Auth.Token.Lookup`'s
/// `auth/token/lookup/{token}` put a live token in the request path, and
/// `RequestEvent.path` is the second consumer of that string after the error. `ERR-003`
/// already covered the first; D-M2-7 named the observer because nothing covered the second.
#[tokio::test]
async fn the_observer_never_sees_an_unredacted_token_in_the_request_path_cfg_080_tst_051_err_003() {
    for (path, method) in [
        ("auth/token/lookup/s.FAKEleak00000000000000", "GET"),
        ("auth/token/renew/s.FAKEleak00000000000000", "POST"),
        ("auth/token/revoke/s.FAKEleak00000000000000", "POST"),
    ] {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 403,
            headers: Vec::new(),
            body: br#"{"error":"Permission denied."}"#.to_vec(),
        });
        let observer = Arc::new(RecordingObserver::default());
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("https://vault.example.com:8200")
            .token("s.FAKEtoken0000000000000000")
            .transport(transport)
            .request_observer(Arc::clone(&observer) as Arc<dyn RequestObserver>)
            .retry_policy(RetryPolicy {
                max_attempts: 1,
                ..RetryPolicy::default()
            })
            .build()
            .expect("valid config");
        let client = Client::new(config).expect("valid client");

        let error = client
            .logical()
            .raw(method, &format!("/v1/{path}"), None, None)
            .await
            .expect_err("a 403 is an error");
        assert_eq!(error.code(), "BV-AUTHZ-001");
        // ERR-003 on the error.
        assert!(!format!("{error}").contains("FAKEleak"), "the rendered error leaked: {error}");
        let redacted_path = error.path().expect("ERR-001 carries the path");
        assert!(redacted_path.ends_with("/<redacted>"), "{redacted_path}");
        assert!(!redacted_path.contains("FAKEleak"));
        // CFG-080 on the observer.
        let events = observer.events.lock().expect("not poisoned").clone();
        assert_eq!(events.len(), 1);
        assert!(
            !events[0].path.contains("FAKEleak"),
            "RequestEvent.path leaked a live token: {}",
            events[0].path
        );
        assert!(events[0].path.contains("<redacted>"));
    }
}

/// D-M2-9's counter split, as far as M2a can observe it: one caller-visible operation has
/// **one** `request_id` across every attempt, and the reported count accumulates.
#[tokio::test]
async fn one_logical_operation_keeps_one_request_id_across_its_attempts_d_m1b_8_d_m2_9() {
    let transport = Arc::new(FakeTransport::new());
    for _ in 0..3 {
        transport.script_response(TransportResponse {
            status: 503,
            headers: Vec::new(),
            body: br#"{"error":"cluster node is unhealthy"}"#.to_vec(),
        });
    }
    let observer = Arc::new(RecordingObserver::default());
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address("https://vault.example.com:8200")
        .token("s.FAKEtoken0000000000000000")
        .transport(transport)
        .request_observer(Arc::clone(&observer) as Arc<dyn RequestObserver>)
        .retry_policy(RetryPolicy {
            max_attempts: 3,
            initial_backoff: Duration::from_millis(0),
            ..RetryPolicy::default()
        })
        .build()
        .expect("valid config");
    let client = Client::new(config).expect("valid client");

    let error = client
        .logical()
        .read("secret/data/x", None)
        .await
        .expect_err("three 503s exhaust the policy");
    assert_eq!(error.code(), "BV-SERVER-002");
    assert_eq!(error.attempts(), 3);

    let events = observer.events.lock().expect("not poisoned").clone();
    assert_eq!(events.len(), 3);
    let ids: std::collections::BTreeSet<&str> = events.iter().map(|event| event.request_id.as_str()).collect();
    assert_eq!(ids.len(), 1, "D-M1b-8: one id per logical operation, stable across attempts");
    assert_eq!(
        events.iter().map(|event| event.attempt).collect::<Vec<_>>(),
        vec![1, 2, 3],
        "the reported count is the accumulated attempts_total"
    );
}
