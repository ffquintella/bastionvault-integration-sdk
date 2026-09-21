//! The SDK error type (D-M1a-1).
//!
//! M1a shipped the whole shape of [`Error`] — every ERR-001 field, the ERR-002 one-line
//! [`Display`](fmt::Display) form, and every [`ErrorCategory`].
//!
//! At M1c the messages, hints, categories and ERR-006 retryability stop being
//! transcribed here and come from the generated catalogue instead
//! ([`crate::ErrorCatalog`], emitted from `specifications/appendix-b-error-catalogue.md`
//! by `tools/error-catalogue`, D-M1c-1). The constructor modules below keep their names
//! so call sites read as before, but each is now a single lookup: there is exactly one
//! copy of every string in this crate, and it is generated.

use std::collections::BTreeMap;
use std::fmt;
use std::time::{Duration, SystemTime};

/// The category a stable error code belongs to (ERR-001's `Category` field).
///
/// The full set lands whole (D-M1a-1); from M1c every variant is reachable, because the
/// generated catalogue carries all ~130 Appendix B codes.
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
/// every `Details` value the SDK produces is one of these four shapes.
///
/// [`DetailValue::List`] exists for D-M1c-4's `keys` capture, whose value is "the listed
/// keys, in order" — a joined string would lose the ordering the requirement names.
#[derive(Debug, Clone, PartialEq)]
pub enum DetailValue {
    Str(String),
    Int(i64),
    Bool(bool),
    List(Vec<String>),
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

impl From<Vec<String>> for DetailValue {
    fn from(value: Vec<String>) -> Self {
        Self::List(value)
    }
}

impl fmt::Display for DetailValue {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Str(value) => f.write_str(value),
            Self::Int(value) => write!(f, "{value}"),
            Self::Bool(value) => write!(f, "{value}"),
            Self::List(values) => f.write_str(&values.join(", ")),
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

    /// Builds the error for `code` from the generated catalogue (ERR-036, D-M1c-1):
    /// message, hint, category and ERR-006 retryability all come from the one Appendix B
    /// row, so nothing in this crate can drift from the specification.
    ///
    /// A code absent from the catalogue is a generator or wiring defect, not a caller
    /// error, so this panics rather than returning an `Option` every construction site
    /// would have to thread through — the same internal contract .NET's
    /// `ErrorCatalog.Require` carries.
    pub(crate) fn from_catalog(code: &'static str) -> Self {
        let entry = crate::error_catalog::ErrorCatalog::require(code);
        Self::new(code, entry.category(), entry.message(), entry.hint())
            .with_retryable(entry.retryable())
    }

    /// Replaces the hint with an enriched one (ERR-034/ERR-040). The catalogue hint is
    /// never rewritten in place — the caller passes the catalogue hint plus its appended
    /// notes (D-M1c-5).
    pub(crate) fn with_hint(mut self, hint: impl Into<String>) -> Self {
        self.hint = hint.into();
        self
    }

    pub(crate) fn with_detail(mut self, key: impl Into<String>, value: impl Into<DetailValue>) -> Self {
        self.details.insert(key.into(), value.into());
        self
    }

