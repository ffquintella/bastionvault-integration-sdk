//! The `Client` (D-M1a-6, redesigned at D-M1b-9/14, reshaped at D-M2-9): a shared token
//! **source**, namespace views that share it (CFG-070/071), and construction that asserts
//! the transport can send custom verbs (TRN-010/D-M1b-14).
//!
//! **What M2a changed.** The token cell used to be an `Option<SecretString>` behind a
//! mutex, and `CFG-070`'s thread-safety rested entirely on the read being a plain clone
//! of it. D-M2-9 made resolution asynchronous and side-effecting, which invalidated that
//! justification and is why D-M2-11(c) re-opened `CFG-070` onto the traceability baseline
//! and gave it to M2a. It now rests on two facts instead: each pass resolves exactly once,
//! above the retry loop (D-M1b-9 — the snapshot did **not** move, only its source
//! changed), and a concurrent first-use resolution of a `Login` source is single-flighted
//! inside [`crate::TokenSource`] (D-M2-11(a)).

use std::sync::{Arc, Mutex};
use std::time::Duration;

use crate::auth::{Auth, TokenInfo};
use crate::config::ClientConfig;
use crate::error::catalog_errors::config_list_verb_unsupported;
use crate::error::Error;
use crate::logical::Logical;
use crate::rate::RateGateState;
use crate::secret::SecretString;
use crate::token_source::TokenSource;
use crate::transport::Transport;

/// The token state a `Client` shares with every [`Client::with_namespace`] view
/// (`CFG-071`): `AUT-001`'s one source, the last value it resolved to (`AUT-004`), and the
/// last `LookupSelf` result (`AUT-004`).
#[derive(Debug)]
pub(crate) struct TokenState {
    source: Mutex<Arc<TokenSource>>,
    /// The most recent value [`Client::resolve_token`] produced, or the configured token
    /// before the first resolution — which is what `AUT-004`'s `Auth.CurrentToken` reads.
    ///
    /// Separate from `source` because **reading the current token must never perform a
    /// resolution**: a `Callback` source would otherwise call into the application every
    /// time a property was inspected.
    last_resolved: Mutex<SecretString>,
    token_info: Mutex<Option<TokenInfo>>,
}

impl TokenState {
    fn new(source: Arc<TokenSource>, last_resolved: SecretString) -> Self {
        Self {
            source: Mutex::new(source),
            last_resolved: Mutex::new(last_resolved),
            token_info: Mutex::new(None),
        }
    }
}

/// Recovers a poisoned mutex rather than propagating the panic: every value guarded here
/// is replaced wholesale, never mutated in place, so a panic elsewhere cannot have left
/// one half-written.
fn lock<T>(cell: &Mutex<T>) -> std::sync::MutexGuard<'_, T> {
    cell.lock().unwrap_or_else(|poison| poison.into_inner())
}

/// The SDK's client handle. Holds an immutable, already-resolved [`ClientConfig`], a
/// shared token cell (D-M1b-9), a shared rate-gate state (D-M1b-16), and the transport
/// it was built with.
#[derive(Debug)]
pub struct Client {
    config: Arc<ClientConfig>,
    namespace_override: Option<String>,
    tokens: Arc<TokenState>,
    transport: Arc<dyn Transport>,
    rate_state: Arc<Mutex<RateGateState>>,
}

impl Client {
    /// Wraps an already-resolved `ClientConfig`. Resolution happened once, in
    /// `ClientConfigBuilder::build` (CFG-002).
    ///
    /// D-M1b-14: fails with `BV-CONFIG-009` when the transport cannot send custom HTTP
    /// verbs (the SDK requires the literal `LIST` verb, TRN-010). Every production
    /// transport this SDK ships declares `true`; a fake transport can prove the
    /// failure path by declaring `false`.
    pub fn new(config: ClientConfig) -> Result<Self, Error> {
        // AUT-001: exactly one source. An application-supplied one is it; otherwise the
        // resolved token becomes a `Static` source, which is byte-for-byte the pre-M2a
        // behaviour. A `Static` source's token is already known, so `Auth::current_token`
        // can report it without resolving; any other source has resolved nothing yet, and
        // reading the current token must not be what triggers the first resolution
        // (AUT-004).
        let configured = config.token().cloned().unwrap_or_else(|| SecretString::new(""));
        let (source, last_resolved) = match config.token_source() {
            Some(explicit) => (Arc::clone(explicit), SecretString::new("")),
            None => (Arc::new(TokenSource::r#static(configured.clone())), configured),
        };
        let transport: Arc<dyn Transport> = match config.transport() {
            Some(transport) => Arc::clone(transport),
            None => Arc::new(crate::transport_http::HttpTransport::new(&config)),
        };
        if !transport.supports_custom_verbs() {
            return Err(config_list_verb_unsupported());
        }
        Ok(Self {
            config: Arc::new(config),
            namespace_override: None,
            tokens: Arc::new(TokenState::new(source, last_resolved)),
            transport,
            rate_state: Arc::new(Mutex::new(RateGateState::new())),
        })
    }

    /// `true` when `TlsSkipVerify` was set (CFG-018).
    pub fn is_insecure(&self) -> bool {
        self.config.tls_skip_verify()
    }

    pub fn config(&self) -> &ClientConfig {
        &self.config
    }

    pub fn transport(&self) -> &Arc<dyn Transport> {
        &self.transport
    }

    /// `client.logical()` (D-M1b public API table): the four logical primitives plus
    /// `Raw`.
    pub fn logical(&self) -> Logical<'_> {
        Logical { client: self }
    }

    /// `client.auth()` (`OVR-008`, D-M2-6): this project's first sub-API grouping, and the
    /// shape every later engine grouping copies.
    pub fn auth(&self) -> Auth<'_> {
        Auth { client: self }
    }

