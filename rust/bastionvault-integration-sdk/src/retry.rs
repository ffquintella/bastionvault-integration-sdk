//! `RetryPolicy` (CFG-050/CFG-051..055) and the retry-eligibility/backoff maths
//! (D-M1b-7/RES-003). M1a resolved and materialised the policy; M1b adds the missing
//! `RetryOn` field and the loop that reads it (`src/logical.rs`).

use std::time::Duration;

/// Retry policy defaults and shape (CFG-050). `RetryOn` was missing in every M1a
/// language (D-M1b-7's defect) and is added here with its CFG-050 default.
#[derive(Debug, Clone, PartialEq)]
pub struct RetryPolicy {
    pub max_attempts: u32,
    pub initial_backoff: Duration,
    pub max_backoff: Duration,
    pub backoff_multiplier: f64,
    pub jitter: f64,
    pub respect_retry_after: bool,
    pub retry_idempotent_only: bool,
    /// D-M1b-7: the codes eligible for automatic retry. A strictly smaller set than
    /// `Error::retryable()` (ERR-006) — see D-M1b-4b. Defaults to
    /// `[BV-TRANSPORT-001, BV-TRANSPORT-002, BV-SERVER-002, BV-SERVER-003]`.
    pub retry_on: Vec<&'static str>,
}

/// CFG-052/053: hard exclusions. Never retried even if a caller puts them in
/// `RetryOn` — these are prohibitions on the SDK, not preferences (D-M1b-7).
pub(crate) const HARD_EXCLUDED_CODES: [&str; 2] = ["BV-SERVER-001", "BV-RATE-001"];

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
            retry_on: vec![
                "BV-TRANSPORT-001",
                "BV-TRANSPORT-002",
                "BV-SERVER-002",
                "BV-SERVER-003",
            ],
        }
    }
}

impl RetryPolicy {
    /// D-M1b-7: eligible iff the mapped code is in `RetryOn` **and** is not one of the
    /// CFG-052/053 hard exclusions, regardless of what the caller put in `RetryOn`.
    pub(crate) fn code_is_retry_eligible(&self, code: &str) -> bool {
        if HARD_EXCLUDED_CODES.contains(&code) {
            return false;
        }
        self.retry_on.contains(&code)
    }

    /// RES-003: `min(MaxBackoff, InitialBackoff * Multiplier^(attempt-1))` with
    /// `+/- Jitter` uniform randomisation. `attempt` is 1-based (the attempt that just
    /// failed). `unit_random` is `JitterSource::next_f64()`'s `[0.0, 1.0)` output.
    pub(crate) fn backoff_for_attempt(&self, attempt: u32, unit_random: f64) -> Duration {
        let exponent = attempt.saturating_sub(1);
        let scaled = self.initial_backoff.as_secs_f64() * self.backoff_multiplier.powi(exponent as i32);
        let base = scaled.min(self.max_backoff.as_secs_f64()).max(0.0);
        // `unit_random` in [0, 1) maps to a jitter factor in [1 - jitter, 1 + jitter).
        let jitter_factor = 1.0 - self.jitter + (2.0 * self.jitter * unit_random);
        let jittered = (base * jitter_factor).max(0.0);
        Duration::from_secs_f64(jittered)
    }

    /// D-M1b-7/CFG-054: with `Retry-After` present and `RespectRetryAfter` true, the
    /// wait is `min(max(RetryAfter, backoff), MaxBackoff * 6)`.
    pub(crate) fn wait_with_retry_after(&self, backoff: Duration, retry_after: Option<Duration>) -> Duration {
        match retry_after {
            Some(retry_after) if self.respect_retry_after => {
                let floor = backoff.max(retry_after);
                floor.min(self.max_backoff.saturating_mul(6))
            }
            _ => backoff,
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
        assert_eq!(
            policy.retry_on,
            vec!["BV-TRANSPORT-001", "BV-TRANSPORT-002", "BV-SERVER-002", "BV-SERVER-003"]
        );
    }

    #[test]
    fn hard_exclusions_are_never_eligible_even_if_listed_in_retry_on_cfg_052_cfg_053() {
        let policy = RetryPolicy {
            retry_on: vec!["BV-SERVER-001", "BV-RATE-001", "BV-TRANSPORT-001"],
            ..RetryPolicy::default()
        };
        assert!(!policy.code_is_retry_eligible("BV-SERVER-001"));
        assert!(!policy.code_is_retry_eligible("BV-RATE-001"));
        assert!(policy.code_is_retry_eligible("BV-TRANSPORT-001"));
    }

    #[test]
    fn codes_outside_retry_on_are_not_eligible() {
        let policy = RetryPolicy::default();
        assert!(!policy.code_is_retry_eligible("BV-RATE-002"));
        assert!(!policy.code_is_retry_eligible("BV-TRANSPORT-003"));
    }

    #[test]
    fn backoff_grows_exponentially_and_is_capped_at_max_backoff() {
        let policy = RetryPolicy {
            initial_backoff: Duration::from_millis(100),
            max_backoff: Duration::from_secs(1),
            backoff_multiplier: 2.0,
            jitter: 0.0,
            ..RetryPolicy::default()
        };
        assert_eq!(policy.backoff_for_attempt(1, 0.5), Duration::from_millis(100));
        assert_eq!(policy.backoff_for_attempt(2, 0.5), Duration::from_millis(200));
        assert_eq!(policy.backoff_for_attempt(3, 0.5), Duration::from_millis(400));
        // 100ms * 2^4 = 1600ms, capped at max_backoff = 1s.
        assert_eq!(policy.backoff_for_attempt(5, 0.5), Duration::from_secs(1));
    }

    #[test]
    fn jitter_randomises_within_the_declared_band() {
        let policy = RetryPolicy {
            initial_backoff: Duration::from_millis(1000),
            max_backoff: Duration::from_secs(10),
            backoff_multiplier: 1.0,
            jitter: 0.2,
            ..RetryPolicy::default()
        };
        let low = policy.backoff_for_attempt(1, 0.0);
        let mid = policy.backoff_for_attempt(1, 0.5);
        let high = policy.backoff_for_attempt(1, 0.999_999);
        assert_eq!(low, Duration::from_millis(800));
        assert_eq!(mid, Duration::from_millis(1000));
        assert!(high.as_millis() >= 1199);
    }

    #[test]
    fn retry_after_floors_the_wait_but_is_capped_at_six_times_max_backoff_cfg_054() {
        let policy = RetryPolicy {
            max_backoff: Duration::from_secs(5),
            respect_retry_after: true,
            ..RetryPolicy::default()
        };
        let backoff = Duration::from_millis(250);
        assert_eq!(
            policy.wait_with_retry_after(backoff, Some(Duration::from_secs(2))),
            Duration::from_secs(2)
        );
        assert_eq!(
            policy.wait_with_retry_after(backoff, Some(Duration::from_secs(60))),
            Duration::from_secs(30)
        );
        assert_eq!(policy.wait_with_retry_after(backoff, None), backoff);
    }

    #[test]
    fn retry_after_is_ignored_when_respect_retry_after_is_false() {
        let policy = RetryPolicy {
            respect_retry_after: false,
            ..RetryPolicy::default()
        };
        let backoff = Duration::from_millis(250);
        assert_eq!(
            policy.wait_with_retry_after(backoff, Some(Duration::from_secs(2))),
            backoff
        );
    }
}
