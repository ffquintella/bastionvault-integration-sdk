//! The SDK error type (D-M1a-1).
//!
//! M1a ships the whole shape of [`Error`] — every ERR-001 field, the ERR-002 one-line
//! [`Display`](fmt::Display) form, and every [`ErrorCategory`] — but only the
//! `BV-CONFIG-001..008` codes are populated with messages and hints (transcribed
//! verbatim from `specifications/appendix-b-error-catalogue.md`). Every other category
//! is M1c work.

use std::collections::BTreeMap;
use std::fmt;
use std::time::{Duration, SystemTime};

/// The category a stable error code belongs to (ERR-001's `Category` field).
///
/// Only `Configuration` is raised by M1a. The rest of the enum exists so the type lands
/// whole (D-M1a-1) and later milestones do not have to make a breaking change to add a
/// variant.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum ErrorCategory {
    Configuration,
    Input,
    Transport,
    Protocol,
    Authentication,
    Authorization,
    NotFound,
    Conflict,
    RateLimit,
    Quota,
    ServerState,
    Discovery,
    Engine,
}

impl fmt::Display for ErrorCategory {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let name = match self {
            Self::Configuration => "Configuration",
            Self::Input => "Input",
            Self::Transport => "Transport",
            Self::Protocol => "Protocol",
            Self::Authentication => "Authentication",
            Self::Authorization => "Authorization",
            Self::NotFound => "NotFound",
            Self::Conflict => "Conflict",
            Self::RateLimit => "RateLimit",
            Self::Quota => "Quota",
            Self::ServerState => "ServerState",
            Self::Discovery => "Discovery",
            Self::Engine => "Engine",
        };
        f.write_str(name)
    }
}

/// A structured value carried in [`Error::details`] (ERR-001's `Details` map).
///
/// Deliberately not `serde_json::Value`: the base crate has no JSON dependency, and
/// configuration-error details are always one of these three shapes.
#[derive(Debug, Clone, PartialEq)]
pub enum DetailValue {
    Str(String),
    Int(i64),
    Bool(bool),
}

impl From<&str> for DetailValue {
    fn from(value: &str) -> Self {
        Self::Str(value.to_owned())
    }
}

impl From<String> for DetailValue {
    fn from(value: String) -> Self {
        Self::Str(value)
    }
}

impl From<i64> for DetailValue {
    fn from(value: i64) -> Self {
        Self::Int(value)
    }
}

impl From<bool> for DetailValue {
    fn from(value: bool) -> Self {
        Self::Bool(value)
    }
}

impl fmt::Display for DetailValue {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Str(value) => f.write_str(value),
            Self::Int(value) => write!(f, "{value}"),
            Self::Bool(value) => write!(f, "{value}"),
        }
    }
}

/// The SDK's single error type (ERR-001..006).
///
/// A configuration error (the only kind M1a raises) always has `retryable == false`,
/// `attempts == 0`, and no `status_code`/`server_message`/`method`/`path`/`address`
/// (D-M1a-1).
pub struct Error {
    code: &'static str,
    category: ErrorCategory,
    message: String,
    hint: String,
    server_message: Option<String>,
    server_errors: Vec<String>,
    status_code: Option<u16>,
    retry_after: Option<Duration>,
    retryable: bool,
    method: Option<String>,
    path: Option<String>,
    address: Option<String>,
    attempts: u32,
    details: BTreeMap<String, DetailValue>,
    cause: Option<Box<dyn std::error::Error + Send + Sync + 'static>>,
    timestamp: SystemTime,
}

impl Error {
    pub(crate) fn new(
        code: &'static str,
        category: ErrorCategory,
        message: impl Into<String>,
        hint: impl Into<String>,
    ) -> Self {
        Self {
            code,
            category,
            message: message.into(),
            hint: hint.into(),
            server_message: None,
            server_errors: Vec::new(),
            status_code: None,
            retry_after: None,
            retryable: false,
            method: None,
            path: None,
            address: None,
            attempts: 0,
            details: BTreeMap::new(),
            cause: None,
            timestamp: SystemTime::now(),
        }
    }

