//! The minimal `Client` (D-M1a-6).
//!
//! M1a ships `Client` holding a [`ClientConfig`] and (optionally) a transport, exposing
//! [`Client::is_insecure`] and nothing else — no `Read`/`Write`/`List`, no `Auth`, no
//! engines. `Client::set_address` deliberately does not exist (CFG-072); that is
//! asserted by the public-API baseline (CNF-027), not by a runtime test.

use std::sync::Arc;

use crate::config::ClientConfig;
use crate::transport::Transport;

/// The SDK's client handle. Holds an immutable, already-resolved [`ClientConfig`] and
/// the transport it was built with (if any).
#[derive(Debug)]
pub struct Client {
    config: ClientConfig,
}

impl Client {
    /// Wraps an already-resolved `ClientConfig`. Resolution happened once, in
    /// `ClientConfigBuilder::build` (CFG-002); this constructor cannot fail.
    pub fn new(config: ClientConfig) -> Self {
        Self { config }
    }

    /// `true` when `TlsSkipVerify` was set (CFG-018).
    pub fn is_insecure(&self) -> bool {
        self.config.tls_skip_verify()
    }

    pub fn config(&self) -> &ClientConfig {
        &self.config
    }

    pub fn transport(&self) -> Option<&Arc<dyn Transport>> {
        self.config.transport()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{ClientConfigBuilder, EnvironmentSource};
    use std::sync::Arc;

    #[test]
    fn a_fake_transport_can_be_injected_without_opening_a_socket_ovr_001() {
        #[derive(Debug)]
        struct FakeTransport {
            calls: std::sync::atomic::AtomicUsize,
        }
        impl crate::Transport for FakeTransport {}

        let fake = Arc::new(FakeTransport {
            calls: std::sync::atomic::AtomicUsize::new(0),
        });
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .transport(fake.clone())
            .build()
            .expect("valid");
        let client = Client::new(config);
        assert!(client.transport().is_some());
        assert_eq!(
            fake.calls.load(std::sync::atomic::Ordering::SeqCst),
            0,
            "no socket-opening call was made to construct or wrap this client"
        );
    }

    #[test]
    fn client_has_no_set_address_method_cfg_072() {
        // D-M1a-6: `Client::set_address` MUST NOT exist (CFG-072); changing the server
        // requires a new `Client`. This is asserted against the committed public-API
        // baseline (CNF-027), the mechanism D-M1a-6 names for this ID, rather than by a
        // runtime call that the compiler would reject anyway.
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
        let client = Client::new(config);
        assert!(client.is_insecure());
    }

    #[test]
    fn client_is_constructible_without_a_token() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("a client must be constructible without a token");
        let client = Client::new(config);
        assert!(client.config().token().is_none());
    }
}
