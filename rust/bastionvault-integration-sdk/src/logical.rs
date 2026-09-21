//! The logical layer (D-M1b-1/6/7/9/10/11/12/24): `Logical.Read/Write/Delete/List/Raw`,
//! URL construction, envelope parsing, and the single retry loop every operation shares.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::Duration;

use serde_json::{Map, Value};

use crate::client::Client;
use crate::config::{ApiPrefix, ClientConfig};
use crate::error::catalog_errors::{input_body_too_large, input_unsupported_option, protocol_unexpected_response};
use crate::error::Error;
use crate::logger::ClientLogger;
use crate::mapping::status_to_code;
use crate::observer::RequestEvent;
use crate::secret::SecretString;
use crate::transport::{RequestOptions, TransportRequest, TransportResponse};

/// Login paths never carry the token header (TRN-015): `auth/<method>/login` or
/// `auth/<method>/login/<user>`.
fn is_login_path(path: &str) -> bool {
    let segments: Vec<&str> = path.split('/').filter(|segment| !segment.is_empty()).collect();
    segments.len() >= 3 && segments.len() <= 4 && segments[0] == "auth" && segments[2] == "login"
}

/// TRN-020: percent-encodes one path segment.
fn encode_path_segment(segment: &str) -> String {
    let mut out = String::with_capacity(segment.len());
    for byte in segment.bytes() {
        let must_encode = byte < 0x20
            || byte == 0x7f
            || byte >= 0x80
            || matches!(
                byte,
                b' ' | b'"' | b'#' | b'%' | b'/' | b'<' | b'>' | b'?' | b'`' | b'\\' | b'^' | b'{' | b'}' | b'|' | b'[' | b']'
            );
        if must_encode {
            out.push('%');
            out.push_str(&format!("{byte:02X}"));
        } else {
            out.push(byte as char);
        }
    }
    out
}

/// TRN-021: percent-encodes one query key or value (does not encode `/`; additionally
/// encodes `&`, `+`, `=`).
fn encode_query_component(component: &str) -> String {
    let mut out = String::with_capacity(component.len());
    for byte in component.bytes() {
        let must_encode = byte < 0x20
            || byte == 0x7f
            || byte >= 0x80
            || matches!(
                byte,
                b' ' | b'"' | b'#' | b'%' | b'<' | b'>' | b'?' | b'`' | b'\\' | b'^' | b'{' | b'}' | b'|' | b'[' | b']' | b'&' | b'+' | b'='
            );
        if must_encode {
            out.push('%');
            out.push_str(&format!("{byte:02X}"));
        } else {
            out.push(byte as char);
        }
    }
    out
}

fn encode_path(path: &str) -> String {
    path.split('/').map(encode_path_segment).collect::<Vec<_>>().join("/")
}

fn encode_query(query: &str) -> String {
    query
        .split('&')
        .map(|pair| match pair.split_once('=') {
            Some((key, value)) => format!(
                "{}={}",
                encode_query_component(key),
                encode_query_component(value)
            ),
            None => encode_query_component(pair),
        })
        .collect::<Vec<_>>()
        .join("&")
}

/// Splits a logical-path argument into its path and (optional, raw) query part on the
/// first `?`.
fn split_path_and_query(path_and_query: &str) -> (&str, Option<&str>) {
    match path_and_query.split_once('?') {
        Some((path, query)) => (path, Some(query)),
        None => (path_and_query, None),
    }
}

/// TRN-002: builds the absolute, encoded URL for a logical (non-`Raw`) operation.
fn build_logical_url(address: &str, prefix: ApiPrefix, path_and_query: &str) -> String {
    let (path, query) = split_path_and_query(path_and_query);
    let stripped = path.trim_start_matches('/');
    let prefix_str = match prefix {
        ApiPrefix::V1 => "v1",
        ApiPrefix::V2 => "v2",
    };
    let mut url = format!(
        "{}/{}/{}",
        address.trim_end_matches('/'),
        prefix_str,
        encode_path(stripped)
    );
    if let Some(query) = query {
        url.push('?');
        url.push_str(&encode_query(query));
    }
    url
}

/// D-M1b-12: `Logical.Raw` takes an absolute path and adds no prefix.
fn build_raw_url(address: &str, absolute_path: &str) -> String {
    let path = if absolute_path.starts_with('/') {
        absolute_path.to_owned()
    } else {
        format!("/{absolute_path}")
    };
    format!("{}{}", address.trim_end_matches('/'), path)
}

/// The five reserved header names (TRN-012), matched case-insensitively, that never
/// leave this crate's control regardless of `Headers`/`RequestOptions.Headers`
/// (validated already at construction, CFG-017 — this is the transport-side mirror
/// that governs what is actually sent).
const NEVER_SENT_HEADERS: [&str; 4] = ["x-vault-token", "authorization", "cookie", "x-vault-namespace"];

/// TRN-032: the server body limit is 32 MiB; larger bodies are rejected client-side.
const MAX_REQUEST_BODY_BYTES: usize = 32 * 1024 * 1024;

/// TRN-017/CFG-060: `WrapTtl` has no server implementation yet and MUST raise
/// `BV-INPUT-006` rather than silently ignore the option.
fn validate_request_options(body: &Option<Vec<u8>>, options: &RequestOptions) -> Result<(), Error> {
    if options.wrap_ttl.is_some() {
        return Err(input_unsupported_option());
    }
    if let Some(body) = body {
        if body.len() > MAX_REQUEST_BODY_BYTES {
            return Err(input_body_too_large());
        }
    }
    Ok(())
}