    pub(crate) fn with_detail(mut self, key: impl Into<String>, value: impl Into<DetailValue>) -> Self {
        self.details.insert(key.into(), value.into());
        self
    }

    pub(crate) fn with_cause(
        mut self,
        cause: impl std::error::Error + Send + Sync + 'static,
    ) -> Self {
        self.cause = Some(Box::new(cause));
        self
    }

    /// ERR-006: sets `Retryable`. M1b callers always pass the value ERR-006 dictates
    /// for the code being constructed, never the mapper's own opinion (D-M1b-4b).
    pub(crate) fn with_retryable(mut self, value: bool) -> Self {
        self.retryable = value;
        self
    }

    pub(crate) fn with_status_code(mut self, value: u16) -> Self {
        self.status_code = Some(value);
        self
    }

    pub(crate) fn with_server_message(mut self, value: impl Into<String>) -> Self {
        self.server_message = Some(value.into());
        self
    }

    pub(crate) fn with_server_errors(mut self, value: Vec<String>) -> Self {
        self.server_errors = value;
        self
    }

    pub(crate) fn with_retry_after(mut self, value: Option<Duration>) -> Self {
        self.retry_after = value;
        self
    }

    pub(crate) fn with_method(mut self, value: impl Into<String>) -> Self {
        self.method = Some(value.into());
        self
    }

    pub(crate) fn with_path(mut self, value: impl Into<String>) -> Self {
        self.path = Some(value.into());
        self
    }

    pub(crate) fn with_address(mut self, value: impl Into<String>) -> Self {
        self.address = Some(value.into());
        self
    }

    /// CFG-055: the number of attempts actually made.
    pub(crate) fn with_attempts(mut self, value: u32) -> Self {
        self.attempts = value;
        self
    }

    /// The stable code, e.g. `"BV-CONFIG-001"` (ERR-004/ERR-005 — matchable by code
    /// without string comparison via [`Error::code`] returning the same literal the
    /// `error_codes` module constants hold).
    pub fn code(&self) -> &'static str {
        self.code
    }

    /// The error's category (ERR-004).
    pub fn category(&self) -> ErrorCategory {
        self.category
    }

    /// The default human-readable message.
    pub fn message(&self) -> &str {
        &self.message
    }

    /// Actionable guidance (never empty).
    pub fn hint(&self) -> &str {
        &self.hint
    }

    /// Whether an identical retry may succeed without operator/developer action
    /// (ERR-006). Always `false` for a configuration error.
    pub fn retryable(&self) -> bool {
        self.retryable
    }

    /// Number of attempts made. Always `0` for a configuration error (D-M1a-1): no
    /// request was sent.
    pub fn attempts(&self) -> u32 {
        self.attempts
    }

    /// Structured extras, e.g. `details["setting"]` or `details["path"]`.
    pub fn details(&self) -> &BTreeMap<String, DetailValue> {
        &self.details
    }

    pub fn status_code(&self) -> Option<u16> {
        self.status_code
    }

    pub fn server_message(&self) -> Option<&str> {
        self.server_message.as_deref()
    }

    pub fn server_errors(&self) -> &[String] {
        &self.server_errors
    }

    pub fn retry_after(&self) -> Option<Duration> {
        self.retry_after
    }

    pub fn method(&self) -> Option<&str> {
        self.method.as_deref()
    }

    pub fn path(&self) -> Option<&str> {
        self.path.as_deref()
    }

    pub fn address(&self) -> Option<&str> {
        self.address.as_deref()
    }

    pub fn timestamp(&self) -> SystemTime {
        self.timestamp
    }
}

