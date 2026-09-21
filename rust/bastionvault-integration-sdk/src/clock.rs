//! The injected time seam (D-M1b-7 / RES-003, reshaped by D-M2-2). Every backoff and
//! pause wait goes through [`Clock::delay`], so no test in this crate sleeps in real
//! time — a test that calls a real sleep is a review finding.
//!
//! **D-M2-2: the clock states which time it means.** Until M2a this trait had one
//! `now()` returning a monotonic [`Instant`], while .NET's and Python's `now()` returned
//! wall-clock time — one member name meaning two different things depending on the
//! language, which is the R-9 defect class exactly. The concept is now in the member
//! name in all three SDKs:
//!
//! - [`Clock::now_monotonic`] is the monotonic reading backoff, attempt durations and the
//!   rate-gate pause use. It is what `now()` was, renamed.
//! - [`Clock::now_utc`] is wall-clock time, new on Rust, and is what `AUT-014`'s
//!   `remaining_ttl = creation_time + creation_ttl − now` arithmetic needs: a monotonic
//!   instant has no epoch, so on Rust that requirement was not merely untested, it was
//!   uncomputable.
//!
//! **No new dependency.** `SystemTime::duration_since(UNIX_EPOCH)` yields the unix
//! seconds `AUT-014` needs; `chrono` and `time` are both refused, because the
//! dependency-divergence follow-up carried out of M1a is a watched item at `CNF-024` and
//! neither buys anything here.

use std::future::Future;
use std::pin::Pin;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

/// `Clock` — the two readings D-M2-2 distinguishes, plus an async `delay()`. The default
/// is the system clock and `tokio::time::sleep`; tests inject a fake that resolves delays
/// instantly or on command.
pub trait Clock: std::fmt::Debug + Send + Sync {
    /// Monotonic time, for measuring elapsed durations and computing deadlines. Never
    /// projected onto a calendar — it has no epoch (D-M2-2).
    fn now_monotonic(&self) -> Instant;

    /// Wall-clock time (D-M2-2), for the unix-epoch arithmetic `AUT-014` specifies.
    fn now_utc(&self) -> SystemTime;

    /// Waits `duration`. Boxed so the trait stays object-safe (no native `async fn` in
    /// a `dyn Clock`).
    fn delay(&self, duration: Duration) -> Pin<Box<dyn Future<Output = ()> + Send + 'static>>;
}

/// The [`SystemTime`] for a whole-seconds unix timestamp, saturating at the epoch for a
/// negative one. A lookup's `creation_time` is a server-supplied unix timestamp
/// (`05-authentication.md` §Token store operations), so this is its inverse.
pub(crate) fn from_unix_seconds(seconds: i64) -> SystemTime {
    if seconds >= 0 {
        UNIX_EPOCH + Duration::from_secs(seconds as u64)
    } else {
        UNIX_EPOCH - Duration::from_secs(seconds.unsigned_abs())
    }
}

/// The default `Clock`: `Instant::now()`, `SystemTime::now()` and `tokio::time::sleep`.
#[derive(Debug, Clone, Copy, Default)]
pub struct SystemClock;

impl Clock for SystemClock {
    fn now_monotonic(&self) -> Instant {
        Instant::now()
    }

    fn now_utc(&self) -> SystemTime {
        SystemTime::now()
    }

    fn delay(&self, duration: Duration) -> Pin<Box<dyn Future<Output = ()> + Send + 'static>> {
        Box::pin(tokio::time::sleep(duration))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn system_clock_now_monotonic_advances_d_m2_2() {
        let clock = SystemClock;
        let first = clock.now_monotonic();
        let second = clock.now_monotonic();
        assert!(second >= first);
    }

    /// D-M2-2: the new reading is wall-clock, and it is projectable onto the unix epoch —
    /// which is the whole point, because `AUT-014` is epoch arithmetic and an `Instant`
    /// cannot do it.
    #[test]
    fn system_clock_now_utc_is_projectable_onto_the_unix_epoch_d_m2_2_aut_014() {
        let clock = SystemClock;
        let seconds = clock
            .now_utc()
            .duration_since(UNIX_EPOCH)
            .expect("the system clock must be after 1970")
            .as_secs();
        // 2026-01-01T00:00:00Z. A reading below this is a wrong epoch, not a slow machine —
        // and an `Instant` cannot produce one at all, which is D-M2-2's whole point.
        assert!(seconds > 1_767_225_600, "now_utc must read wall-clock time, got {seconds}");
    }

    #[test]
    fn from_unix_seconds_is_the_inverse_of_the_epoch_projection_aut_014() {
        assert_eq!(
            from_unix_seconds(1_789_300_800)
                .duration_since(UNIX_EPOCH)
                .expect("after the epoch")
                .as_secs(),
            1_789_300_800
        );
        assert_eq!(from_unix_seconds(0), UNIX_EPOCH);
        // Before the epoch: the inverse saturates backwards rather than panicking on the
        // subtraction, and has no unix reading.
        assert!(from_unix_seconds(-1).duration_since(UNIX_EPOCH).is_err());
    }

    #[tokio::test]
    async fn system_clock_delay_resolves() {
        let clock = SystemClock;
        clock.delay(Duration::from_millis(1)).await;
    }
}
