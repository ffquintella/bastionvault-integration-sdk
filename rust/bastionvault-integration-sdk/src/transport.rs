//! The transport injection seam (OVR-001), redesigned to its canonical M1b shape
//! (D-M1b-1), and per-operation options (CFG-060/061/CFG-070/071, D-M1b-13).
//!
//! The M1a trait declared no method at all; three languages had three incompatible
//! shapes (DR-0004 context). This module ships the one shape every language now shares:
//! `Transport::send(TransportRequest) -> Result<TransportResponse, Error>`, asynchronous,
//! with a transport-level failure raised as the SDK [`Error`] carrying
//! `BV-TRANSPORT-001/002/003/005` — never a raw runtime exception (D-M1b-1).

use std::collections::HashMap;
use std::fmt;
use std::future::Future;
use std::pin::Pin;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::config::ApiPrefix;
use crate::error::Error;
use crate::mapping::transport_failure_to_error;
use crate::observer::RequestObserver;
use crate::secret::SecretString;

/// A fully-constructed, already-encoded request the transport sends verbatim
/// (D-M1b-1). `method` is the literal HTTP method string, including `LIST` (TRN-010).
/// `url` is the absolute, already-encoded URL (TRN-020/021).
#[derive(Debug, Clone, PartialEq)]
pub struct TransportRequest {
    pub method: String,
    pub url: String,
    pub headers: Vec<(String, String)>,
    pub body: Option<Vec<u8>>,
    /// The per-attempt budget (RES-004); the transport's to enforce.
    pub timeout: Duration,
    pub connect_timeout: Duration,
    /// TRN-033/D-M1b-20: the transport MUST abort as soon as this many response bytes
    /// have been read, and MUST reject an over-bound `Content-Length` without reading a
    /// byte.
    pub max_response_bytes: u64,
}

impl TransportRequest {
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers
            .iter()
            .find(|(header_name, _)| header_name.eq_ignore_ascii_case(name))
            .map(|(_, value)| value.as_str())
    }
}

/// The transport's response (D-M1b-1). `body` is bytes; envelope parsing happens above
/// the transport (TRN-030 et al.), so the transport has no opinion about JSON.
#[derive(Debug, Clone, PartialEq)]
pub struct TransportResponse {
    pub status: u16,
    pub headers: Vec<(String, String)>,
    pub body: Vec<u8>,
}

impl TransportResponse {
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers
            .iter()
            .find(|(header_name, _)| header_name.eq_ignore_ascii_case(name))
            .map(|(_, value)| value.as_str())
    }
}

/// The six fixture failure kinds (D-M1b-4a) plus caller cancellation (OVR-006).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TransportFailureKind {
    ConnectionRefused,
    Dns,
    Reset,
    Timeout,
    TlsVerify,
    TlsHandshake,
    Cancelled,
}

impl TransportFailureKind {
    /// Parses a fixture's `fail` keyword (`specifications/fixtures/schema/fixture.schema.json`).
    pub fn parse(mode: &str) -> Option<Self> {
        match mode {
            "connection_refused" => Some(Self::ConnectionRefused),
            "dns" => Some(Self::Dns),
            "reset" => Some(Self::Reset),
            "timeout" => Some(Self::Timeout),
            "tls_verify" => Some(Self::TlsVerify),
            "tls_handshake" => Some(Self::TlsHandshake),
            "cancelled" => Some(Self::Cancelled),
            _ => None,
        }
    }
}

/// The transport layer's injection point (OVR-001), redesigned once to its canonical
/// shape (D-M1b-1): asynchronous, custom-verb-capable, with transport-level failure
/// raised as [`Error`].
pub trait Transport: fmt::Debug + Send + Sync {
    /// Sends `request` and returns the raw response, or an `Error` carrying
    /// `BV-TRANSPORT-001/002/003/005` for a transport-level failure. Never panics and
    /// never returns any other error type.
    fn send<'a>(
        &'a self,
        request: TransportRequest,
    ) -> Pin<Box<dyn Future<Output = Result<TransportResponse, Error>> + Send + 'a>>;

    /// TRN-010/D-M1b-14: `true` when this transport can send an arbitrary HTTP method
    /// (in particular the literal `LIST` verb). Default `true`; a fake that declares
    /// `false` proves `BV-CONFIG-009` is raised at client construction.
    fn supports_custom_verbs(&self) -> bool {
        true
    }
}

/// A single scripted step of a [`FakeTransport`] (D-M1b-15/TRN-100).
#[derive(Debug, Clone, PartialEq)]
pub enum ScriptedOutcome {
    Respond(TransportResponse),
    Fail(TransportFailureKind),
}

#[derive(Debug, Default)]
struct FakeTransportState {
    script: std::collections::VecDeque<ScriptedOutcome>,
    recorded: Vec<TransportRequest>,
}

/// The SDK's public test-support `Transport` (TRN-100, D-M1b-15). Not a second
/// implementation beside the one the fixture driver runs against — this is the same
/// object, exercised through the real `Client`/`Logical` code path. It records every
/// request (`method, url, headers, body`) and replays a scripted queue of responses or
/// transport-level failures, in order.
#[derive(Debug, Default)]
pub struct FakeTransport {
    state: Mutex<FakeTransportState>,
    supports_custom_verbs: std::sync::atomic::AtomicBool,
}

impl FakeTransport {
    pub fn new() -> Self {
        Self {
            state: Mutex::new(FakeTransportState::default()),
            supports_custom_verbs: std::sync::atomic::AtomicBool::new(true),
        }
    }