impl fmt::Display for Error {
    /// ERR-002: exactly `"<Code>: <Message> — <Hint>"`, optionally followed by
    /// ` [HTTP <status> <METHOD> <path>]` and ` (server: "<ServerMessage>")`. No
    /// newlines.
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}: {} — {}", self.code, self.message, self.hint)?;
        if let (Some(status), method, path) =
            (self.status_code, self.method.as_deref(), self.path.as_deref())
        {
            write!(
                f,
                " [HTTP {status} {} {}]",
                method.unwrap_or(""),
                path.unwrap_or("")
            )?;
        }
        if let Some(server_message) = &self.server_message {
            write!(f, " (server: {server_message:?})")?;
        }
        Ok(())
    }
}

impl fmt::Debug for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Error")
            .field("code", &self.code)
            .field("category", &self.category)
            .field("message", &self.message)
            .field("hint", &self.hint)
            .field("retryable", &self.retryable)
            .field("attempts", &self.attempts)
            .field("details", &self.details)
            .finish_non_exhaustive()
    }
}

impl std::error::Error for Error {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        self.cause
            .as_deref()
            .map(|cause| cause as &(dyn std::error::Error + 'static))
    }
}

/// Stable code constants (ERR-005). Every constant's value is also the literal string
/// form (`"BV-CONFIG-001"`), so logs from different SDKs correlate.
pub mod error_codes {
    pub const CONFIG_INVALID_ADDRESS: &str = "BV-CONFIG-001";
    pub const CONFIG_INSECURE_HTTP_NOT_ALLOWED: &str = "BV-CONFIG-002";
    pub const CONFIG_INVALID_SETTING_VALUE: &str = "BV-CONFIG-003";
    pub const CONFIG_CLIENT_CERT_INCOMPLETE: &str = "BV-CONFIG-004";
    pub const CONFIG_FILE_NOT_READABLE: &str = "BV-CONFIG-005";
    pub const CONFIG_INVALID_PEM: &str = "BV-CONFIG-006";
    pub const CONFIG_INVALID_NAMESPACE: &str = "BV-CONFIG-007";
    pub const CONFIG_RESERVED_HEADER: &str = "BV-CONFIG-008";
    pub const CONFIG_LIST_VERB_UNSUPPORTED: &str = "BV-CONFIG-009";

    pub const INPUT_INVALID_ARGUMENT: &str = "BV-INPUT-001";
    pub const INPUT_UNSUPPORTED_OPTION: &str = "BV-INPUT-006";
    pub const INPUT_BODY_TOO_LARGE: &str = "BV-INPUT-007";
    pub const INPUT_CHUNK_INDEX_OUT_OF_RANGE: &str = "BV-INPUT-008";

    pub const TRANSPORT_CONNECTION_FAILED: &str = "BV-TRANSPORT-001";
    pub const TRANSPORT_TIMEOUT: &str = "BV-TRANSPORT-002";
    pub const TRANSPORT_TLS_ERROR: &str = "BV-TRANSPORT-003";
    pub const TRANSPORT_RESPONSE_TOO_LARGE: &str = "BV-TRANSPORT-004";
    pub const TRANSPORT_CANCELLED: &str = "BV-TRANSPORT-005";

    pub const PROTOCOL_METHOD_NOT_ALLOWED: &str = "BV-PROTOCOL-001";
    pub const PROTOCOL_UNEXPECTED_RESPONSE: &str = "BV-PROTOCOL-002";
    pub const PROTOCOL_UNEXPECTED_REDIRECT: &str = "BV-PROTOCOL-003";

    pub const AUTH_UNAUTHENTICATED: &str = "BV-AUTH-002";
    pub const AUTHZ_PERMISSION_DENIED: &str = "BV-AUTHZ-001";

    pub const NOTFOUND_PATH_NOT_FOUND: &str = "BV-NOTFOUND-001";