    /// `CFG-070`: replaces the client's token, and with it its source — `AUT-001` says a
    /// token write makes the source `Static`. Thread-safe, and in-flight requests are
    /// unaffected because each pass already resolved its own snapshot (D-M1b-9).
    pub fn set_token(&self, token: SecretString) {
        *lock(&self.tokens.source) = Arc::new(TokenSource::r#static(token.clone()));
        *lock(&self.tokens.last_resolved) = token;
    }

    /// `CFG-070`: clears the token. The source stays `Static`, now holding nothing, rather
    /// than reverting to whatever source the client was built with — `AUT-001` allows
    /// exactly one source and `set_token` already replaced it.
    pub fn clear_token(&self) {
        self.set_token(SecretString::new(""));
    }

    /// `AUT-001`/`AUT-004`: the one source this client holds.
    pub(crate) fn token_source(&self) -> Arc<TokenSource> {
        Arc::clone(&lock(&self.tokens.source))
    }

    /// `AUT-004`'s `Auth.CurrentToken`: the token the client currently holds, or `None`
    /// when it holds none. **Never resolves**, so reading it can neither log in nor call an
    /// application callback.
    pub(crate) fn current_token(&self) -> Option<SecretString> {
        let token = lock(&self.tokens.last_resolved).clone();
        (!token.is_empty()).then_some(token)
    }

    /// D-M2-9's resolution call: what replaced the field read. Called once per pass, above
    /// the retry loop, and the result is threaded through every attempt of that pass
    /// (D-M1b-9, `CFG-070`).
    pub(crate) async fn resolve_token(&self) -> Result<Option<SecretString>, Error> {
        let source = self.token_source();
        let resolved = source.resolve().await?;
        *lock(&self.tokens.last_resolved) = resolved.clone().unwrap_or_else(|| SecretString::new(""));
        Ok(resolved)
    }

    /// `AUT-004`'s `Auth.TokenInfo`: the most recent `LookupSelf` result, if any.
    pub(crate) fn token_info(&self) -> Option<TokenInfo> {
        lock(&self.tokens.token_info).clone()
    }

    pub(crate) fn set_token_info(&self, info: TokenInfo) {
        *lock(&self.tokens.token_info) = Some(info);
    }

    /// CFG-071: a lightweight view sharing the transport, the config and the same
    /// token cell, differing only in namespace — so `SetToken` on the parent is
    /// visible to the view.
    pub fn with_namespace(&self, namespace: &str) -> Self {
        Self {
            config: Arc::clone(&self.config),
            namespace_override: Some(namespace.trim_end_matches('/').to_owned()),
            tokens: Arc::clone(&self.tokens),
            transport: Arc::clone(&self.transport),
            rate_state: Arc::clone(&self.rate_state),
        }
    }

    pub(crate) fn effective_namespace(&self) -> &str {
        self.namespace_override
            .as_deref()
            .unwrap_or_else(|| self.config.namespace())
    }

    /// D-M1b-22: pauses the shared rate-gate state on any `429`, driven by the status
    /// alone.
    pub(crate) fn pause_rate_gate(&self, retry_after: Option<Duration>) {
        let now = self.config.clock().now_monotonic();
        self.rate_state
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .pause_for_429(now, retry_after);
    }

    /// `RateGate.Paused` (EFF-006's observable half; D-M1b-16).
    pub fn rate_gate_paused(&self) -> bool {
        let now = self.config.clock().now_monotonic();
        self.rate_state
            .lock()
            .unwrap_or_else(|poison| poison.into_inner())
            .paused(now)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::transport::{FakeTransport, TransportFailureKind, TransportRequest, TransportResponse};
    use crate::{ClientConfigBuilder, EnvironmentSource};

    #[test]
    fn a_fake_transport_can_be_injected_without_opening_a_socket_ovr_001() {
        let fake = Arc::new(FakeTransport::new());
        fake.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: b"{}".to_vec(),
        });
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .transport(fake.clone())
            .build()
            .expect("valid");
        let client = Client::new(config).expect("valid client");
        assert!(client.transport().supports_custom_verbs());
        assert!(fake.recorded_requests().is_empty());
    }

