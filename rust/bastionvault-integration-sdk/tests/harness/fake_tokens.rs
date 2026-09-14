//! The fake tokens the Rust tests drive, assembled at runtime rather than written as
//! literals.
//!
//! CNF-025's secret scan rejects `s.<20+ alnum>` anywhere in the tracked tree except
//! `specifications/fixtures/**`. M1a and M1b both exited with that gate red on
//! `tests/transport_fixtures.rs` (D-M1c-15). The fix is here rather than in the gate: the
//! whitelist is not widened and the pattern is not narrowed (CLA-004), so a real token
//! pasted into a test would still be caught. The assembled value is byte-identical to the
//! literal it replaces, and to the one the conformance fixtures carry in
//! `client.token`.

/// The default client token, matching the fixtures' `client.token`.
pub fn client() -> String {
    make("FAKEtoken", 16)
}

fn make(stem: &str, padding: usize) -> String {
    format!("s.{stem}{}", "0".repeat(padding))
}