    pub const CONFLICT_RECORDING_DIGEST_MISMATCH: &str = "BV-CONFLICT-002";
    pub const CONFLICT_BROKERED_RESOURCE_STATIC_CREDENTIAL: &str = "BV-CONFLICT-003";

    pub const RATE_LIMITED_BY_DOS_GUARD: &str = "BV-RATE-001";
    pub const RATE_NAMESPACE_QUOTA_EXCEEDED: &str = "BV-RATE-002";
    pub const QUOTA_NAMESPACE_QUOTA_EXCEEDED: &str = "BV-QUOTA-001";

    pub const SERVER_SEALED: &str = "BV-SERVER-001";
    pub const SERVER_UNAVAILABLE: &str = "BV-SERVER-002";
    pub const SERVER_INTERNAL_ERROR: &str = "BV-SERVER-005";
}

/// Constructors for the eight `BV-CONFIG-*` codes, transcribed verbatim (message and
/// hint) from `specifications/appendix-b-error-catalogue.md` rows 14-21.
pub(crate) mod config_errors {
    use super::{error_codes, Error, ErrorCategory};

    pub(crate) fn invalid_address() -> Error {
        Error::new(
            error_codes::CONFIG_INVALID_ADDRESS,
            ErrorCategory::Configuration,
            "The server address is missing or not a valid URL or cluster name.",
            "Set `Address` (or `BASTIONVAULT_ADDR`) to `https://host:8200`, or to a bare \
             DNS name for cluster discovery. IPv6 literals must be bracketed.",
        )
    }

    pub(crate) fn insecure_http_not_allowed() -> Error {
        Error::new(
            error_codes::CONFIG_INSECURE_HTTP_NOT_ALLOWED,
            ErrorCategory::Configuration,
            "Plain `http://` to a non-loopback host is not allowed.",
            "Use `https://`, or set `AllowInsecureHttp = true` only for isolated test \
             networks.",
        )
    }

    pub(crate) fn invalid_setting_value() -> Error {
        Error::new(
            error_codes::CONFIG_INVALID_SETTING_VALUE,
            ErrorCategory::Configuration,
            "A configuration value has the wrong type or range.",
            "Check `Details.setting`; booleans accept 1/0/true/false/yes/no/on/off, \
             durations accept `30s`, `1m30s` or integer seconds; timeouts must be > 0.",
        )
    }

    pub(crate) fn client_cert_incomplete() -> Error {
        Error::new(
            error_codes::CONFIG_CLIENT_CERT_INCOMPLETE,
            ErrorCategory::Configuration,
            "Only one of `ClientCertPath` / `ClientKeyPath` is set.",
            "Provide both the client certificate and its private key (PEM), or neither.",
        )
    }

    pub(crate) fn file_not_readable() -> Error {
        Error::new(
            error_codes::CONFIG_FILE_NOT_READABLE,
            ErrorCategory::Configuration,
            "A configured file cannot be read.",
            "Check `Details.path` exists and the process user can read it.",
        )
    }

    pub(crate) fn invalid_pem() -> Error {
        Error::new(
            error_codes::CONFIG_INVALID_PEM,
            ErrorCategory::Configuration,
            "A certificate or key is not valid PEM.",
            "Ensure the file contains `-----BEGIN CERTIFICATE-----`/`PRIVATE KEY` blocks \
             and is not DER or PKCS#12.",
        )
    }

    pub(crate) fn invalid_namespace() -> Error {
        Error::new(
            error_codes::CONFIG_INVALID_NAMESPACE,
            ErrorCategory::Configuration,
            "The namespace path is malformed.",
            "Use `parent/child` without a leading slash, whitespace or control characters.",
        )
    }

