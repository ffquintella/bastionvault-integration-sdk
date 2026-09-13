//! The transport injection seam (OVR-001) and per-operation options (CFG-060/061).
//!
//! M1a defines the shape only. No operation exists yet to drive a request through
//! `Transport` (D-M1a-6), so nothing here executes a request; that is M1b.

use std::collections::HashMap;
use std::fmt;
use std::time::Duration;

use crate::secret::SecretString;

/// The transport layer's injection point: tests provide a fake that returns canned
/// responses without opening a socket (OVR-001). M1a declares the trait; no method is
/// called by anything shipped in this milestone.
pub trait Transport: fmt::Debug + Send + Sync {}

/// Per-operation overrides (CFG-060/061). Always optional in every operation's
/// signature — `RequestOptions::default()` is the "omitted" value — and never mutates
/// the `Client` it is passed alongside: nothing in this type holds a reference back to
/// a `Client` or its `ClientConfig`, so there is no path by which passing one could
/// change client state.
#[derive(Debug, Clone, Default)]
pub struct RequestOptions {
    pub namespace: Option<String>,
    pub headers: Option<HashMap<String, String>>,
    pub timeout: Option<Duration>,
    pub idempotent: Option<bool>,
    pub wrap_ttl: Option<Duration>,
    pub token: Option<SecretString>,
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
    }

    #[test]
    fn per_call_options_cannot_mutate_a_client_cfg_061() {
        let config = crate::ClientConfigBuilder::new()
            .with_environment(crate::EnvironmentSource::None)
            .address("https://vault.example.test:8200")
            .namespace("team-a")
            .build()
            .expect("valid config");
        let client = crate::Client::new(config);
        let namespace_before = client.config().namespace().to_owned();

        // Building an override-laden RequestOptions has no handle back to `client` at
        // all: there is no method to feed it into that could touch client state, and
        // no field here is a reference to the client or its config.
        let _options = RequestOptions {
            namespace: Some("team-b".to_owned()),
            timeout: Some(Duration::from_secs(1)),
            ..RequestOptions::default()
        };

        assert_eq!(client.config().namespace(), namespace_before);
    }
}
