//! The observability hook (D-M1b-8 / CFG-080/081 / RES-002).
//!
//! `RequestObserver::on_request_completed` fires once per attempt, carrying no body, no
//! token and no headers (CFG-080). `RequestId` lives only on the event, never on
//! [`crate::logical::Response`] (TRN-042 forbids fabricating one there).

use std::time::Duration;

/// One attempt's outcome (RES-002). `request_id` is SDK-generated, opaque, and stable
/// across every attempt of one logical operation (D-M1b-8).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RequestEvent {
    pub method: String,
    pub path: String,
    pub namespace: String,
    pub status_code: Option<u16>,
    pub duration: Duration,
    pub request_id: String,
    pub attempt: u32,
    pub error_code: Option<&'static str>,
}

/// The observability seam (CFG-080). Settable on [`crate::RequestOptions`] /
/// `ClientConfigBuilder`; the default is a no-op.
pub trait RequestObserver: std::fmt::Debug + Send + Sync {
    fn on_request_completed(&self, event: &RequestEvent);
}

/// The default `RequestObserver`: discards every event.
#[derive(Debug, Clone, Copy, Default)]
pub struct NoopRequestObserver;

impl RequestObserver for NoopRequestObserver {
    fn on_request_completed(&self, _event: &RequestEvent) {}
}

/// Documented metric names (CFG-081). Not wired to any particular metrics backend at
/// M1b; named here so a `RequestObserver` implementation can emit them under these
/// exact names.
pub mod metric_names {
    pub const REQUEST_DURATION: &str = "bastionvault.client.request.duration";
    pub const REQUEST_RETRIES: &str = "bastionvault.client.request.retries";
    pub const REQUEST_ERRORS: &str = "bastionvault.client.request.errors";
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn noop_observer_accepts_any_event_without_panicking() {
        let observer = NoopRequestObserver;
        observer.on_request_completed(&RequestEvent {
            method: "GET".to_owned(),
            path: "secret/data/x".to_owned(),
            namespace: String::new(),
            status_code: Some(200),
            duration: Duration::from_millis(5),
            request_id: "req-1".to_owned(),
            attempt: 1,
            error_code: None,
        });
    }

    #[test]
    fn metric_names_match_cfg_081() {
        assert_eq!(
            metric_names::REQUEST_DURATION,
            "bastionvault.client.request.duration"
        );
        assert_eq!(
            metric_names::REQUEST_RETRIES,
            "bastionvault.client.request.retries"
        );
        assert_eq!(
            metric_names::REQUEST_ERRORS,
            "bastionvault.client.request.errors"
        );
    }
}