    pub(crate) fn reserved_header() -> Error {
        Error::new(
            error_codes::CONFIG_RESERVED_HEADER,
            ErrorCategory::Configuration,
            "A custom header would override a header the SDK manages.",
            "Remove `X-BastionVault-Token`, `X-Vault-Token`, `Authorization`, `Cookie`, \
             `X-BastionVault-Namespace`, `Host`, `Content-Length` from `Headers`; use \
             `Token`/`Namespace` instead.",
        )
    }
}

/// Constructors for the D-M1b-4 populated status-derived codes, transcribed verbatim
/// (message and hint) from `specifications/appendix-b-error-catalogue.md`. `Retryable`
/// is set here per ERR-006, independent of `RetryPolicy.RetryOn` (D-M1b-4b): these two
/// are different sets and neither derives from the other.
pub(crate) mod mapping_errors {
    use super::{error_codes, Error, ErrorCategory};

    pub(crate) fn config_list_verb_unsupported() -> Error {
        Error::new(
            error_codes::CONFIG_LIST_VERB_UNSUPPORTED,
            ErrorCategory::Configuration,
            "The HTTP stack cannot send the custom `LIST` method.",
            "Use the SDK's default transport or an HTTP client that allows non-standard \
             methods; the server does not support `?list=true`.",
        )
    }

    pub(crate) fn input_invalid_argument() -> Error {
        Error::new(
            error_codes::INPUT_INVALID_ARGUMENT,
            ErrorCategory::Input,
            "An argument is missing or invalid.",
            "See `Details.argument` and `Details.reason`; required strings must be \
             non-empty, `env` cannot contain `/`, `env` and `envs` are mutually exclusive.",
        )
    }

    pub(crate) fn input_body_too_large() -> Error {
        Error::new(
            error_codes::INPUT_BODY_TOO_LARGE,
            ErrorCategory::Input,
            "The request body exceeds the server limit.",
            "Keep bodies under 32 MiB; for files, upload smaller versions or use sync \
             targets.",
        )
    }

    pub(crate) fn input_chunk_index_out_of_range() -> Error {
        Error::new(
            error_codes::INPUT_CHUNK_INDEX_OUT_OF_RANGE,
            ErrorCategory::Input,
            "The recording chunk index is past the end.",
            "Read chunk 0 first and stop at `eof`; `Details.chunk_count` is the real \
             count.",
        )
    }

    pub(crate) fn input_unsupported_option() -> Error {
        Error::new(
            error_codes::INPUT_UNSUPPORTED_OPTION,
            ErrorCategory::Input,
            "The option is not supported by BastionVault.",
            "Response wrapping (`WrapTtl`) is not implemented by the server; remove the \
             option.",
        )
    }

    pub(crate) fn transport_connection_failed() -> Error {
        Error::new(
            error_codes::TRANSPORT_CONNECTION_FAILED,
            ErrorCategory::Transport,
            "Could not connect to the server.",
            "Check `Address`, DNS, firewall and that the server is listening (default \
             `https://127.0.0.1:8200`).",
        )
        .with_retryable(true)
    }

    pub(crate) fn transport_timeout() -> Error {
        Error::new(
            error_codes::TRANSPORT_TIMEOUT,
            ErrorCategory::Transport,
            "The request timed out.",
            "Increase `Timeout`/`ConnectTimeout`, check server load; long-poll calls need \
             \u{2265} 40 s.",
        )
        .with_retryable(true)
    }

    pub(crate) fn transport_tls_error() -> Error {
        Error::new(
            error_codes::TRANSPORT_TLS_ERROR,
            ErrorCategory::Transport,
            "TLS handshake or certificate verification failed.",
            "Provide the server CA via `CaCertPath`; check `TlsServerName` matches a SAN; \
             verify the clock. Only as a diagnostic step, and never in production, \
             `TlsSkipVerify` confirms whether trust is the cause.",
        )
        .with_retryable(true)
    }

    pub(crate) fn transport_response_too_large() -> Error {
        Error::new(
            error_codes::TRANSPORT_RESPONSE_TOO_LARGE,
            ErrorCategory::Transport,
            "The response exceeded `MaxResponseBytes`.",
            "Use the chunked route (`Rustion.Recordings.Download`) or paging \
             (`*-info`), or raise `MaxResponseBytes`.",
        )
    }

