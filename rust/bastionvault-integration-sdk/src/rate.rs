//! `RateGate` — client-side token bucket settings (D-M1a-13). M1a resolves and
//! validates the values only; the bucket itself is M8 (D-M1b-16).
//!
//! M1b ships the *pause* half of EFF-003/EFF-004 only: on a `429` (any `429`, per
//! D-M1b-22 — not only `BV-RATE-001`), the gate pauses for `min(Retry-After, 30s)` or
//! `1s` with no header. No `EFF-*` ID leaves the traceability baseline at M1b
//! (D-M1b-16): this is the behaviour the fixture needs, claimed under no `EFF-*` ID.

use std::time::{Duration, Instant};

/// `BASTIONVAULT_RATE_PER_SEC` / `BASTIONVAULT_RATE_BURST`; defaults `8`/`16`. `0`
/// disables the gate for that field.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RateGate {
    pub rate_per_second: i64,
    pub burst: i64,
}

/// The upper bound on a pause with no `Retry-After` header (D-M1b-16), and the cap
/// applied even when `Retry-After` is present and larger.
const MAX_PAUSE: Duration = Duration::from_secs(30);
const DEFAULT_PAUSE: Duration = Duration::from_secs(1);

/// The observable half of the rate gate this milestone ships (part of EFF-006):
/// whether the gate is currently paused, and until when. `Client`-shared state, one
/// instance per `Client` (and its `WithNamespace` views, which share it — D-M1b-9).
#[derive(Debug, Default)]
pub struct RateGateState {
    paused_until: Option<Instant>,
}

impl RateGateState {
    pub fn new() -> Self {
        Self { paused_until: None }
    }

    /// D-M1b-22: pauses on **any** `429`, driven by the status alone — never by the
    /// mapped code. `retry_after` is the parsed header value, if present.
    pub(crate) fn pause_for_429(&mut self, now: Instant, retry_after: Option<Duration>) {
        let requested = retry_after.unwrap_or(DEFAULT_PAUSE);
        let bounded = requested.min(MAX_PAUSE);
        self.paused_until = Some(now + bounded);
    }

    /// `RateGate.Paused` (part of EFF-006's observable state; the fixture's
    /// `clientState` assertion reads this).
    pub fn paused(&self, now: Instant) -> bool {
        self.paused_until.is_some_and(|until| until > now)
    }

    pub fn paused_until(&self) -> Option<Instant> {
        self.paused_until
    }
}

impl Default for RateGate {
    fn default() -> Self {
        Self {
            rate_per_second: 8,
            burst: 16,
        }
    }
}

impl RateGate {
    /// `true` when the gate is disabled (either field is `0`).
    pub fn is_disabled(&self) -> bool {
        self.rate_per_second == 0 || self.burst == 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_are_enabled_at_8_per_second_burst_16() {
        let gate = RateGate::default();
        assert_eq!(gate.rate_per_second, 8);
        assert_eq!(gate.burst, 16);
        assert!(!gate.is_disabled());
    }

    #[test]
    fn pause_with_no_retry_after_is_one_second_d_m1b_16() {
        let mut state = RateGateState::new();
        let now = Instant::now();
        state.pause_for_429(now, None);
        assert!(state.paused(now));
        assert!(!state.paused(now + Duration::from_millis(1001)));
    }

    #[test]
    fn pause_with_retry_after_is_capped_at_thirty_seconds_d_m1b_16() {
        let mut state = RateGateState::new();
        let now = Instant::now();
        state.pause_for_429(now, Some(Duration::from_secs(120)));
        assert!(state.paused(now + Duration::from_secs(29)));
        assert!(!state.paused(now + Duration::from_secs(31)));
    }

    #[test]
    fn pause_with_small_retry_after_uses_that_value() {
        let mut state = RateGateState::new();
        let now = Instant::now();
        state.pause_for_429(now, Some(Duration::from_secs(17)));
        assert!(state.paused(now + Duration::from_secs(16)));
        assert!(!state.paused(now + Duration::from_secs(18)));
    }

    #[test]
    fn either_field_at_zero_disables_the_gate() {
        assert!(RateGate {
            rate_per_second: 0,
            burst: 16
        }
        .is_disabled());
        assert!(RateGate {
            rate_per_second: 8,
            burst: 0
        }
        .is_disabled());
    }
}