fn effective_namespace(client: &Client, options: &RequestOptions) -> String {
    options
        .namespace
        .clone()
        .unwrap_or_else(|| client.effective_namespace().to_owned())
}

/// **D-M2-9's seam.** The token for one pass, resolved **through the source** rather than
/// read from a field, and resolved exactly **once** — here, above the retry loop.
///
/// The placement is unchanged and deliberately so: D-M1b-9 put the snapshot here, and that
/// is `CFG-070`'s "in-flight requests keep the token they started with". Only the *source*
/// of the value changed.
///
/// `None` means no token is to be sent. Distinguishing "resolved to absent" from "not
/// resolved yet" is what `ERR-022`'s preflight turns on, and that preflight is M2b's — so
/// M2a sends no token rather than refusing, which is the absence of an unimplemented
/// requirement and not a guess at one (D-M1c-25).
///
/// A failure is classified by [`crate::token_source`] in the order D-M2-18 item 1 pins,
/// and is finished here with the request-scoped fields so it reaches the caller as a coded
/// `Error` — `ERR-020`/`TRN-054` forbid an untyped escape, and D-M2-9's own ruling (moving
/// resolution above the loop and making it perform I/O) is what created the path that could
/// have produced one.
async fn resolve_token_for(
    client: &Client,
    raw_path: &str,
    options: &RequestOptions,
    method: &str,
    display_path: &str,
    execution: &RequestExecution,
) -> Result<Option<SecretString>, Error> {
    if let Some(explicit) = &options.token {
        return Ok(Some(explicit.clone()));
    }
    // CFG-020's first MUST (D-M1c-24): a login carries no token header. Resolution is
    // skipped entirely rather than resolved-and-discarded, so a `Login` source does not
    // recurse into a login in order to send one. The *raw* path, never the display path:
    // the login pattern is anchored and would not match through the `[ns=…] ` prefix.
    let (path_only, _) = split_path_and_query(raw_path);
    if is_login_path(path_only.trim_start_matches('/')) {
        return Ok(None);
    }
    client.resolve_token().await.map_err(|error| {
        // `attempts` is `attempts_before` — none on a first pass — because no request was
        // issued on this pass either way, matching the cancellation arm.
        finish_error(
            error,
            method,
            display_path,
            client.config(),
            &effective_namespace(client, options),
            execution.attempts_before,
        )
    })
}

fn build_headers(
    client: &Client,
    options: &RequestOptions,
    path: &str,
    has_body: bool,
    token: Option<&SecretString>,
) -> Vec<(String, String)> {
    let mut headers: Vec<(String, String)> = Vec::new();
    headers.push(("Accept".to_owned(), "application/json".to_owned()));
    if has_body {
        headers.push(("Content-Type".to_owned(), "application/json".to_owned()));
    }
    headers.push(("User-Agent".to_owned(), client.config().user_agent().to_owned()));

    let omit_token = is_login_path(path) && options.token.is_none();
    if !omit_token {
        if let Some(explicit) = &options.token {
            headers.push(("X-BastionVault-Token".to_owned(), explicit.reveal().to_owned()));
        } else if let Some(token) = token {
            if !token.reveal().is_empty() {
                headers.push(("X-BastionVault-Token".to_owned(), token.reveal().to_owned()));
            }
        }
    }

    let namespace = effective_namespace(client, options);
    let namespace = namespace.trim_matches('/');
    if !namespace.is_empty() {
        headers.push(("X-BastionVault-Namespace".to_owned(), namespace.to_owned()));
    }

    for (name, value) in client.config().headers() {
        if !NEVER_SENT_HEADERS.contains(&name.to_ascii_lowercase().as_str()) {
            headers.push((name.clone(), value.clone()));
        }
    }
    if let Some(extra) = &options.headers {
        for (name, value) in extra {
            if !NEVER_SENT_HEADERS.contains(&name.to_ascii_lowercase().as_str()) {
                headers.push((name.clone(), value.clone()));
            }
        }
    }
    headers
}

/// The auth block (TRN's `Auth` object). Only these five fields are ever emitted by
/// the server (section 03); an SDK that wants more calls `auth/token/lookup-self`.
#[derive(Debug, Clone)]
pub struct AuthInfo {
    pub client_token: SecretString,
    pub policies: Vec<String>,
    pub metadata: HashMap<String, String>,
    pub lease_duration: Duration,
    pub renewable: bool,
}

/// The canonical logical-operation result (TRN-040..043). `raw` retains the exact
/// parsed body for diagnostics (TRN-043); fields absent on the wire are absent here,
/// never fabricated (TRN-042).
#[derive(Debug, Clone)]
pub struct Response {
    pub data: Option<Map<String, Value>>,
    pub auth: Option<AuthInfo>,
    pub lease_id: Option<String>,
    pub renewable: Option<bool>,
    pub lease_duration: Option<Duration>,
    pub warnings: Vec<String>,
    pub status_code: u16,
    pub headers: HashMap<String, String>,
    pub raw: Value,
}

/// `Logical.Raw`'s result (D-M1b-12): no envelope parsing, the body is unparsed bytes.
#[derive(Debug, Clone)]
pub struct RawResponse {
    pub status_code: u16,
    pub headers: HashMap<String, String>,
    pub body: Vec<u8>,
}

/// The four HTTP-level logical primitives, plus the `Raw` escape hatch (TRN-001).
/// Borrows the `Client` it was produced from; `client.logical()` is the only
/// constructor (D-M1b public API table).
pub struct Logical<'a> {
    pub(crate) client: &'a Client,
}