    pub(crate) fn transport_cancelled() -> Error {
        Error::new(
            error_codes::TRANSPORT_CANCELLED,
            ErrorCategory::Transport,
            "The operation was cancelled.",
            "The caller cancelled; no request state is known. Retry is the caller's \
             decision.",
        )
    }

    pub(crate) fn protocol_method_not_allowed() -> Error {
        Error::new(
            error_codes::PROTOCOL_METHOD_NOT_ALLOWED,
            ErrorCategory::Protocol,
            "The server does not accept this HTTP method on this path.",
            "Only GET, POST/PUT, DELETE and LIST are routed; use the matching logical \
             operation.",
        )
    }

    pub(crate) fn protocol_unexpected_response() -> Error {
        Error::new(
            error_codes::PROTOCOL_UNEXPECTED_RESPONSE,
            ErrorCategory::Protocol,
            "The server response could not be interpreted.",
            "The body was not JSON or not a known shape (`Details.snippet`); confirm \
             `Address` points at a BastionVault API listener, not a proxy or GUI.",
        )
    }

    pub(crate) fn protocol_unexpected_redirect() -> Error {
        Error::new(
            error_codes::PROTOCOL_UNEXPECTED_REDIRECT,
            ErrorCategory::Protocol,
            "The server answered with a redirect.",
            "BastionVault never redirects; a proxy or load balancer in front of it does. \
             Point `Address` at the vault or fix the proxy.",
        )
    }

    pub(crate) fn auth_unauthenticated() -> Error {
        Error::new(
            error_codes::AUTH_UNAUTHENTICATED,
            ErrorCategory::Authentication,
            "The server requires authentication for this call.",
            "Provide a valid token; for connect-MFA calls the caller must be a userpass \
             principal.",
        )
    }

    pub(crate) fn authz_permission_denied() -> Error {
        Error::new(
            error_codes::AUTHZ_PERMISSION_DENIED,
            ErrorCategory::Authorization,
            "The token does not have permission for this path (or the token is invalid, \
             expired or revoked).",
            "Check the token's policies grant the capability on `Details.path` \
             (`Sys.CapabilitiesSelf`); verify the token with `Auth.Token.LookupSelf`; if \
             the credential is namespace-scoped set `Namespace`; a `token_bound_cidrs` or \
             `bound_source_ips` rule may exclude this client.",
        )
    }

    pub(crate) fn notfound_path_not_found() -> Error {
        Error::new(
            error_codes::NOTFOUND_PATH_NOT_FOUND,
            ErrorCategory::NotFound,
            "Nothing exists at this path.",
            "Check the mount and the engine's path layout (`Details.path`); KV v2 data \
             lives under `<mount>/data/<name>`; unregistered `sys/*` routes also answer \
             404.",
        )
    }

    /// D-M1b-4 populates this code, but its trigger is a `409` **plus** a specific
    /// server message (`sha256`/`digest`) — message recognition is M1c
    /// (D-M1b-23's 409 note). No M1b fixture reaches it through status alone; kept
    /// here as the catalogue entry M1c wires up, not dead weight.
    #[allow(dead_code)]
    pub(crate) fn conflict_recording_digest_mismatch() -> Error {
        Error::new(
            error_codes::CONFLICT_RECORDING_DIGEST_MISMATCH,
            ErrorCategory::Conflict,
            "The recording bytes do not match the recorded digest.",
            "Deterministic failure: do not retry; inspect the bastion and the sidecar \
             digest.",
        )
    }

