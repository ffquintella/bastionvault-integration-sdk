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
// `Client` (decisions/0003-m1a-configuration.md).
// M1b: the transport seam, the logical layer, retry execution, the rate-gate pause and
// the status->code mapping function (decisions/0004-m1b-transport.md).
// M1c: the generated Appendix B catalogue, message recognition, hint enrichment and the
// ERR-002/ERR-003 string rules (decisions/0005-m1c-error-model.md).
// M2a: the token source model and its resolution seam, the `Auth` grouping, the nine
// token-store operations and the token-helper write path
// (decisions/0006-m2-authentication.md).
mod auth;
mod client;
mod clock;
mod config;
mod enrichment;
mod env;
mod error;
mod error_catalog;
mod error_paths;
// Generated from specifications/appendix-b-error-catalogue.md by tools/error-catalogue
// (D-M1c-1). Private: only `error_codes` is re-exported, so the generator is free to
// reshape the tuple rows without a public-API change.
mod generated;
mod jitter;
mod logger;
mod logical;
mod mapping;
mod observer;
mod parse;
mod pem;
mod rate;
mod recognition;
mod retry;
mod secret;
mod tls;
mod token_files;
mod token_source;
mod transport;
mod transport_http;

pub use auth::{Auth, CreateTokenRequest, TokenInfo, TokenOps};
pub use client::Client;
pub use clock::{Clock, SystemClock};
pub use config::{ApiPrefix, AutoRenew, ClientConfig, ClientConfigBuilder};
pub use env::EnvironmentSource;
pub use error::{DetailValue, Error, ErrorCategory};
pub use error_catalog::{ErrorCatalog, ErrorCatalogEntry};
/// Stable code constants (ERR-005), generated from Appendix B §1 (D-M1c-1/D-M1c-2).
/// Every constant's value is also the literal string form (`"BV-CONFIG-001"`), so logs
/// from different SDKs correlate.
pub use generated::error_catalog_data::error_codes;
pub use jitter::{DefaultJitterSource, JitterSource};
pub use logger::{ClientLogger, NoopLogger};
pub use logical::{AuthInfo, Logical, RawResponse, Response};
pub use observer::{metric_names, NoopRequestObserver, RequestEvent, RequestObserver};
pub use rate::{RateGate, RateGateState};
pub use retry::RetryPolicy;
pub use secret::SecretString;
pub use tls::{ClientCertificate, TlsParameters, TlsVersion};
pub use token_source::{TokenCallback, TokenFuture, TokenSource, TokenSourceKind};
pub use transport::{
    FakeTransport, RequestOptions, ScriptedOutcome, Transport, TransportFailureKind, TransportRequest,
    TransportResponse,
};
pub use transport_http::HttpTransport;