/// Idempotency table (D-M1b-6/CFG-051): the default when `RequestOptions.Idempotent`
/// is unset.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum LogicalOp {
    Read,
    Write,
    Delete,
    List,
    Raw,
}

fn default_idempotent(op: LogicalOp, raw_method: Option<&str>) -> bool {
    match op {
        LogicalOp::Read | LogicalOp::List => true,
        LogicalOp::Write | LogicalOp::Delete => false,
        LogicalOp::Raw => matches!(
            raw_method.unwrap_or_default().to_ascii_uppercase().as_str(),
            "GET" | "HEAD" | "OPTIONS" | "LIST"
        ),
    }
}

static REQUEST_ID_COUNTER: AtomicU64 = AtomicU64::new(1);

fn next_request_id() -> String {
    let value = REQUEST_ID_COUNTER.fetch_add(1, Ordering::Relaxed);
    format!("req-{value:016x}")
}

/// One caller-visible operation's identity and attempt accounting, **hoisted above** the
/// retry loop (D-M1b-8 as amended by D-M2-9).
///
/// Two fields, because `attempt` used to do double duty: it drove retry eligibility and
/// backoff *and* was the count the error and the observer reported. `AUT-003`'s re-login
/// replay (M2b) is a second pass of one logical operation, so:
///
/// - `request_id` is minted **once**, by the caller, above any replay, so one
///   caller-visible operation keeps one id across both passes. Re-minting it inside the
///   loop would give the application two uncorrelated ids and two `attempt: 1` observer
///   events for one call — worse observability than it had before opting in.
/// - per-pass **`attempt`** (local to the loop) governs eligibility and backoff and resets
///   on the replay; accumulated **`attempts_total`** is what the error's `attempts` and the
///   observer report. Accumulating the eligibility counter instead would mean that at
///   `max_attempts = 3`, a first pass which burned 1–3 and ended on `BV-AUTHZ-001` enters
///   the replay at 3, `3 < 3` is false, and a transient transport failure on the replayed
///   request is **never retried** — `CFG-051`…`055` would silently not apply to the replay
///   path.
///
/// M2a lands the shape; M2b lands the pass that uses `attempts_before`.
#[derive(Debug, Clone)]
pub(crate) struct RequestExecution {
    request_id: String,
    attempts_before: u32,
}

impl RequestExecution {
    fn new() -> Self {
        Self {
            request_id: next_request_id(),
            attempts_before: 0,
        }
    }
}

/// The 256-byte sanitised snippet TRN-053 requires in `Details.snippet`.
fn snippet_of(body: &[u8]) -> String {
    let text = String::from_utf8_lossy(body);
    let truncated: String = text.chars().take(256).collect();
    truncated
        .chars()
        .map(|c| if c.is_control() { '\u{FFFD}' } else { c })
        .collect()
}

fn is_blank(body: &[u8]) -> bool {
    body.iter().all(|byte| byte.is_ascii_whitespace())
}

fn parse_retry_after(response: &TransportResponse) -> Option<Duration> {
    response
        .header("Retry-After")
        .and_then(|value| value.trim().parse::<u64>().ok())
        .map(Duration::from_secs)
}

fn parse_error_body(body: &[u8]) -> (Option<String>, Vec<String>) {
    if is_blank(body) {
        return (None, Vec::new());
    }
    match serde_json::from_slice::<Value>(body) {
        Ok(Value::Object(map)) => {
            if let Some(Value::String(message)) = map.get("error") {
                return (Some(message.clone()), Vec::new());
            }
            if let Some(Value::Array(items)) = map.get("errors") {
                let strings: Vec<String> = items
                    .iter()
                    .filter_map(|item| item.as_str().map(str::to_owned))
                    .collect();
                let joined = strings.join("; ");
                let message = if joined.is_empty() { None } else { Some(joined) };
                return (message, strings);
            }
            (None, Vec::new())
        }
        _ => (None, Vec::new()),
    }
}