    #[test]
    fn client_has_no_set_address_method_cfg_072() {
        let baseline = std::fs::read_to_string(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/public-api-baseline.txt"
        ))
        .expect("public API baseline must be readable");
        assert!(
            !baseline.contains("set_address"),
            "the public-API baseline must never list a Client::set_address method"
        );
    }

    #[test]
    fn is_insecure_reflects_tls_skip_verify_cfg_018() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .tls_skip_verify(true)
            .build()
            .expect("valid");
        let client = Client::new(config).expect("valid client");
        assert!(client.is_insecure());
    }

    #[test]
    fn client_is_constructible_without_a_token() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        let client = Client::new(config).expect("valid client");
        assert!(client.current_token().is_none());
    }

    #[test]
    fn construction_fails_with_config_009_when_the_transport_cannot_send_custom_verbs_trn_010_d_m1b_14()
    {
        let fake = Arc::new(FakeTransport::new().without_custom_verb_support());
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .transport(fake)
            .build()
            .expect("valid");
        let error = Client::new(config).expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-009");
    }

    #[test]
    fn set_token_and_clear_token_affect_the_shared_cell_cfg_070() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        let client = Client::new(config).expect("valid client");
        assert!(client.current_token().is_none());
        client.set_token(SecretString::new("s.new-token"));
        assert_eq!(client.current_token().map(|t| t.reveal().to_owned()), Some("s.new-token".to_owned()));
        client.clear_token();
        assert!(client.current_token().is_none());
    }

    #[test]
    fn with_namespace_shares_the_token_cell_with_its_parent_cfg_071() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .namespace("team-a")
            .build()
            .expect("valid");
        let client = Client::new(config).expect("valid client");
        let view = client.with_namespace("team-b");
        assert_eq!(view.effective_namespace(), "team-b");
        client.set_token(SecretString::new("s.shared"));
        assert_eq!(
            view.current_token().map(|t| t.reveal().to_owned()),
            Some("s.shared".to_owned())
        );
    }

    #[tokio::test]
    async fn a_429_pauses_the_shared_rate_gate_state_d_m1b_16() {
        let fake = Arc::new(FakeTransport::new());
        fake.script_response(TransportResponse {
            status: 429,
            headers: Vec::new(),
            body: br#"{"error":"namespace request-rate quota exceeded: 5 req/s"}"#.to_vec(),
        });
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .transport(fake)
            .retry_policy(crate::RetryPolicy {
                max_attempts: 1,
                ..crate::RetryPolicy::default()
            })
            .build()
            .expect("valid");
        let client = Client::new(config).expect("valid client");
        assert!(!client.rate_gate_paused());
        let error = client
            .logical()
            .read("secret/data/x", None)
            .await
            .expect_err("429 must be an error");
        assert_eq!(error.code(), "BV-RATE-002");
        assert!(client.rate_gate_paused());
    }

    #[tokio::test]
    async fn transport_failure_kind_is_reachable_through_the_fake_transport_used_by_a_real_client() {
        let fake = Arc::new(FakeTransport::new());
        fake.script_failure(TransportFailureKind::ConnectionRefused);
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .transport(fake)
            .retry_policy(crate::RetryPolicy {
                max_attempts: 1,
                ..crate::RetryPolicy::default()
            })
            .build()
            .expect("valid");
        let client = Client::new(config).expect("valid client");
        let error = client
            .logical()
            .read("secret/data/x", None)
            .await
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-TRANSPORT-001");
        let _ = TransportRequest {
            method: "GET".to_owned(),
            url: String::new(),
            headers: Vec::new(),
            body: None,
            timeout: Duration::from_secs(1),
            connect_timeout: Duration::from_secs(1),
            max_response_bytes: 1,
        };
    }
}
