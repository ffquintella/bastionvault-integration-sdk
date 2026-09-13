//! `RateGate` — client-side token bucket settings (D-M1a-13). M1a resolves and
//! validates the values only; the bucket itself is M8.

/// `BASTIONVAULT_RATE_PER_SEC` / `BASTIONVAULT_RATE_BURST`; defaults `8`/`16`. `0`
/// disables the gate for that field.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RateGate {
    pub rate_per_second: i64,
    pub burst: i64,
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