/// ERR-050: current servers never emit `warnings`, but when one does the SDK surfaces
/// the list on [`Response::warnings`] and logs each entry at *warning* level through the
/// CNF-030 logger seam. Warnings are never turned into errors (D-M1c-11).
///
/// A non-string entry is kept as its raw JSON text rather than dropped: the requirement
/// is that a future server's warnings reach the caller, not that they are strings.
fn extract_warnings(parsed: &Value, logger: &dyn ClientLogger) -> Vec<String> {
    let warnings = parsed
        .as_object()
        .and_then(|map| map.get("warnings"))
        .and_then(Value::as_array)
        .map(|values| {
            values
                .iter()
                .map(|value| value.as_str().map_or_else(|| value.to_string(), str::to_owned))
                .filter(|text| !text.is_empty())
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();

    for warning in &warnings {
        logger.warn(&format!("BastionVault server warning: {warning}"));
    }
    warnings
}

/// Shape detection (TRN-040): Shape A when the body is an object with a `data` key, or
/// an `auth` object containing `client_token`; otherwise Shape B (whole body is
/// `Data`).
fn parse_response_body(
    response: &TransportResponse,
    logger: &dyn ClientLogger,
) -> Result<Option<Response>, Error> {
    if response.status == 204 {
        return Ok(None);
    }
    if is_blank(&response.body) {
        return Ok(None);
    }
    if let Some(content_type) = response.header("content-type") {
        if !content_type.to_ascii_lowercase().contains("json") {
            return Err(protocol_unexpected_response()
                .with_status_code(response.status)
                .with_detail("snippet", snippet_of(&response.body)));
        }
    }
    let parsed: Value = serde_json::from_slice(&response.body).map_err(|_| {
        protocol_unexpected_response()
            .with_status_code(response.status)
            .with_detail("snippet", snippet_of(&response.body))
    })?;

    let headers: HashMap<String, String> = response.headers.iter().cloned().collect();

    // ERR-050: `warnings` is read from the top-level envelope regardless of the TRN-040
    // shape, surfaced on `Response.warnings`, and logged at *warning* level. A warning is
    // never turned into an error (D-M1c-11).
    let warnings = extract_warnings(&parsed, logger);

    let (data, auth, lease_id, renewable, lease_duration) = match &parsed {
        Value::Object(map) => {
            let has_data_key = map.contains_key("data");
            let auth_value = map.get("auth").filter(|value| !value.is_null());
            let auth_has_client_token = auth_value
                .and_then(Value::as_object)
                .is_some_and(|auth_map| auth_map.contains_key("client_token"));

            if has_data_key || auth_has_client_token {
                let data = map.get("data").and_then(Value::as_object).cloned();
                let auth = auth_value.and_then(Value::as_object).map(|auth_map| AuthInfo {
                    client_token: SecretString::new(
                        auth_map
                            .get("client_token")
                            .and_then(Value::as_str)
                            .unwrap_or_default()
                            .to_owned(),
                    ),
                    policies: auth_map
                        .get("policies")
                        .and_then(Value::as_array)
                        .map(|values| {
                            values
                                .iter()
                                .filter_map(|value| value.as_str().map(str::to_owned))
                                .collect()
                        })
                        .unwrap_or_default(),
                    metadata: auth_map
                        .get("metadata")
                        .and_then(Value::as_object)
                        .map(|values| {
                            values
                                .iter()
                                .filter_map(|(key, value)| {
                                    value.as_str().map(|value| (key.clone(), value.to_owned()))
                                })
                                .collect()
                        })
                        .unwrap_or_default(),
                    lease_duration: Duration::from_secs(
                        auth_map.get("lease_duration").and_then(Value::as_u64).unwrap_or(0),
                    ),
                    renewable: auth_map
                        .get("renewable")
                        .and_then(Value::as_bool)
                        .unwrap_or(false),
                });
                let lease_id = map
                    .get("lease_id")
                    .and_then(Value::as_str)
                    .filter(|value| !value.is_empty())
                    .map(str::to_owned);
                let renewable = map.get("renewable").and_then(Value::as_bool);
                let lease_duration = map
                    .get("lease_duration")
                    .and_then(Value::as_u64)
                    .map(Duration::from_secs);
                (data, auth, lease_id, renewable, lease_duration)
            } else {
                (Some(map.clone()), None, None, None, None)
            }
        }
        _ => (None, None, None, None, None),
    };

    Ok(Some(Response {
        data,
        auth,
        lease_id,
        renewable,
        lease_duration,
        warnings,
        status_code: response.status,
        headers,
        raw: parsed,
    }))
}

/// The result of one execution of the shared retry loop (D-M1b-24): either a transport
/// response the caller still has to shape (204/200-empty/404-empty-on-read-list are
/// resolved as `None` immediately by the caller), or a terminal, fully-populated
/// `Error`.
enum LoopOutcome {
    Response(TransportResponse),
    NullBody,
}

/// D-M1b-24: the **one** retry loop, shared by every logical operation and by `Raw`
/// (which differs only in how its result is shaped afterwards, per D-M1b-12).
#[allow(clippy::too_many_arguments)]
async fn execute_with_retry(
    client: &Client,
    method: &str,
    display_path: &str,
    url: &str,
    headers: Vec<(String, String)>,
    body: Option<Vec<u8>>,
    idempotent: bool,
    treat_404_empty_as_null: bool,
    options: &RequestOptions,
    execution: &RequestExecution,
) -> Result<LoopOutcome, Error> {
    let config = client.config();
    let retry_policy = config.retry_policy();
    let clock = config.clock();
    let jitter = config.jitter();
    let observer = config.request_observer();
    let request_id = execution.request_id.clone();
    let max_attempts = retry_policy.max_attempts.max(1);
    let total_timeout = options.total_timeout;
    let deadline = total_timeout.map(|timeout| clock.now_monotonic() + timeout);
    let namespace = effective_namespace(client, options);
    // CFG-080 / TST-051: the observer sees the **redacted** display path. AUT-080's
    // `auth/token/renew/{token}` and `Auth.Token.Lookup`'s `auth/token/lookup/{token}` put
    // a live token in the path, and `RequestEvent.path` is the second consumer of that
    // string after the error (ERR-003 already covers the first). Redacted once, here, so
    // no call site below can pass the unredacted form — which is exactly the leak the
    // .NET pass shipped and its TST-051 instrument caught.
    let observed_path = crate::error_paths::redact(display_path);

    let mut attempt: u32 = 0;
    let mut last_error: Option<Error> = None;

    loop {
        attempt += 1;
        let attempts_total = execution.attempts_before + attempt;
        if let Some(deadline) = deadline {
            if clock.now_monotonic() >= deadline {
                break;
            }
        }

        let attempt_started = clock.now_monotonic();
        let request = TransportRequest {
            method: method.to_owned(),
            url: url.to_owned(),
            headers: headers.clone(),
            body: body.clone(),
            timeout: options.timeout.unwrap_or_else(|| config.timeout()),
            connect_timeout: config.connect_timeout(),
            max_response_bytes: config.max_response_bytes(),
        };

        let outcome = client.transport().send(request).await;
        let duration = clock.now_monotonic().saturating_duration_since(attempt_started);

        match outcome {
            Err(transport_error) => {
                observer.on_request_completed(&RequestEvent {
                    method: method.to_owned(),
                    path: observed_path.clone(),
                    namespace: namespace.clone(),
                    status_code: None,
                    duration,
                    request_id: request_id.clone(),
                    attempt: attempts_total,
                    error_code: Some(transport_error.code()),
                });
                let eligible = idempotent
                    && attempt < max_attempts
                    && retry_policy.code_is_retry_eligible(transport_error.code());
                if eligible {
                    let unit_random = jitter.next_f64();
                    let backoff = retry_policy.backoff_for_attempt(attempt, unit_random);
                    last_error = Some(transport_error);
                    clock.delay(backoff).await;
                    continue;
                }
                return Err(finish_error(
                    transport_error,
                    method,
                    display_path,
                    config,
                    &namespace,
                    attempts_total,
                ));
            }
            Ok(response) => {
                if response.status == 404 && treat_404_empty_as_null && is_blank(&response.body) {
                    observer.on_request_completed(&RequestEvent {
                        method: method.to_owned(),
                        path: observed_path.clone(),
                        namespace: namespace.clone(),
                        status_code: Some(response.status),
                        duration,
                        request_id: request_id.clone(),
                        attempt: attempts_total,
                        error_code: None,
                    });
                    return Ok(LoopOutcome::NullBody);
                }
                if matches!(response.status, 200 | 204 | 304) {
                    observer.on_request_completed(&RequestEvent {
                        method: method.to_owned(),
                        path: observed_path.clone(),
                        namespace: namespace.clone(),
                        status_code: Some(response.status),
                        duration,
                        request_id: request_id.clone(),
                        attempt: attempts_total,
                        error_code: None,
                    });
                    return Ok(LoopOutcome::Response(response));
                }

                let retry_after = parse_retry_after(&response);
                if response.status == 429 {
                    // D-M1b-22: the pause fires on any 429, driven by status alone.
                    client.pause_rate_gate(retry_after);
                }
                let (server_message, server_errors) = parse_error_body(&response.body);
                let mapped = status_to_code(
                    response.status,
                    server_message.as_deref(),
                    retry_after.is_some(),
                    display_path,
                    is_blank(&response.body),
                );
                let code = mapped.code();

                observer.on_request_completed(&RequestEvent {
                    method: method.to_owned(),
                    path: observed_path.clone(),
                    namespace: namespace.clone(),
                    status_code: Some(response.status),
                    duration,
                    request_id: request_id.clone(),
                    attempt: attempts_total,
                    error_code: Some(code),
                });

                let mut mapped = mapped
                    .with_status_code(response.status)
                    .with_retry_after(retry_after);
                if let Some(message) = server_message {
                    mapped = mapped.with_server_message(message);
                }
                if !server_errors.is_empty() {
                    mapped = mapped.with_server_errors(server_errors);
                }

                let eligible =
                    idempotent && attempt < max_attempts && retry_policy.code_is_retry_eligible(code);
                if eligible {
                    let unit_random = jitter.next_f64();
                    let backoff = retry_policy.backoff_for_attempt(attempt, unit_random);
                    let wait = retry_policy.wait_with_retry_after(backoff, retry_after);
                    last_error = Some(mapped);
                    clock.delay(wait).await;
                    continue;
                }
                return Err(finish_error(mapped, method, display_path, config, &namespace, attempts_total));
            }
        }
    }

    // The `TotalTimeout` deadline was already exceeded before this attempt could run.
    let error = last_error.unwrap_or_else(|| {
        crate::error::catalog_errors::transport_timeout().with_detail("reason", "TotalTimeout exceeded")
    });
    Err(finish_error(
        error,
        method,
        display_path,
        config,
        &namespace,
        (execution.attempts_before + attempt).saturating_sub(1).max(1),
    ))
}

/// The single place a request-scoped error becomes the error the caller sees: it fixes
/// the request-scoped fields and the attempt count, records the (ERR-003 redacted) path
/// in `Details`, interpolates it into any hint that points at `Details.path` (ERR-034),
/// and appends the ERR-040 context notes (D-M1c-5).
///
/// Enrichment lives here rather than in [`crate::mapping`] because two of the seven
/// ERR-040 rows fire on transport failures, which never reach the mapper (D-M1c-14
/// item 7).
fn finish_error(
    error: Error,
    method: &str,
    path: &str,
    config: &ClientConfig,
    active_namespace: &str,
    attempts: u32,
) -> Error {
    let error = error
        .with_method(method)
        .with_path(path)
        .with_address(config.address())
        .with_attempts(attempts);

    // `with_path` applied ERR-003, so the redacted form is the one every surface sees.
    let redacted_path = error.path().unwrap_or_default().to_owned();
    let hint = crate::enrichment::interpolate_path(error.hint(), &redacted_path);
    let hint = crate::enrichment::enrich(
        error.code(),
        &hint,
        &crate::enrichment::Context {
            status_code: error.status_code(),
            retry_after: error.retry_after(),
            path: &redacted_path,
            active_namespace,
            has_ca_certificate: !config.tls().ca_certificates.is_empty(),
            address: config.address(),
        },
    );

    error
        .with_detail("path", redacted_path)
        .with_hint(hint)
}

impl<'a> Logical<'a> {
    fn options_or_default(options: Option<RequestOptions>) -> RequestOptions {
        options.unwrap_or_default()
    }

    fn is_idempotent(&self, op: LogicalOp, raw_method: Option<&str>, options: &RequestOptions) -> bool {
        options.idempotent.unwrap_or_else(|| {
            if !self.client.config().retry_policy().retry_idempotent_only {
                true
            } else {
                default_idempotent(op, raw_method)
            }
        })
    }

    pub async fn read(&self, path: &str, options: Option<RequestOptions>) -> Result<Option<Response>, Error> {
        self.execute(LogicalOp::Read, "GET", path, None, options).await
    }

    pub async fn write(
        &self,
        path: &str,
        body: Option<Value>,
        options: Option<RequestOptions>,
    ) -> Result<Option<Response>, Error> {
        self.execute(LogicalOp::Write, "POST", path, body, options).await
    }

    pub async fn delete(
        &self,
        path: &str,
        body: Option<Value>,
        options: Option<RequestOptions>,
    ) -> Result<Option<Response>, Error> {
        self.execute(LogicalOp::Delete, "DELETE", path, body, options).await
    }

    pub async fn list(&self, path: &str, options: Option<RequestOptions>) -> Result<Option<Response>, Error> {
        self.execute(LogicalOp::List, "LIST", path, None, options).await
    }

    async fn execute(
        &self,
        op: LogicalOp,
        method: &str,
        path: &str,
        body: Option<Value>,
        options: Option<RequestOptions>,
    ) -> Result<Option<Response>, Error> {
        let options = Self::options_or_default(options);
        let idempotent = self.is_idempotent(op, None, &options);
        let treat_404_as_null = matches!(op, LogicalOp::Read | LogicalOp::List);
        self.run(method, path, body, options, idempotent, treat_404_as_null).await
    }

    /// The shared entry point every `Auth.*` operation issues its request through
    /// (D-M2-4): the same URL construction, the same resolution seam, the same retry loop
    /// and the same envelope parsing the logical layer uses. **No `Auth.*` operation builds
    /// its own HTTP path, its own error mapping, or its own recognition table.**
    ///
    /// `default_idempotent` is what `CFG-051`'s table would say for the operation, used
    /// only when the caller left `RequestOptions.idempotent` unset — the token-store
    /// operations are not in [`LogicalOp`]'s table and must not be forced into it.
    pub(crate) async fn execute_shaped(
        &self,
        method: &str,
        path: &str,
        body: Option<Value>,
        options: Option<RequestOptions>,
        default_idempotent: bool,
        treat_404_empty_as_null: bool,
    ) -> Result<Option<Response>, Error> {
        let options = Self::options_or_default(options);
        let idempotent = options.idempotent.unwrap_or_else(|| {
            if self.client.config().retry_policy().retry_idempotent_only {
                default_idempotent
            } else {
                true
            }
        });
        self.run(method, path, body, options, idempotent, treat_404_empty_as_null)
            .await
    }

    /// One logical operation: mint its identity, resolve its token once, then run the
    /// shared loop.
    async fn run(
        &self,
        method: &str,
        path: &str,
        body: Option<Value>,
        options: RequestOptions,
        idempotent: bool,
        treat_404_as_null: bool,
    ) -> Result<Option<Response>, Error> {
        let url = build_logical_url(self.client.config().address(), self.client.config().api_prefix(), path);
        let body_bytes = body
            .as_ref()
            .map(|value| serde_json::to_vec(value).unwrap_or_default());
        validate_request_options(&body_bytes, &options)?;
        let display_path = display_path(&effective_namespace(self.client, &options), path);
        let execution = RequestExecution::new();
        let token = resolve_token_for(self.client, path, &options, method, &display_path, &execution).await?;
        let headers = build_headers(self.client, &options, path, body_bytes.is_some(), token.as_ref());

        match execute_with_retry(
            self.client,
            method,
            &display_path,
            &url,
            headers,
            body_bytes,
            idempotent,
            treat_404_as_null,
            &options,
            &execution,
        )
        .await?
        {
            LoopOutcome::NullBody => Ok(None),
            LoopOutcome::Response(response) => {
                parse_response_body(&response, self.client.config().logger().as_ref())
            }
        }
    }

    pub async fn raw(
        &self,
        method: &str,
        absolute_path: &str,
        body: Option<Value>,
        options: Option<RequestOptions>,
    ) -> Result<RawResponse, Error> {
        let options = Self::options_or_default(options);
        let idempotent = self.is_idempotent(LogicalOp::Raw, Some(method), &options);
        let url = build_raw_url(self.client.config().address(), absolute_path);
        let body_bytes = body
            .as_ref()
            .map(|value| serde_json::to_vec(value).unwrap_or_default());
        validate_request_options(&body_bytes, &options)?;
        let display_path = display_path(&effective_namespace(self.client, &options), absolute_path);
        let execution = RequestExecution::new();
        let token =
            resolve_token_for(self.client, absolute_path, &options, method, &display_path, &execution).await?;
        let headers = build_headers(self.client, &options, absolute_path, body_bytes.is_some(), token.as_ref());

        match execute_with_retry(
            self.client,
            method,
            &display_path,
            &url,
            headers,
            body_bytes,
            idempotent,
            false,
            &options,
            &execution,
        )
        .await?
        {
            LoopOutcome::NullBody => Ok(RawResponse {
                status_code: 204,
                headers: HashMap::new(),
                body: Vec::new(),
            }),
            LoopOutcome::Response(response) => Ok(RawResponse {
                status_code: response.status,
                headers: response.headers.into_iter().collect(),
                body: response.body,
            }),
        }
    }
}

/// TRN-054/ERR-001's `Path`: the logical path with the namespace prefix for display
/// (`[ns=dti/esi] secret/data/x`).
fn display_path(namespace: &str, path: &str) -> String {
    if namespace.is_empty() {
        path.to_owned()
    } else {
        format!("[ns={namespace}] {path}")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::transport::{FakeTransport, TransportFailureKind};
    use crate::{ClientConfigBuilder, EnvironmentSource, RetryPolicy};
    use std::sync::Arc;

    fn client_with(transport: Arc<FakeTransport>, retry: RetryPolicy) -> Client {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("https://vault.example.test:8200")
            .token("s.token")
            .transport(transport)
            .retry_policy(retry)
            .build()
            .expect("valid config");
        Client::new(config).expect("valid client")
    }

    fn one_shot_policy() -> RetryPolicy {
        RetryPolicy {
            max_attempts: 1,
            ..RetryPolicy::default()
        }
    }

    #[tokio::test]
    async fn delete_reaches_the_real_delete_path_d_m1b_6() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 204,
            headers: Vec::new(),
            body: Vec::new(),
        });
        let client = client_with(transport.clone(), one_shot_policy());
        let result = client.logical().delete("secret/data/x", None, None).await.expect("must succeed");
        assert!(result.is_none());
        assert_eq!(transport.recorded_requests()[0].method, "DELETE");
    }

    #[tokio::test]
    async fn raw_returns_a_204_as_an_empty_raw_response_d_m1b_12() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 204,
            headers: Vec::new(),
            body: Vec::new(),
        });
        let client = client_with(transport, one_shot_policy());
        let raw = client
            .logical()
            .raw("DELETE", "/v1/secret/data/x", None, None)
            .await
            .expect("must succeed");
        assert_eq!(raw.status_code, 204);
        assert!(raw.body.is_empty());
    }

    #[tokio::test]
    async fn wrap_ttl_is_rejected_as_unsupported_trn_017() {
        let transport = Arc::new(FakeTransport::new());
        let client = client_with(transport, one_shot_policy());
        let options = RequestOptions {
            wrap_ttl: Some(Duration::from_secs(60)),
            ..RequestOptions::default()
        };
        let error = client
            .logical()
            .read("secret/data/x", Some(options))
            .await
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-INPUT-006");
    }

    #[tokio::test]
    async fn oversized_body_is_rejected_client_side_trn_032() {
        let transport = Arc::new(FakeTransport::new());
        let client = client_with(transport, one_shot_policy());
        let huge = "x".repeat(MAX_REQUEST_BODY_BYTES + 1);
        let error = client
            .logical()
            .write("secret/data/x", Some(Value::String(huge)), None)
            .await
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-INPUT-007");
    }

    #[tokio::test]
    async fn retry_idempotent_only_false_makes_a_write_eligible_for_retry_d_m1b_7() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_failure(TransportFailureKind::ConnectionRefused);
        transport.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: br#"{"data":{"a":1}}"#.to_vec(),
        });
        let policy = RetryPolicy {
            max_attempts: 2,
            initial_backoff: Duration::ZERO,
            retry_idempotent_only: false,
            ..RetryPolicy::default()
        };
        let client = client_with(transport, policy);
        let response = client
            .logical()
            .write("secret/data/x", Some(serde_json::json!({"a": 1})), None)
            .await
            .expect("must succeed after one retry")
            .expect("must have data");
        assert_eq!(response.data.unwrap().get("a").unwrap(), 1);
    }

    #[tokio::test]
    async fn a_retryable_status_code_is_retried_then_succeeds_cfg_051() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 503,
            headers: Vec::new(),
            body: br#"{"error":"cluster has no leader"}"#.to_vec(),
        });
        transport.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: br#"{"data":{"a":1}}"#.to_vec(),
        });
        let policy = RetryPolicy {
            max_attempts: 2,
            initial_backoff: Duration::ZERO,
            ..RetryPolicy::default()
        };
        let client = client_with(transport, policy);
        let response = client
            .logical()
            .read("secret/data/x", None)
            .await
            .expect("must succeed after one retry")
            .expect("must have data");
        assert_eq!(response.data.unwrap().get("a").unwrap(), 1);
    }

    #[tokio::test]
    async fn total_timeout_already_elapsed_yields_a_terminal_error() {
        let transport = Arc::new(FakeTransport::new());
        let client = client_with(transport, one_shot_policy());
        let options = RequestOptions {
            total_timeout: Some(Duration::ZERO),
            ..RequestOptions::default()
        };
        // Give the clock a moment to move past a zero-length deadline.
        tokio::time::sleep(Duration::from_millis(1)).await;
        let error = client
            .logical()
            .read("secret/data/x", Some(options))
            .await
            .expect_err("must fail");
        assert_eq!(error.attempts(), 1);
    }

    #[tokio::test]
    async fn shape_b_non_object_body_is_returned_as_absent_data() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: b"[1,2,3]".to_vec(),
        });
        let client = client_with(transport, one_shot_policy());
        let response = client
            .logical()
            .read("sys/x", None)
            .await
            .expect("must succeed")
            .expect("must have a response");
        assert!(response.data.is_none());
    }

    #[tokio::test]
    async fn warnings_and_policies_are_parsed_when_present() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: br#"{
                "data": {"a": 1},
                "warnings": ["careful"],
                "auth": {
                    "client_token": "s.child",
                    "policies": ["default", "app"],
                    "metadata": {"username": "alice"},
                    "lease_duration": 60,
                    "renewable": true
                }
            }"#
            .to_vec(),
        });
        let client = client_with(transport, one_shot_policy());
        let response = client
            .logical()
            .read("auth/userpass/login/alice", None)
            .await
            .expect("must succeed")
            .expect("must have a response");
        assert_eq!(response.warnings, vec!["careful".to_owned()]);
        let auth = response.auth.expect("auth must be present");
        assert_eq!(auth.policies, vec!["default".to_owned(), "app".to_owned()]);
        assert_eq!(auth.metadata.get("username").map(String::as_str), Some("alice"));
        assert!(auth.renewable);
    }

    #[tokio::test]
    async fn an_explicit_per_call_token_overrides_the_client_token() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: b"{}".to_vec(),
        });
        let client = client_with(transport.clone(), one_shot_policy());
        let options = RequestOptions {
            token: Some(SecretString::new("s.explicit")),
            ..RequestOptions::default()
        };
        client.logical().read("secret/data/x", Some(options)).await.expect("must succeed");
        let recorded = transport.recorded_requests();
        let token_header = recorded[0].header("X-BastionVault-Token").expect("token header present");
        assert_eq!(token_header, "s.explicit");
    }

    #[tokio::test]
    async fn a_reserved_header_in_per_call_options_is_filtered_out() {
        let transport = Arc::new(FakeTransport::new());
        transport.script_response(TransportResponse {
            status: 200,
            headers: Vec::new(),
            body: b"{}".to_vec(),
        });
        let client = client_with(transport.clone(), one_shot_policy());
        let mut extra = HashMap::new();
        extra.insert("Authorization".to_owned(), "should-be-dropped".to_owned());
        extra.insert("X-Custom".to_owned(), "kept".to_owned());
        let options = RequestOptions {
            headers: Some(extra),
            ..RequestOptions::default()
        };
        client.logical().read("secret/data/x", Some(options)).await.expect("must succeed");
        let recorded = transport.recorded_requests();
        assert!(recorded[0].header("Authorization").is_none());
        assert_eq!(recorded[0].header("X-Custom"), Some("kept"));
    }

    #[test]
    fn login_path_detection_matches_trn_015() {
        assert!(is_login_path("auth/userpass/login"));
        assert!(is_login_path("auth/userpass/login/alice"));
        assert!(!is_login_path("auth/userpass/lookup-self"));
        assert!(!is_login_path("secret/data/login"));
    }

    #[test]
    fn path_segments_are_percent_encoded_per_trn_020() {
        assert_eq!(encode_path_segment("db 01"), "db%2001");
        assert_eq!(encode_path_segment("a/b"), "a%2Fb");
        assert_eq!(encode_path_segment("plain"), "plain");
    }

    #[test]
    fn query_components_are_percent_encoded_per_trn_021_and_slash_is_preserved() {
        assert_eq!(encode_query_component("us west"), "us%20west");
        assert_eq!(encode_query_component("a&b"), "a%26b");
        assert_eq!(encode_query_component("a/b"), "a/b");
    }

    #[test]
    fn build_logical_url_matches_the_encoding_vectors_fixture() {
        let url = build_logical_url(
            "https://vault.example.com:8200",
            ApiPrefix::V1,
            "secret/data/db 01?env=us west&version=2",
        );
        assert_eq!(
            url,
            "https://vault.example.com:8200/v1/secret/data/db%2001?env=us%20west&version=2"
        );
    }

    #[test]
    fn build_logical_url_preserves_trailing_slash_for_list_trn_011() {
        let url = build_logical_url(
            "https://vault.example.com:8200",
            ApiPrefix::V1,
            "secret/metadata/app/",
        );
        assert_eq!(url, "https://vault.example.com:8200/v1/secret/metadata/app/");
    }

    #[test]
    fn build_raw_url_adds_no_prefix_d_m1b_12() {
        let url = build_raw_url("https://vault.example.com:8200", "/v1/secret/data/x");
        assert_eq!(url, "https://vault.example.com:8200/v1/secret/data/x");
    }

    #[test]
    fn default_idempotency_matches_the_d_m1b_6_table() {
        assert!(default_idempotent(LogicalOp::Read, None));
        assert!(default_idempotent(LogicalOp::List, None));
        assert!(!default_idempotent(LogicalOp::Write, None));
        assert!(!default_idempotent(LogicalOp::Delete, None));
        assert!(default_idempotent(LogicalOp::Raw, Some("GET")));
        assert!(default_idempotent(LogicalOp::Raw, Some("LIST")));
        assert!(!default_idempotent(LogicalOp::Raw, Some("POST")));
    }

    #[test]
    fn snippet_is_bounded_to_256_chars_and_strips_control_characters() {
        let body = format!("{}\u{1}", "x".repeat(300));
        let snippet = snippet_of(body.as_bytes());
        assert_eq!(snippet.chars().count(), 256);
        assert!(!snippet.contains('\u{1}'));
    }

    #[test]
    fn error_body_parsing_handles_all_three_trn_052_shapes() {
        let (message, errors) = parse_error_body(br#"{"error":"boom"}"#);
        assert_eq!(message.as_deref(), Some("boom"));
        assert!(errors.is_empty());

        let (message, errors) = parse_error_body(br#"{"errors":["a","b"]}"#);
        assert_eq!(message.as_deref(), Some("a; b"));
        assert_eq!(errors, vec!["a".to_owned(), "b".to_owned()]);

        let (message, errors) = parse_error_body(b"");
        assert_eq!(message, None);
        assert!(errors.is_empty());
    }

    #[test]
    fn display_path_includes_namespace_when_present() {
        assert_eq!(display_path("", "secret/data/x"), "secret/data/x");
        assert_eq!(display_path("dti/esi", "secret/data/x"), "[ns=dti/esi] secret/data/x");
    }
}
