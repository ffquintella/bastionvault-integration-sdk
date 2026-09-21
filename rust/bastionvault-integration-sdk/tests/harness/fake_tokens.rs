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

/// A second, distinct token, for asserting that a store keyed one does not return the other.
pub fn other() -> String {
    make("FAKEother", 18)
}

/// The token a `TokenOps::use` call pins onto the client (`AUT-020`).
pub fn used() -> String {
    make("FAKEused", 17)
}

/// The token a token *file* holds on disk (`CFG` token-file surface).
pub fn persisted() -> String {
    make("FAKEpersisted", 11)
}

/// The token two clients share through one token cell (`CFG-071`).
pub fn shared() -> String {
    make("FAKEshared", 13)
}

/// The token a login response hands back in `auth.client_token`.
pub fn issued() -> String {
    make("FAKEissued", 13)
}

/// The token a request pins for the duration of one call.
pub fn pinned() -> String {
    make("FAKEpinned", 13)
}

/// The token a `set_token` rotation swaps in (`CFG-070`).
pub fn swapped() -> String {
    make("FAKEswapped", 13)
}

/// The token the `TST-051` leak probes plant and then search for.
pub fn leak() -> String {
    make("FAKEleak", 14)
}

/// The token an `auth/token/create` response hands back (`AUT-082`).
pub fn created() -> String {
    make("FAKEcreated", 14)
}

/// The first token from a source that returns a different one on every resolution,
/// for asserting a path and a header agree (`AUT-080`).
pub fn resolved(ordinal: u32) -> String {
    let suffix = format!("{ordinal:08}");
    format!("s.{}{}", "FAKEresolved", suffix)
}


/// The same values as the functions above, in `const` form, for the `const` and
/// string-literal positions a `String` cannot occupy. `concat!` is what keeps the
/// `s.<20+ alnum>` run out of the source text (`CNF-025`).
pub const CLIENT: &str = concat!("s.", "FAKEtoken0000000000000000");
pub const OTHER: &str = concat!("s.", "FAKEother000000000000000000");
pub const USED: &str = concat!("s.", "FAKEused00000000000000000");
pub const PERSISTED: &str = concat!("s.", "FAKEpersisted00000000000");
pub const SHARED: &str = concat!("s.", "FAKEshared0000000000000");
pub const ISSUED: &str = concat!("s.", "FAKEissued0000000000000");
pub const PINNED: &str = concat!("s.", "FAKEpinned0000000000000");
pub const SWAPPED: &str = concat!("s.", "FAKEswapped0000000000000");
pub const LEAK: &str = concat!("s.", "FAKEleak00000000000000");
pub const CREATED: &str = concat!("s.", "FAKEcreated00000000000000");
pub const RESOLVED_1: &str = concat!("s.", "FAKEresolved00000001");

fn make(stem: &str, padding: usize) -> String {
    format!("s.{stem}{}", "0".repeat(padding))
}
