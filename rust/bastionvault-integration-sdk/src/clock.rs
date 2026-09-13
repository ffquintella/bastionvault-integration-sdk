//! The injected time seam (D-M1b-7 / RES-003). Every backoff and pause wait goes
//! through [`Clock::delay`], so no test in this crate sleeps in real time — a test that
//! calls a real sleep is a review finding.

use std::future::Future;
use std::pin::Pin;
use std::time::{Duration, Instant};

/// `Clock` — `now()` and an async `delay()`. The default is the system clock and
/// `tokio::time::sleep`; tests inject a fake that resolves delays instantly or on
/// command.
pub trait Clock: std::fmt::Debug + Send + Sync {
    fn now(&self) -> Instant;

    /// Waits `duration`. Boxed so the trait stays object-safe (no native `async fn` in
    /// a `dyn Clock`).
    fn delay(&self, duration: Duration) -> Pin<Box<dyn Future<Output = ()> + Send + 'static>>;
}

/// The default `Clock`: `Instant::now()` and `tokio::time::sleep`.
#[derive(Debug, Clone, Copy, Default)]
pub struct SystemClock;

impl Clock for SystemClock {
    fn now(&self) -> Instant {
        Instant::now()
    }

    fn delay(&self, duration: Duration) -> Pin<Box<dyn Future<Output = ()> + Send + 'static>> {
        Box::pin(tokio::time::sleep(duration))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn system_clock_now_advances() {
        let clock = SystemClock;
        let first = clock.now();
        let second = clock.now();
        assert!(second >= first);
    }

    #[tokio::test]
    async fn system_clock_delay_resolves() {
        let clock = SystemClock;
        clock.delay(Duration::from_millis(1)).await;
    }
}
