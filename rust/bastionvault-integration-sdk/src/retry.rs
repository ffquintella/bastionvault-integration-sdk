//! `RetryPolicy` (CFG-050). M1a resolves and materialises the policy; retry
//! *execution* is M1b (D-M1a-10).

use std::time::Duration;

/// Retry policy defaults and shape (CFG-050). Only `max_attempts` is affected by
/// environment resolution in M1a (`BASTIONVAULT_MAX_RETRIES`/`VAULT_MAX_RETRIES` set
/// `max_attempts = value + 1`, D-M1a-4).
#[derive(Debug, Clone, PartialEq)]
pub struct RetryPolicy {
    pub max_attempts: u32,
    pub initial_backoff: Duration,
    pub max_backoff: Duration,
    pub backoff_multiplier: f64,
    pub jitter: f64,
    pub respect_retry_after: bool,
    pub retry_idempotent_only: bool,
}

impl Default for RetryPolicy {
    fn default() -> Self {
        Self {
            max_attempts: 3,
            initial_backoff: Duration::from_millis(250),
            max_backoff: Duration::from_secs(5),
            backoff_multiplier: 2.0,
            jitter: 0.2,
            respect_retry_after: true,
            retry_idempotent_only: true,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_match_cfg_050() {
        let policy = RetryPolicy::default();
        assert_eq!(policy.max_attempts, 3);
        assert_eq!(policy.initial_backoff, Duration::from_millis(250));
        assert_eq!(policy.max_backoff, Duration::from_secs(5));
        assert_eq!(policy.backoff_multiplier, 2.0);
        assert_eq!(policy.jitter, 0.2);
        assert!(policy.respect_retry_after);
        assert!(policy.retry_idempotent_only);
    }
}
