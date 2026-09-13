//! BastionVault Integration SDK — base Rust crate.
//!
//! M0 exposes exactly one minimal public symbol pair per decision D-M0-12a: the
//! specification version this SDK implements, and the SDK (crate) version. Both are
//! metadata, not behaviour, and exist so the M0 coverage gate (CNF-041/D-M0-12a) has a
//! non-zero denominator and the CNF-027 public-API baseline has something to diff.
//!
//! These are functions rather than `const` values: a `pub const &'static str` compiles to
//! inert rodata with no instrumented coverage region, which keeps the denominator at zero
//! (see D-M0-12, superseded by D-M0-12a). A function body is instrumented, so calling it
//! from the `_cnf_041` test produces measurable line/region coverage.

/// The version of `specifications/` (see `specifications/00-overview.md#specification-version`)
/// that this SDK implements.
pub fn specification_version() -> &'static str {
    "1.0.0"
}

/// The version of this SDK crate, taken from `Cargo.toml` at compile time.
pub fn sdk_version() -> &'static str {
    env!("CARGO_PKG_VERSION")
}