    /// Same note as `conflict_recording_digest_mismatch`: message-based (M1c).
    #[allow(dead_code)]
    pub(crate) fn conflict_brokered_resource_static_credential() -> Error {
        Error::new(
            error_codes::CONFLICT_BROKERED_RESOURCE_STATIC_CREDENTIAL,
            ErrorCategory::Conflict,
            "A static SSH credential cannot be attached to a brokered resource.",
            "Remove `private_key`/`password` or change the resource's `login_class`.",
        )
    }

    pub(crate) fn rate_limited_by_dos_guard() -> Error {
        Error::new(
            error_codes::RATE_LIMITED_BY_DOS_GUARD,
            ErrorCategory::RateLimit,
            "The server's abuse guard temporarily blocked this client IP.",
            "The rate gate is paused for `RetryAfter` seconds. Reduce request fan-out: \
             use `Sys.Batch`, `Kv.ReadMany`, `*-info` pages and a read cache. Do not add \
             retries.",
        )
    }

    pub(crate) fn rate_namespace_quota_exceeded() -> Error {
        Error::new(
            error_codes::RATE_NAMESPACE_QUOTA_EXCEEDED,
            ErrorCategory::RateLimit,
            "The namespace request-rate quota was exceeded.",
            "Slow down or ask an admin to raise `request_rate` on the namespace; back off \
             before retrying.",
        )
        .with_retryable(true)
    }

    pub(crate) fn quota_namespace_quota_exceeded() -> Error {
        Error::new(
            error_codes::QUOTA_NAMESPACE_QUOTA_EXCEEDED,
            ErrorCategory::Quota,
            "A namespace capacity quota was reached.",
            "`ServerMessage` names the quota (mounts, leases, entities, storage); free \
             capacity or raise the quota via `Sys.UpdateNamespace`.",
        )
    }

    pub(crate) fn server_sealed() -> Error {
        Error::new(
            error_codes::SERVER_SEALED,
            ErrorCategory::ServerState,
            "The vault is sealed.",
            "An operator must unseal it (`bvault operator unseal` or HSM auto-unseal); \
             the SDK does not retry. Use `Sys.Health` to watch for readiness.",
        )
    }

    pub(crate) fn server_unavailable() -> Error {
        Error::new(
            error_codes::SERVER_UNAVAILABLE,
            ErrorCategory::ServerState,
            "The server is temporarily unavailable.",
            "Cluster has no leader/quorum, node unhealthy, or HSM unreachable; the SDK \
             retries idempotent calls. Check `Sys.ClusterStatus` and node health.",
        )
        .with_retryable(true)
    }

