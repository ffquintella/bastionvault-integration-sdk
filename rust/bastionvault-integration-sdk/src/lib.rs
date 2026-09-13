// `Error` is a fixed-shape public type (DR-0003): every ERR-001 field lands whole in
// M1a, which makes it larger than clippy's default `result_large_err` threshold.
// Boxing it would change ergonomics for every caller to shrink a type that is never on
// a hot path (configuration errors are raised once, at construction). Allowed
// deliberately rather than reshaping the type to satisfy a lint.
#![allow(clippy::result_large_err)]

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

// M1a: client configuration, the error type skeleton, `SecretString` and a minimal
// `Client` (decisions/0003-m1a-configuration.md). Everything else (operations, real
// transport execution, retry execution, auth) is a later milestone.
mod client;
mod config;
mod env;
mod error;
mod logger;
mod parse;
mod pem;
mod rate;
mod retry;
mod secret;
mod tls;
mod transport;

pub use client::Client;
pub use config::{ApiPrefix, AutoRenew, ClientConfig, ClientConfigBuilder};
pub use env::EnvironmentSource;
pub use error::{error_codes, DetailValue, Error, ErrorCategory};
pub use logger::{ClientLogger, NoopLogger};
pub use rate::RateGate;
pub use retry::RetryPolicy;
pub use secret::SecretString;
pub use tls::{ClientCertificate, TlsParameters, TlsVersion};
pub use transport::{RequestOptions, Transport};