    /// D-M1b-14: a fake that declares `false` proves `BV-CONFIG-009` fires at
    /// construction.
    pub fn without_custom_verb_support(self) -> Self {
        self.supports_custom_verbs
            .store(false, std::sync::atomic::Ordering::SeqCst);
        self
    }

    pub fn script_response(&self, response: TransportResponse) -> &Self {
        self.state
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .script
            .push_back(ScriptedOutcome::Respond(response));
        self
    }

    pub fn script_failure(&self, kind: TransportFailureKind) -> &Self {
        self.state
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .script
            .push_back(ScriptedOutcome::Fail(kind));
        self
    }

    /// The requests recorded so far, in order.
    pub fn recorded_requests(&self) -> Vec<TransportRequest> {
        self.state
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .recorded
            .clone()
    }
}

impl Transport for FakeTransport {
    fn send<'a>(
        &'a self,
        request: TransportRequest,
    ) -> Pin<Box<dyn Future<Output = Result<TransportResponse, Error>> + Send + 'a>> {
        Box::pin(async move {
            let outcome = {
                let mut state = self.state.lock().unwrap_or_else(|poison| poison.into_inner());
                state.recorded.push(request.clone());
                state.script.pop_front()
            };
            match outcome {
                Some(ScriptedOutcome::Respond(response)) => {
                    if response.body.len() as u64 > request.max_response_bytes {
                        return Err(crate::error::catalog_errors::transport_response_too_large());
                    }
                    Ok(response)
                }
                Some(ScriptedOutcome::Fail(kind)) => Err(transport_failure_to_error(kind)),
                None => Err(crate::error::catalog_errors::transport_connection_failed()
                    .with_detail("reason", "fake transport script exhausted")),
            }
        })
    }

    fn supports_custom_verbs(&self) -> bool {
        self.supports_custom_verbs.load(std::sync::atomic::Ordering::SeqCst)
    }
}

/// Per-operation overrides (CFG-060/061), extended at M1b with `ApiVersion` and
/// `TotalTimeout` (D-M1b-13). Always optional in every operation's signature and never
/// mutates the `Client` it is passed alongside.
#[derive(Debug, Clone, Default)]
pub struct RequestOptions {
    pub namespace: Option<String>,
    pub headers: Option<HashMap<String, String>>,
    pub timeout: Option<Duration>,
    pub idempotent: Option<bool>,
    pub wrap_ttl: Option<Duration>,
    pub token: Option<SecretString>,
    /// D-M1b-13: pins the request to `v1`/`v2` regardless of `ClientConfig::api_prefix`.
    pub api_version: Option<ApiPrefix>,
    /// D-M1b-13/D-M1b-23: bounds attempts *plus* backoff together, unlike `timeout`
    /// which bounds only one attempt.
    pub total_timeout: Option<Duration>,
    /// CFG-080: an optional per-call observer, in addition to `ClientConfig`'s.
    pub observer: Option<Arc<dyn RequestObserver>>,
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    #[test]
    fn default_instance_has_no_overrides_cfg_060() {
        let options = RequestOptions::default();
        assert!(options.namespace.is_none());
        assert!(options.headers.is_none());
        assert!(options.timeout.is_none());
        assert!(options.idempotent.is_none());
        assert!(options.wrap_ttl.is_none());
        assert!(options.token.is_none());
        assert!(options.api_version.is_none());
        assert!(options.total_timeout.is_none());
    }

    #[test]
    fn per_call_options_cannot_mutate_a_client_cfg_061() {
        let config = crate::ClientConfigBuilder::new()
            .with_environment(crate::EnvironmentSource::None)
            .address("https://vault.example.test:8200")
            .namespace("team-a")
            .build()
            .expect("valid config");
        let client = crate::Client::new(config).expect("valid client");
        let namespace_before = client.config().namespace().to_owned();

        let _options = RequestOptions {
            namespace: Some("team-b".to_owned()),
            timeout: Some(Duration::from_secs(1)),
            ..RequestOptions::default()
        };

        assert_eq!(client.config().namespace(), namespace_before);
    }

    #[tokio::test]
    async fn fake_transport_records_requests_and_replays_scripted_responses() {
        let transport = FakeTransport::new();
        transport.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: b"{}".to_vec(),
        });
        let request = TransportRequest {
            method: "GET".to_owned(),
            url: "https://vault.example.test:8200/v1/secret/data/x".to_owned(),
            headers: Vec::new(),
            body: None,
            timeout: Duration::from_secs(30),
            connect_timeout: Duration::from_secs(10),
            max_response_bytes: 1024,
        };
        let response = transport.send(request.clone()).await.expect("scripted response");
        assert_eq!(response.status, 200);
        assert_eq!(transport.recorded_requests(), vec![request]);
    }

    #[tokio::test]
    async fn fake_transport_replays_scripted_failures_as_bv_transport_errors() {
        let transport = FakeTransport::new();
        transport.script_failure(TransportFailureKind::ConnectionRefused);
        let request = TransportRequest {
            method: "GET".to_owned(),
            url: "https://vault.example.test:8200/v1/secret/data/x".to_owned(),
            headers: Vec::new(),
            body: None,
            timeout: Duration::from_secs(30),
            connect_timeout: Duration::from_secs(10),
            max_response_bytes: 1024,
        };
        let error = transport.send(request).await.expect_err("scripted failure");
        assert_eq!(error.code(), "BV-TRANSPORT-001");
        assert!(error.retryable());
    }

    #[test]
    fn fake_transport_without_custom_verb_support_reports_false() {
        let transport = FakeTransport::new().without_custom_verb_support();
        assert!(!transport.supports_custom_verbs());
    }
}