    /// Merges a whole `Details` map in, for the ERR-035 captures recognition produced.
    pub(crate) fn with_details(mut self, details: BTreeMap<String, DetailValue>) -> Self {
        self.details.extend(details);
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

    /// Sets `Path`, applying ERR-003's redaction here — once, at construction — so the
    /// one-line form and any hint that interpolates the path are redacted by the same
    /// rule rather than by two that can drift (D-M1c-14 item 6).
    pub(crate) fn with_path(mut self, value: impl Into<String>) -> Self {
        self.path = Some(crate::error_paths::redact(&value.into()));
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

    /// An independently-owned copy of this error, for the one case where a single error
    /// has to be reported to several callers at once: D-M2-11(a)'s single-flight cell.
    ///
    /// The awaiters of one `TokenSource` login flight all observe **that flight's**
    /// failure (D-M2-17), so the flight's outcome is shared behind an `Arc` — but
    /// `Result<_, Error>` is what every public signature returns, and `Error` is not
    /// `Clone` because its `cause` is a boxed trait object with no `Clone` bound. So the
    /// cause is carried forward as its rendered text ([`RenderedCause`]) and every other
    /// field is copied verbatim.
    ///
    /// Deliberately not a `Clone` impl: this is a lossy copy, and making it the obvious
    /// thing to reach for would invite it onto the ordinary request path, where an error
    /// is built once and moved.
    pub(crate) fn duplicate(&self) -> Self {
        Self {
            code: self.code,
            category: self.category,
            message: self.message.clone(),
            hint: self.hint.clone(),
            server_message: self.server_message.clone(),
            server_errors: self.server_errors.clone(),
            status_code: self.status_code,
            retry_after: self.retry_after,
            retryable: self.retryable,
            method: self.method.clone(),
            // Assigned directly rather than through `with_path`, which would apply
            // ERR-003's redaction a second time to an already-redacted value.
            path: self.path.clone(),
            address: self.address.clone(),
            attempts: self.attempts,
            details: self.details.clone(),
            cause: self
                .cause
                .as_ref()
                .map(|cause| Box::new(RenderedCause(cause.to_string())) as Box<dyn std::error::Error + Send + Sync>),
            timestamp: self.timestamp,
        }
    }
}

/// A cause reduced to its rendered text, so [`Error::duplicate`] can carry a cause chain
/// across a share boundary that a boxed trait object cannot cross.
#[derive(Debug)]
pub(crate) struct RenderedCause(pub(crate) String);

impl fmt::Display for RenderedCause {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for RenderedCause {}

impl fmt::Display for Error {
    /// ERR-002: exactly `"<Code>: <Message> — <Hint>"`, optionally followed by
    /// ` [HTTP <status> <METHOD> <path>]` and ` (server: "<ServerMessage>")`. No
    /// newlines.
    ///
    /// The whole line is passed through the crate-internal `one_line` collapse before it is
    /// written: nothing in the catalogue carries a newline, but a server message is
    /// attacker-influenced input and must not be able to forge a second log line
    /// (ERR-002, D-M1c-14 item 6). `Path` is already redacted by
    /// the crate-internal `with_path` (ERR-003).
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let mut line = format!("{}: {} — {}", self.code, self.message, self.hint);
        if let (Some(status), method, path) =
            (self.status_code, self.method.as_deref(), self.path.as_deref())
        {
            line.push_str(&format!(
                " [HTTP {status} {} {}]",
                method.unwrap_or(""),
                path.unwrap_or("")
            ));
        }
        if let Some(server_message) = &self.server_message {
            // Quoted by hand rather than with `{:?}`: the debug form would escape a
            // newline to a literal `\n` instead of collapsing it, which is a different
            // rendering from .NET's and Python's for the same message (CLA-003). The
            // `one_line` pass below is what removes the break.
            line.push_str(&format!(" (server: \"{server_message}\")"));
        }
        f.write_str(&crate::error_paths::one_line(&line))
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

/// Named constructors for the codes this crate raises directly (as opposed to the ones
/// [`crate::mapping`] derives from a status or a recognition rule).
///
/// Each is a single lookup into the generated catalogue: the message, hint, category and
/// ERR-006 retryability live in `specifications/appendix-b-error-catalogue.md` and
/// nowhere else (D-M1c-1). The macro exists so adding a constructor cannot accidentally
/// add a *second* copy of a string — there is no body to put one in.
pub(crate) mod catalog_errors {
    use super::Error;
    use crate::generated::error_catalog_data::error_codes;

    macro_rules! catalog_constructors {
        ($($name:ident => $code:ident,)*) => {
            $(
                #[doc = concat!("`", stringify!($code), "`, from the generated catalogue.")]
                pub(crate) fn $name() -> Error {
                    Error::from_catalog(error_codes::$code)
                }
            )*
        };
    }

    catalog_constructors! {
        invalid_address => CONFIG_INVALID_ADDRESS,
        insecure_http_not_allowed => CONFIG_INSECURE_HTTP_NOT_ALLOWED,
        invalid_setting_value => CONFIG_INVALID_SETTING_VALUE,
        client_cert_incomplete => CONFIG_CLIENT_CERT_INCOMPLETE,
        file_not_readable => CONFIG_FILE_NOT_READABLE,
        invalid_pem => CONFIG_INVALID_PEM,
        invalid_namespace => CONFIG_INVALID_NAMESPACE,
        reserved_header => CONFIG_RESERVED_HEADER,
        config_list_verb_unsupported => CONFIG_LIST_VERB_UNSUPPORTED,
        input_body_too_large => INPUT_BODY_TOO_LARGE,
        input_unsupported_option => INPUT_UNSUPPORTED_OPTION,
        protocol_unexpected_response => PROTOCOL_UNEXPECTED_RESPONSE,
        transport_connection_failed => TRANSPORT_CONNECTION_FAILED,
        transport_response_too_large => TRANSPORT_RESPONSE_TOO_LARGE,
        transport_timeout => TRANSPORT_TIMEOUT,
        transport_tls_error => TRANSPORT_TLS_ERROR,
        // M2a (DR-0006): the token store's client-side guards, the D-M2-16 codes, and the
        // cancellation code D-M2-18 item 1's guard order has to reach ahead of them.
        input_invalid_argument => INPUT_INVALID_ARGUMENT,
        input_reserved_token_meta_key => INPUT_RESERVED_TOKEN_META_KEY,
        auth_token_source_failed => AUTH_TOKEN_SOURCE_FAILED,
        config_token_file_not_writable => CONFIG_TOKEN_FILE_NOT_WRITABLE,
        transport_cancelled => TRANSPORT_CANCELLED,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::generated::error_catalog_data::error_codes;

    #[test]
    fn display_matches_the_one_line_err_002_form() {
        let error = catalog_errors::invalid_address();
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
    fn configuration_errors_are_never_retryable_with_zero_attempts() {
        for error in [
            catalog_errors::invalid_address(),
            catalog_errors::insecure_http_not_allowed(),
            catalog_errors::invalid_setting_value(),
            catalog_errors::client_cert_incomplete(),
            catalog_errors::file_not_readable(),
            catalog_errors::invalid_pem(),
            catalog_errors::invalid_namespace(),
            catalog_errors::reserved_header(),
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
            catalog_errors::invalid_address().code(),
            error_codes::CONFIG_INVALID_ADDRESS
        );
        assert_eq!(error_codes::CONFIG_INVALID_ADDRESS, "BV-CONFIG-001");
    }

    #[test]
    fn details_carry_structured_context() {
        let error = catalog_errors::invalid_setting_value().with_detail("setting", "Timeout");
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
        let error = catalog_errors::invalid_address().with_status_code(503);
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
