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
