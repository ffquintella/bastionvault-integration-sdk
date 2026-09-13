//! The injected randomness seam (D-M1b-7 / RES-003): `JitterSource::next_f64()` ->
//! `[0.0, 1.0)`. The default is a small, dependency-free xorshift PRNG seeded from the
//! system clock — no new crate is added for this, since RES-003 only requires the
//! source be *injectable*, not any particular algorithm.

use std::sync::Mutex;
use std::time::{SystemTime, UNIX_EPOCH};

/// A source of uniform randomness in `[0.0, 1.0)` (RES-003). Tests inject a fixed or
/// scripted source so backoff jitter is deterministic.
pub trait JitterSource: std::fmt::Debug + Send + Sync {
    fn next_f64(&self) -> f64;
}

/// The default `JitterSource`: a xorshift64* generator seeded from the system clock.
#[derive(Debug)]
pub struct DefaultJitterSource {
    state: Mutex<u64>,
}

impl DefaultJitterSource {
    pub fn new() -> Self {
        let seed = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|duration| duration.as_nanos() as u64)
            .unwrap_or(0x9E3779B97F4A7C15)
            | 1;
        Self {
            state: Mutex::new(seed),
        }
    }
}

impl Default for DefaultJitterSource {
    fn default() -> Self {
        Self::new()
    }
}

impl JitterSource for DefaultJitterSource {
    fn next_f64(&self) -> f64 {
        let mut state = self.state.lock().unwrap_or_else(|poison| poison.into_inner());
        let mut x = *state;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        *state = x;
        // Top 53 bits give a value in [0, 1) with full `f64` mantissa precision.
        ((x >> 11) as f64) * (1.0 / (1u64 << 53) as f64)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_source_produces_values_in_unit_range() {
        let source = DefaultJitterSource::new();
        for _ in 0..1000 {
            let value = source.next_f64();
            assert!((0.0..1.0).contains(&value), "{value} out of range");
        }
    }

    #[test]
    fn default_source_is_not_constant() {
        let source = DefaultJitterSource::new();
        let values: Vec<f64> = (0..10).map(|_| source.next_f64()).collect();
        assert!(values.windows(2).any(|pair| pair[0] != pair[1]));
    }
}