    pub(crate) fn server_internal_error() -> Error {
        Error::new(
            error_codes::SERVER_INTERNAL_ERROR,
            ErrorCategory::ServerState,
            "The server reported an internal error.",
            "Read `ServerMessage`; many engine validation errors are reported as 500 — \
             the message names the field or object. Check server logs if it is generic.",
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn display_matches_the_one_line_err_002_form() {
        let error = config_errors::invalid_address();
        let rendered = error.to_string();
        assert_eq!(
            rendered,
            "BV-CONFIG-001: The server address is missing or not a valid URL or cluster \
             name. — Set `Address` (or `BASTIONVAULT_ADDR`) to `https://host:8200`, or to \
             a bare DNS name for cluster discovery. IPv6 literals must be bracketed."
        );
        assert!(!rendered.contains('\n'));
    }

    #[test]
    fn config_errors_are_never_retryable_with_zero_attempts() {
        for error in [
            config_errors::invalid_address(),
            config_errors::insecure_http_not_allowed(),
            config_errors::invalid_setting_value(),
            config_errors::client_cert_incomplete(),
            config_errors::file_not_readable(),
            config_errors::invalid_pem(),
            config_errors::invalid_namespace(),
            config_errors::reserved_header(),
        ] {
            assert!(!error.retryable());
            assert_eq!(error.attempts(), 0);
            assert!(error.status_code().is_none());
            assert!(error.server_message().is_none());
            assert!(error.method().is_none());
            assert!(error.path().is_none());
            assert!(error.address().is_none());
        }
    }

    #[test]
    fn code_constant_and_literal_string_agree() {
        assert_eq!(
            config_errors::invalid_address().code(),
            error_codes::CONFIG_INVALID_ADDRESS
        );
        assert_eq!(error_codes::CONFIG_INVALID_ADDRESS, "BV-CONFIG-001");
    }

    #[test]
    fn details_carry_structured_context() {
        let error = config_errors::invalid_setting_value().with_detail("setting", "Timeout");
        assert_eq!(
            error.details().get("setting"),
            Some(&DetailValue::Str("Timeout".to_owned()))
        );
    }

    #[test]
    fn every_error_category_display_form_is_its_name() {
        let names = [
            (ErrorCategory::Configuration, "Configuration"),
            (ErrorCategory::Input, "Input"),
            (ErrorCategory::Transport, "Transport"),
            (ErrorCategory::Protocol, "Protocol"),
            (ErrorCategory::Authentication, "Authentication"),
            (ErrorCategory::Authorization, "Authorization"),
            (ErrorCategory::NotFound, "NotFound"),
            (ErrorCategory::Conflict, "Conflict"),
            (ErrorCategory::RateLimit, "RateLimit"),
            (ErrorCategory::Quota, "Quota"),
            (ErrorCategory::ServerState, "ServerState"),
            (ErrorCategory::Discovery, "Discovery"),
            (ErrorCategory::Engine, "Engine"),
        ];
        for (category, name) in names {
            assert_eq!(category.to_string(), name);
        }
    }

    #[test]
    fn detail_value_conversions_and_display() {
        assert_eq!(DetailValue::from("text").to_string(), "text");
        assert_eq!(DetailValue::from(String::from("owned")).to_string(), "owned");
        assert_eq!(DetailValue::from(42_i64).to_string(), "42");
        assert_eq!(DetailValue::from(true).to_string(), "true");
    }

    #[test]
    fn display_renders_the_http_suffix_even_when_method_and_path_are_absent() {
        let error = config_errors::invalid_address().with_status_code(503);
        let rendered = error.to_string();
        assert!(rendered.contains("[HTTP 503  ]"));
    }

    #[test]
    fn every_err_001_field_is_reachable_through_its_accessor() {
        let error = Error {
            code: "BV-TRANSPORT-001",
            category: ErrorCategory::Transport,
            message: "connection refused".to_owned(),
            hint: "retry the request".to_owned(),
            server_message: Some("upstream unavailable".to_owned()),
            server_errors: vec!["one".to_owned(), "two".to_owned()],
            status_code: Some(503),
            retry_after: Some(Duration::from_secs(5)),
            retryable: true,
            method: Some("GET".to_owned()),
            path: Some("secret/data/x".to_owned()),
            address: Some("vault.example.com".to_owned()),
            attempts: 2,
            details: BTreeMap::new(),
            cause: Some(Box::new(std::io::Error::other("boom"))),
            timestamp: SystemTime::now(),
        };
        assert_eq!(error.category(), ErrorCategory::Transport);
        assert_eq!(error.message(), "connection refused");
        assert_eq!(error.server_errors(), &["one".to_owned(), "two".to_owned()]);
        assert_eq!(error.retry_after(), Some(Duration::from_secs(5)));
        assert_eq!(error.method(), Some("GET"));
        assert_eq!(error.path(), Some("secret/data/x"));
        assert_eq!(error.address(), Some("vault.example.com"));
        assert!(error.timestamp() <= SystemTime::now());

        let rendered = error.to_string();
        assert!(rendered.contains("[HTTP 503 GET secret/data/x]"));
        assert!(rendered.contains(r#"(server: "upstream unavailable")"#));

        let debug = format!("{error:?}");
        assert!(debug.contains("BV-TRANSPORT-001"));

        use std::error::Error as _;
        assert!(error.source().is_some());
    }
}
