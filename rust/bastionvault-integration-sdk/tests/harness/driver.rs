use std::collections::{BTreeMap, BTreeSet};
use std::fmt::{Display, Formatter};
use std::time::Duration;

use serde_json::{Map, Value};

use std::sync::Arc;

use super::fixture::{Fixture, Operation};
use super::instruments::{secrets, CapturingLogger, CapturingObserver, FixtureClock};
use super::transport::FakeTransport;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RequestSnapshot {
    pub method: String,
    pub url: String,
    pub headers: BTreeMap<String, String>,
    pub body: Value,
}

impl RequestSnapshot {
    pub fn new<I, K, V>(
        method: impl Into<String>,
        url: impl Into<String>,
        headers: I,
        body: Value,
    ) -> Self
    where
        I: IntoIterator<Item = (K, V)>,
        K: AsRef<str>,
        V: AsRef<str>,
    {
        Self {
            method: method.into(),
            url: url.into(),
            headers: headers
                .into_iter()
                .map(|(name, value)| (normalize_header(name.as_ref()), value.as_ref().to_owned()))
                .collect(),
            body,
        }
    }
}

pub fn compare_request(
    expected: &Value,
    actual: &RequestSnapshot,
    strict_headers: bool,
) -> Result<(), String> {
    let object = expected
        .as_object()
        .ok_or_else(|| "request expectation must be an object".to_owned())?;
    let mut failures = Vec::new();
    if let Some(method) = object.get("method").and_then(Value::as_str)
        && method != actual.method
    {
        failures.push(format!(
            "method expected {method:?}, got {:?}",
            actual.method
        ));
    }
    if let Some(url) = object.get("url").and_then(Value::as_str)
        && url != actual.url
    {
        failures.push(format!("url expected {url:?}, got {:?}", actual.url));
    }
    if let Some(headers) = object.get("headers").and_then(Value::as_object) {
        for (name, expected_value) in headers {
            let normalized = normalize_header(name);
            match (expected_value.as_str(), actual.headers.get(&normalized)) {
                (Some(expected_value), Some(actual_value)) if expected_value == actual_value => {}
                (Some(expected_value), Some(actual_value)) => failures.push(format!(
                    "header {name} expected {expected_value:?}, got {actual_value:?}"
                )),
                (Some(expected_value), None) => failures.push(format!(
                    "header {name} expected {expected_value:?}, but it was absent"
                )),
                (None, _) => failures.push(format!("header {name} expectation must be a string")),
            }
        }
        if strict_headers {
            let expected_names = headers
                .keys()
                .map(|name| normalize_header(name))
                .collect::<BTreeSet<_>>();
            for name in actual
                .headers
                .keys()
                .filter(|name| !expected_names.contains(*name))
            {
                failures.push(format!("unlisted header {name} present in strict mode"));
            }
        }
    }
    if let Some(absent_headers) = object.get("absentHeaders").and_then(Value::as_array) {
        for name in absent_headers.iter().filter_map(Value::as_str) {
            if actual.headers.contains_key(&normalize_header(name)) {
                failures.push(format!("absent header {name} was present"));
            }
        }
    }
    if let Some(body) = object.get("body") {
        if canonical_json(body) != canonical_json(&actual.body) {
            failures.push(format!("body expected {body}, got {}", actual.body));
        }
    }
    if failures.is_empty() {
        Ok(())
    } else {
        Err(failures.join("; "))
    }
}

#[derive(Debug, Clone, PartialEq)]
pub enum ActualValue {
    Null,
    Bool(bool),
    Number(serde_json::Number),
    String(String),
    Array(Vec<ActualValue>),
    Object(BTreeMap<String, ActualValue>),
    Redacted(RedactedValue),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RedactedValue {
    original: String,
    rendered: String,
}

impl Display for RedactedValue {
    fn fmt(&self, formatter: &mut Formatter<'_>) -> std::fmt::Result {
        formatter.write_str(&self.rendered)
    }
}

impl ActualValue {
    pub fn null() -> Self {
        Self::Null
    }

    pub fn boolean(value: bool) -> Self {
        Self::Bool(value)
    }

    pub fn number(value: i64) -> Self {
        Self::Number(serde_json::Number::from(value))
    }

    pub fn string(value: impl Into<String>) -> Self {
        Self::String(value.into())
    }

    pub fn array<I>(values: I) -> Self
    where
        I: IntoIterator<Item = ActualValue>,
    {
        Self::Array(values.into_iter().collect())
    }

    pub fn object<I, K>(values: I) -> Self
    where
        I: IntoIterator<Item = (K, ActualValue)>,
        K: Into<String>,
    {
        Self::Object(
            values
                .into_iter()
                .map(|(key, value)| (key.into(), value))
                .collect(),
        )
    }

    pub fn redacted(value: impl Into<String>) -> Self {
        Self::Redacted(RedactedValue {
            original: value.into(),
            rendered: "[REDACTED]".to_owned(),
        })
    }

    pub fn from_json(value: &Value) -> Self {
        match value {
            Value::Null => Self::Null,
            Value::Bool(value) => Self::Bool(*value),
            Value::Number(value) => Self::Number(value.clone()),
            Value::String(value) => Self::String(value.clone()),
            Value::Array(values) => Self::Array(values.iter().map(Self::from_json).collect()),
            Value::Object(values) => Self::Object(
                values
                    .iter()
                    .map(|(key, value)| (key.clone(), Self::from_json(value)))
                    .collect(),
            ),
        }
    }
}

pub fn compare_result(expected: &Value, actual: &ActualValue) -> Result<(), String> {
    let mut failures = Vec::new();
    compare_result_at(expected, Some(actual), "$", &mut failures);
    if failures.is_empty() {
        Ok(())
    } else {
        Err(failures.join("; "))
    }
}

fn compare_result_at(
    expected: &Value,
    actual: Option<&ActualValue>,
    path: &str,
    failures: &mut Vec<String>,
) {
    if let Some(sentinel) = expected.as_str() {
        match sentinel {
            "$absent" => {
                if !matches!(actual, None | Some(ActualValue::Null)) {
                    failures.push(format!("{path} expected absent or null"));
                }
            }
            "$any" => {
                if actual.is_none() {
                    failures.push(format!("{path} expected a present value"));
                }
            }
            "$redacted" => match actual {
                Some(ActualValue::Redacted(value))
                    if !value
                        .rendered
                        .to_ascii_lowercase()
                        .contains(&value.original.to_ascii_lowercase()) => {}
                Some(ActualValue::Redacted(_)) => {
                    failures.push(format!("{path} redacted value leaked"))
                }
                _ => failures.push(format!("{path} expected a redacting value")),
            },
            _ => compare_plain_value(expected, actual, path, failures),
        }
        return;
    }
    compare_plain_value(expected, actual, path, failures);
}

fn compare_plain_value(
    expected: &Value,
    actual: Option<&ActualValue>,
    path: &str,
    failures: &mut Vec<String>,
) {
    match (expected, actual) {
        (Value::Object(expected), Some(ActualValue::Object(actual))) => {
            for (key, value) in expected {
                compare_result_at(value, actual.get(key), &format!("{path}.{key}"), failures);
            }
        }
        (Value::Array(expected), Some(ActualValue::Array(actual))) => {
            if expected.len() != actual.len() {
                failures.push(format!(
                    "{path} expected {} array items, got {}",
                    expected.len(),
                    actual.len()
                ));
            }
            for (index, value) in expected.iter().enumerate() {
                compare_result_at(
                    value,
                    actual.get(index),
                    &format!("{path}[{index}]"),
                    failures,
                );
            }
        }
        (Value::Null, Some(ActualValue::Null)) => {}
        (Value::Bool(expected), Some(ActualValue::Bool(actual))) if expected == actual => {}
        (Value::Number(expected), Some(ActualValue::Number(actual))) if expected == actual => {}
        (Value::String(expected), Some(ActualValue::String(actual))) if expected == actual => {}
        (_, Some(ActualValue::Redacted(_))) => failures.push(format!(
            "{path} ordinary comparison received redacted value"
        )),
        (_, None) => failures.push(format!("{path} expected {expected}, but it was absent")),
        (_, Some(actual)) => failures.push(format!("{path} expected {expected}, got {actual:?}")),
    }
}

#[derive(Debug, Clone, Default, PartialEq)]
pub struct ActualError {
    pub code: String,
    pub status_code: Option<i64>,
    pub retryable: Option<bool>,
    pub attempts: Option<i64>,
    pub retry_after: Option<i64>,
    pub details: BTreeMap<String, Value>,
    pub hint: Option<String>,
    pub server_message: Option<String>,
    /// The three surfaces `TST-051` searches that no comparison rule reads: the catalogue
    /// message, `ERR-001`'s redacted `Path`, and `ERR-002`'s one-line rendering. Projected
    /// in one place so no operation family can forget one.
    pub message: Option<String>,
    pub path: Option<String>,
    pub rendered: Option<String>,
}

impl ActualError {
    pub fn new(code: impl Into<String>) -> Self {
        Self {
            code: code.into(),
            ..Self::default()
        }
    }

    pub fn status_code(mut self, value: Option<i64>) -> Self {
        self.status_code = value;
        self
    }

    pub fn retryable(mut self, value: bool) -> Self {
        self.retryable = Some(value);
        self
    }

    pub fn attempts(mut self, value: i64) -> Self {
        self.attempts = Some(value);
        self
    }

    pub fn retry_after(mut self, value: Option<i64>) -> Self {
        self.retry_after = value;
        self
    }

    pub fn detail(mut self, key: impl Into<String>, value: Value) -> Self {
        self.details.insert(key.into(), value);
        self
    }

    pub fn hint(mut self, value: impl Into<String>) -> Self {
        self.hint = Some(value.into());
        self
    }

    pub fn server_message(mut self, value: impl Into<String>) -> Self {
        self.server_message = Some(value.into());
        self
    }
}

pub fn compare_error(expected: &Value, actual: &ActualError) -> Result<(), String> {
    let object = expected
        .as_object()
        .ok_or_else(|| "error expectation must be an object".to_owned())?;
    let mut failures = Vec::new();
    if let Some(expected_code) = object.get("code").and_then(Value::as_str)
        && expected_code != actual.code
    {
        failures.push(format!(
            "code expected {expected_code:?}, got {:?}",
            actual.code
        ));
    }
    compare_optional_i64(object, "statusCode", actual.status_code, &mut failures);
    if let Some(expected_retryable) = object.get("retryable").and_then(Value::as_bool)
        && actual.retryable != Some(expected_retryable)
    {
        failures.push(format!(
            "retryable expected {expected_retryable}, got {:?}",
            actual.retryable
        ));
    }
    compare_optional_i64(object, "attempts", actual.attempts, &mut failures);
    compare_optional_i64(object, "retryAfter", actual.retry_after, &mut failures);
    if let Some(keys) = object.get("detailsKeys").and_then(Value::as_array) {
        for key in keys.iter().filter_map(Value::as_str) {
            if !actual.details.contains_key(key) {
                failures.push(format!("detailsKeys missing {key}"));
            }
        }
    }
    if let Some(hints) = object.get("hintContains").and_then(Value::as_array) {
        let actual_hint = actual
            .hint
            .as_deref()
            .unwrap_or_default()
            .to_ascii_lowercase();
        for hint in hints.iter().filter_map(Value::as_str) {
            if !actual_hint.contains(&hint.to_ascii_lowercase()) {
                failures.push(format!("hintContains missing {hint:?}"));
            }
        }
    }
    if let Some(expected_message) = object.get("serverMessage").and_then(Value::as_str)
        && actual.server_message.as_deref() != Some(expected_message)
    {
        failures.push(format!(
            "serverMessage expected {expected_message:?}, got {:?}",
            actual.server_message
        ));
    }
    if failures.is_empty() {
        Ok(())
    } else {
        Err(failures.join("; "))
    }
}

fn compare_optional_i64(
    object: &Map<String, Value>,
    field: &str,
    actual: Option<i64>,
    failures: &mut Vec<String>,
) {
    if let Some(expected) = object.get(field) {
        let expected_value = expected.as_i64();
        if expected_value != actual {
            failures.push(format!("{field} expected {expected}, got {actual:?}"));
        }
    }
}

fn normalize_header(name: &str) -> String {
    name.to_ascii_lowercase()
}

fn canonical_json(value: &Value) -> String {
    match value {
        Value::Null | Value::Bool(_) | Value::Number(_) | Value::String(_) => value.to_string(),
        Value::Array(values) => format!(
            "[{}]",
            values
                .iter()
                .map(canonical_json)
                .collect::<Vec<_>>()
                .join(",")
        ),
        Value::Object(values) => {
            let mut entries = values
                .iter()
                .map(|(key, value)| (key, canonical_json(value)))
                .collect::<Vec<_>>();
            entries.sort_by(|left, right| left.0.cmp(right.0));
            format!(
                "{{{}}}",
                entries
                    .into_iter()
                    .map(|(key, value)| format!(
                        "{}:{value}",
                        serde_json::to_string(key).unwrap_or_default()
                    ))
                    .collect::<Vec<_>>()
                    .join(",")
            )
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DriverConfig {
    pub address: Option<String>,
    pub token: Option<String>,
    pub namespace: Option<String>,
    pub api_prefix: Option<String>,
    pub cluster_discovery: Option<bool>,
    pub settings: Option<Value>,
    pub environment: BTreeMap<String, String>,
}

pub fn configure(fixture: &Fixture) -> DriverConfig {
    let client = fixture.client.as_ref();
    DriverConfig {
        address: client.and_then(|config| config.address.clone()),
        token: client.and_then(|config| config.token.clone()),
        namespace: client.and_then(|config| config.namespace.clone()),
        api_prefix: client.and_then(|config| config.api_prefix.clone()),
        cluster_discovery: client.and_then(|config| config.cluster_discovery),
        settings: client.and_then(|config| config.settings.clone()),
        environment: fixture.environment.clone().unwrap_or_default(),
    }
}

/// D-M2-7's instruments for one fixture run: the injected clock, the capturing logger and
/// the capturing `RequestObserver`.
///
/// Attached to **every** fixture run in every language, not opted into per fixture, because
/// `TST-051` is a whole-run assertion.
#[derive(Debug)]
pub struct Instruments {
    pub clock: Arc<FixtureClock>,
    pub logger: Arc<CapturingLogger>,
    pub observer: Arc<CapturingObserver>,
}

impl Instruments {
    pub fn for_fixture(fixture: &Fixture) -> Result<Self, String> {
        Ok(Self {
            clock: Arc::new(FixtureClock::from_fixture(fixture)?),
            logger: Arc::new(CapturingLogger::default()),
            observer: Arc::new(CapturingObserver::default()),
        })
    }

    /// Everything one fixture run surfaced: log lines, observer events, and the error as the
    /// caller would see it. Searched by `TST-051`.
    fn surfaced(&self, result: &Result<ActualValue, ActualError>) -> Vec<String> {
        let mut haystacks = self.logger.lines();
        for event in self.observer.events() {
            // The event's own `Debug` renders every member, so a member added later is
            // searched without this list being updated.
            haystacks.push(format!("{event:?}"));
            haystacks.push(event.path.clone());
        }
        if let Err(error) = result {
            haystacks.push(error.code.clone());
            haystacks.push(error.hint.clone().unwrap_or_default());
            haystacks.push(error.server_message.clone().unwrap_or_default());
            haystacks.push(error.rendered.clone().unwrap_or_default());
            haystacks.push(error.message.clone().unwrap_or_default());
            haystacks.push(error.path.clone().unwrap_or_default());
            for (key, value) in &error.details {
                haystacks.push(key.clone());
                haystacks.push(value.to_string());
            }
        }
        haystacks
    }
}

/// A registered operation resolves to a *handler* (D-M1a-12), matching the shape .NET
/// and Python's registries already had at M0: `configure()` the fixture's client/env,
/// hand the handler a `FakeTransport`, and let it produce a language-neutral result or
/// error the harness can compare against `fixture.expect`.
pub type OperationHandler =
    fn(&DriverConfig, &Instruments, &mut FakeTransport, &Operation) -> Result<ActualValue, ActualError>;

#[derive(Debug, Clone)]
pub enum OperationResolution {
    Registered(OperationHandler),
    Pending { operation: String },
}

impl OperationResolution {
    pub fn is_pending(&self) -> bool {
        matches!(self, Self::Pending { .. })
    }
}

#[derive(Debug, Clone, Default)]
pub struct OperationRegistry {
    handlers: BTreeMap<String, OperationHandler>,
}

impl OperationRegistry {
    pub fn empty() -> Self {
        Self::default()
    }

    /// The registry M1a actually exercises fixtures with: every operation the Rust SDK
    /// has real code for yet. Kept separate from `empty()` so the M0 "everything is
    /// pending" tests, which construct their own registry, are undisturbed.
    pub fn m1a() -> Self {
        let mut registry = Self::empty();
        registry.register("Client.Construct", operations::client_construct);
        registry
    }

    /// D-M1b: `m1a()` plus the five logical primitives (TRN-001), driven through the
    /// SDK's own public `FakeTransport` (D-M1b-15) — not a second implementation.
    pub fn m1b() -> Self {
        let mut registry = Self::m1a();
        registry.register("Logical.Read", operations::logical_read);
        registry.register("Logical.Write", operations::logical_write);
        registry.register("Logical.Delete", operations::logical_delete);
        registry.register("Logical.List", operations::logical_list);
        registry.register("Logical.Raw", operations::logical_raw);
        registry
    }

    /// D-M2-6: `m1b()` plus the eleven `Auth.*` operations M2a lands (`AUT-020`,
    /// `AUT-080`…`AUT-085`), driven through the SDK's own public `FakeTransport` and the
    /// same executor the logical layer uses (D-M2-4).
    pub fn m2a() -> Self {
        let mut registry = Self::m1b();
        registry.register("Auth.Token.Use", operations::auth_token_use);
        registry.register("Auth.Token.Verify", operations::auth_token_verify);
        registry.register("Auth.Token.Create", operations::auth_token_create);
        registry.register("Auth.Token.Lookup", operations::auth_token_lookup);
        registry.register("Auth.Token.LookupSelf", operations::auth_token_lookup_self);
        registry.register("Auth.Token.Renew", operations::auth_token_renew);
        registry.register("Auth.Token.RenewSelf", operations::auth_token_renew_self);
        registry.register("Auth.Token.Revoke", operations::auth_token_revoke);
        registry.register("Auth.Token.RevokeOrphan", operations::auth_token_revoke_orphan);
        registry.register("Auth.Token.RevokeSelf", operations::auth_token_revoke_self);
        registry.register("Auth.Token.AuditLogin", operations::auth_token_audit_login);
        registry
    }

    pub fn register(&mut self, operation: impl Into<String>, handler: OperationHandler) {
        self.handlers.insert(operation.into(), handler);
    }

    pub fn resolve(&self, operation: &str) -> OperationResolution {
        match self.handlers.get(operation) {
            Some(handler) => OperationResolution::Registered(*handler),
            None => OperationResolution::Pending {
                operation: operation.to_owned(),
            },
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub enum RunOutcome {
    Pending { operation: String },
    Ran { result: Result<ActualValue, ActualError> },
}

#[derive(Debug, Clone, Default)]
pub struct FixtureDriver {
    registry: OperationRegistry,
}

impl FixtureDriver {
    pub fn new() -> Self {
        Self {
            registry: OperationRegistry::empty(),
        }
    }

    pub fn with_registry(registry: OperationRegistry) -> Self {
        Self { registry }
    }

    /// Runs one fixture with D-M2-7's two instruments attached, and enforces both of them
    /// on the way out:
    ///
    /// 1. **The clock must have been read** when the fixture declares one. A fixture that
    ///    declares a `clock` a driver ignores must *fail*, not pass — which is exactly how
    ///    `auth.token.lookup-self-remaining-ttl` passed vacuously from M0 to M2a.
    /// 2. **No fixture secret may appear in anything the SDK surfaced** (`TST-051`).
    ///
    /// Both are returned as driver errors rather than as fixture mismatches, so every
    /// caller of `run` gets them without opting in.
    pub fn run(&self, fixture: &Fixture) -> Result<RunOutcome, String> {
        let configuration = configure(fixture);
        let instruments = Instruments::for_fixture(fixture)?;
        let mut transport = FakeTransport::from_fixture(fixture)?;
        let handler = match self.registry.resolve(&fixture.operation.name) {
            OperationResolution::Pending { operation } => {
                return Ok(RunOutcome::Pending { operation });
            }
            OperationResolution::Registered(handler) => handler,
        };
        let result = handler(&configuration, &instruments, &mut transport, &fixture.operation);

        assert_clock_was_honoured(fixture, &instruments.clock)?;
        assert_waits_matched(fixture, &instruments.clock)?;
        assert_no_harness_failure(&instruments.clock)?;
        secrets::assert_no_leak(&fixture.id, &fixture.document, &instruments.surfaced(&result))?;

        Ok(RunOutcome::Ran { result })
    }

    pub fn registry(&self) -> &OperationRegistry {
        &self.registry
    }
}

/// Real handlers for the operations the Rust SDK can already resolve fixtures against
/// (D-M1a-12). `Client.Construct` is the only one M1a adds: it runs the fixture's
/// `client`/`environment` block through the real `ClientConfigBuilder` and reports
/// either the client's observable state or the `Error` construction raised, so
/// `transport.headers.reserved-rejected` exercises real SDK code rather than a test
/// shim (D-M1a-6).
mod operations {
    use bastionvault_integration_sdk::{
        ApiPrefix, AuthInfo, Client, ClientConfigBuilder, CreateTokenRequest, DetailValue,
        EnvironmentSource, Error, FakeTransport as SdkFakeTransport, Response, SecretString,
        TokenInfo, Transport, TransportFailureKind as SdkFailureKind,
        TransportResponse as SdkTransportResponse,
    };
    use std::sync::Arc;
    use std::time::Duration;

    use super::super::instruments::{ClockAdvancingTransport, FixtureJitter, SequenceJitter};
    use super::super::transport::{ScriptedOutcome, TransportFailure};
    use super::{ActualError, ActualValue, DriverConfig, FakeTransport, Instruments, Operation};
    use std::collections::{BTreeMap, HashMap};
    use serde_json::Value;

    /// D-M1b-15: converts the harness's already-parsed fixture script (the schema
    /// decoding belongs to `harness::transport`) into the SDK's own public
    /// `FakeTransport`, which is the object real `Client`/`Logical` code actually runs
    /// against — not a second implementation beside it.
    fn sdk_transport_from(transport: &mut FakeTransport) -> Arc<SdkFakeTransport> {
        let sdk = Arc::new(SdkFakeTransport::new());
        loop {
            match transport.next() {
                Ok(ScriptedOutcome::Respond(response)) => {
                    let body = match (&response.body, &response.raw_body) {
                        (Some(json), _) => serde_json::to_vec(json).unwrap_or_default(),
                        (None, Some(raw)) => raw.clone().into_bytes(),
                        (None, None) => Vec::new(),
                    };
                    sdk.script_response(SdkTransportResponse {
                        status: response.status,
                        headers: response.headers.clone(),
                        body,
                    });
                }
                Ok(ScriptedOutcome::Fail(failure)) => {
                    sdk.script_failure(convert_failure(failure));
                }
                Err(TransportFailure::ScriptExhausted) => break,
                Err(_) => break,
            }
        }
        sdk
    }

    fn convert_failure(failure: TransportFailure) -> SdkFailureKind {
        match failure {
            TransportFailure::ConnectionRefused => SdkFailureKind::ConnectionRefused,
            TransportFailure::Timeout => SdkFailureKind::Timeout,
            TransportFailure::TlsVerify => SdkFailureKind::TlsVerify,
            TransportFailure::TlsHandshake => SdkFailureKind::TlsHandshake,
            TransportFailure::Reset => SdkFailureKind::Reset,
            TransportFailure::Dns => SdkFailureKind::Dns,
            TransportFailure::ScriptExhausted => SdkFailureKind::ConnectionRefused,
        }
    }

    /// Builds the real `Client` a fixture runs against, with D-M2-7's instruments attached:
    /// the fixture clock, the capturing logger and the capturing `RequestObserver`.
    fn build_client(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: Arc<SdkFakeTransport>,
    ) -> Result<Client, ActualError> {
        let transport: Arc<dyn Transport> = Arc::new(ClockAdvancingTransport::new(
            transport,
            Arc::clone(&instruments.clock),
        ));
        let mut builder = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::Map(config.environment.clone()))
            .transport(transport)
            .clock(Arc::clone(&instruments.clock) as Arc<dyn bastionvault_integration_sdk::Clock>)
            .logger(Arc::clone(&instruments.logger) as Arc<dyn bastionvault_integration_sdk::ClientLogger>)
            .request_observer(
                Arc::clone(&instruments.observer) as Arc<dyn bastionvault_integration_sdk::RequestObserver>
            );
        if let Some(address) = &config.address {
            builder = builder.address(address.clone());
        }
        if let Some(token) = &config.token {
            builder = builder.token(token.clone());
        }
        if let Some(namespace) = &config.namespace {
            builder = builder.namespace(namespace.clone());
        }
        if let Some(api_prefix) = &config.api_prefix {
            builder = builder.api_prefix(if api_prefix == "v2" { ApiPrefix::V2 } else { ApiPrefix::V1 });
        }
        if let Some(cluster_discovery) = config.cluster_discovery {
            builder = builder.cluster_discovery(cluster_discovery);
        }
        if let Some(settings) = &config.settings {
            if let Some(headers) = settings.get("Headers").and_then(|value| value.as_object()) {
                let pairs = headers
                    .iter()
                    .map(|(key, value)| (key.clone(), value.as_str().unwrap_or_default().to_owned()))
                    .collect::<Vec<_>>();
                builder = builder.headers(pairs);
            }
            if let Some(retry) = settings.get("RetryPolicy").and_then(|value| value.as_object()) {
                let mut policy = bastionvault_integration_sdk::RetryPolicy::default();
                if let Some(value) = retry.get("MaxAttempts").and_then(|v| v.as_u64()) {
                    policy.max_attempts = value as u32;
                }
                if let Some(value) = retry.get("InitialBackoff").and_then(|v| v.as_str()) {
                    if let Some(seconds) = parse_iso8601_duration_seconds(value) {
                        policy.initial_backoff = std::time::Duration::from_secs_f64(seconds);
                    }
                }
                // RES-003's other three operands. Omitting them left `MaxBackoff` at the
                // 5 s default, so a fixture asserting the clip got an unclipped wait and
                // `resilience.backoff.math-seeded` proved nothing about the formula.
                if let Some(value) = retry.get("MaxBackoff").and_then(|v| v.as_str()) {
                    if let Some(seconds) = parse_iso8601_duration_seconds(value) {
                        policy.max_backoff = std::time::Duration::from_secs_f64(seconds);
                    }
                }
                if let Some(value) = retry.get("BackoffMultiplier").and_then(|v| v.as_f64()) {
                    policy.backoff_multiplier = value;
                }
                if let Some(value) = retry.get("Jitter").and_then(|v| v.as_f64()) {
                    policy.jitter = value;
                }
                builder = builder.retry_policy(policy);
            }
            // The jitter source is always deterministic under a fixture: `settings.__jitter`
            // when the fixture seeds one, the midpoint otherwise. The SDK's own default is
            // seeded from the system clock, which is why the granted waits drifted by a
            // fraction of a millisecond run to run and no fixture could assert them.
            let jitter_values = settings
                .get("__jitter")
                .and_then(|value| value.get("values"))
                .and_then(|value| value.as_array())
                .map(|values| values.iter().filter_map(|value| value.as_f64()).collect::<Vec<_>>());
            let jitter: Arc<dyn bastionvault_integration_sdk::JitterSource> = match jitter_values {
                Some(values) => Arc::new(SequenceJitter::new(values)),
                None => Arc::new(FixtureJitter),
            };
            builder = builder.jitter(jitter);
            if let Some(rate) = settings.get("RateGate").and_then(|value| value.as_object()) {
                let mut gate = bastionvault_integration_sdk::RateGate::default();
                if let Some(value) = rate.get("RatePerSecond").and_then(|v| v.as_i64()) {
                    gate.rate_per_second = value;
                }
                if let Some(value) = rate.get("Burst").and_then(|v| v.as_i64()) {
                    gate.burst = value;
                }
                builder = builder.rate_gate(gate);
            }
        }
        let client_config = builder.build().map_err(error_to_actual)?;
        Client::new(client_config).map_err(error_to_actual)
    }

    fn error_to_actual(error: Error) -> ActualError {
        let mut details = BTreeMap::new();
        for (key, value) in error.details() {
            let json_value = match value {
                DetailValue::Str(text) => serde_json::Value::String(text.clone()),
                DetailValue::Int(number) => serde_json::json!(number),
                DetailValue::Bool(flag) => serde_json::Value::Bool(*flag),
                // D-M1c-4's `keys` capture is an ordered list; the harness compares
                // against the fixture's JSON, so it stays a JSON array here.
                DetailValue::List(items) => serde_json::json!(items),
            };
            details.insert(key.clone(), json_value);
        }
        ActualError {
            code: error.code().to_owned(),
            status_code: error.status_code().map(i64::from),
            retryable: Some(error.retryable()),
            attempts: Some(i64::from(error.attempts())),
            retry_after: error.retry_after().map(|duration| duration.as_secs() as i64),
            details,
            hint: Some(error.hint().to_owned()),
            server_message: error.server_message().map(str::to_owned),
            // TST-051's three extra surfaces, projected once here so every operation family
            // is searched identically.
            message: Some(error.message().to_owned()),
            path: error.path().map(str::to_owned),
            rendered: Some(error.to_string()),
        }
    }

    /// A tiny ISO-8601 duration parser sufficient for the fixtures' own vocabulary
    /// (`PT0S`, `PT1.5S`, …) — not a general ISO-8601 implementation.
    fn parse_iso8601_duration_seconds(value: &str) -> Option<f64> {
        let rest = value.strip_prefix("PT")?;
        let rest = rest.strip_suffix('S')?;
        rest.parse::<f64>().ok()
    }

    fn response_to_actual(response: Option<Response>) -> ActualValue {
        match response {
            None => ActualValue::null(),
            Some(response) => {
                let data = match response.data {
                    Some(map) => ActualValue::from_json(&serde_json::Value::Object(map)),
                    None => ActualValue::null(),
                };
                let auth = match response.auth {
                    Some(auth) => ActualValue::object([
                        ("ClientToken", ActualValue::redacted(auth.client_token.reveal())),
                        (
                            "Policies",
                            ActualValue::array(auth.policies.iter().map(|policy| ActualValue::string(policy.clone()))),
                        ),
                        ("LeaseDuration", ActualValue::number(auth.lease_duration.as_secs() as i64)),
                        ("Renewable", ActualValue::boolean(auth.renewable)),
                    ]),
                    None => ActualValue::null(),
                };
                let lease_id = match response.lease_id {
                    Some(id) => ActualValue::string(id),
                    None => ActualValue::null(),
                };
                ActualValue::object([("Data", data), ("Auth", auth), ("LeaseId", lease_id)])
            }
        }
    }

    fn runtime() -> tokio::runtime::Runtime {
        tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .expect("a current-thread tokio runtime must build")
    }

    fn arg_str<'a>(operation: &'a Operation, key: &str) -> Option<&'a str> {
        operation.args.as_ref()?.get(key)?.as_str()
    }

    fn arg_body(operation: &Operation) -> Option<serde_json::Value> {
        operation.args.as_ref()?.get("body").cloned()
    }

    pub(super) fn logical_read(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let sdk_transport = sdk_transport_from(transport);
        let client = build_client(config, instruments, sdk_transport)?;
        let path = arg_str(operation, "path").unwrap_or_default().to_owned();
        runtime()
            .block_on(client.logical().read(&path, None))
            .map(response_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn logical_write(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let sdk_transport = sdk_transport_from(transport);
        let client = build_client(config, instruments, sdk_transport)?;
        let path = arg_str(operation, "path").unwrap_or_default().to_owned();
        let body = arg_body(operation);
        runtime()
            .block_on(client.logical().write(&path, body, None))
            .map(response_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn logical_delete(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let sdk_transport = sdk_transport_from(transport);
        let client = build_client(config, instruments, sdk_transport)?;
        let path = arg_str(operation, "path").unwrap_or_default().to_owned();
        let body = arg_body(operation);
        runtime()
            .block_on(client.logical().delete(&path, body, None))
            .map(response_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn logical_list(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let sdk_transport = sdk_transport_from(transport);
        let client = build_client(config, instruments, sdk_transport)?;
        let path = arg_str(operation, "path").unwrap_or_default().to_owned();
        runtime()
            .block_on(client.logical().list(&path, None))
            .map(response_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn logical_raw(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let sdk_transport = sdk_transport_from(transport);
        let client = build_client(config, instruments, sdk_transport)?;
        let method = arg_str(operation, "method").unwrap_or("GET").to_owned();
        let path = arg_str(operation, "path").unwrap_or_default().to_owned();
        let body = arg_body(operation);
        runtime()
            .block_on(client.logical().raw(&method, &path, body, None))
            .map(|raw| {
                ActualValue::object([
                    ("StatusCode", ActualValue::number(i64::from(raw.status_code))),
                ])
            })
            .map_err(error_to_actual)
    }

    pub(super) fn client_construct(
        config: &DriverConfig,
        _instruments: &Instruments,
        _transport: &mut FakeTransport,
        _operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let mut builder =
            ClientConfigBuilder::new().with_environment(EnvironmentSource::Map(config.environment.clone()));
        if let Some(address) = &config.address {
            builder = builder.address(address.clone());
        }
        if let Some(token) = &config.token {
            builder = builder.token(token.clone());
        }
        if let Some(namespace) = &config.namespace {
            builder = builder.namespace(namespace.clone());
        }
        if let Some(api_prefix) = &config.api_prefix {
            builder = builder.api_prefix(if api_prefix == "v2" {
                ApiPrefix::V2
            } else {
                ApiPrefix::V1
            });
        }
        if let Some(cluster_discovery) = config.cluster_discovery {
            builder = builder.cluster_discovery(cluster_discovery);
        }
        if let Some(settings) = &config.settings {
            if let Some(headers) = settings.get("Headers").and_then(|value| value.as_object()) {
                let pairs = headers
                    .iter()
                    .map(|(key, value)| (key.clone(), value.as_str().unwrap_or_default().to_owned()))
                    .collect::<Vec<_>>();
                builder = builder.headers(pairs);
            }
        }

        match builder.build() {
            Ok(client_config) => match Client::new(client_config) {
                Ok(client) => Ok(ActualValue::object([(
                    "isInsecure",
                    ActualValue::boolean(client.is_insecure()),
                )])),
                // One projection for every error the harness surfaces (`error_to_actual`),
                // rather than two hand-written copies that were free to omit the three
                // TST-051 fields — which is how a surface a whole-run assertion depends on
                // goes unsearched.
                Err(error) => Err(error_to_actual(error)),
            },
            Err(error) => Err(error_to_actual(error)),
        }
    }

    // ---------------------------------------------------------------------------------
    // M2a: the eleven `Auth.*` operations (D-M2-6). Every one goes through the real
    // `Client`, the real `Auth` grouping and the same executor the logical layer uses
    // (D-M2-4) — there is no second implementation of any auth path here.
    // ---------------------------------------------------------------------------------

    /// Builds the client every `Auth.*` handler runs against, from the fixture's own
    /// `client` block.
    fn auth_client(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
    ) -> Result<Client, ActualError> {
        let sdk_transport = sdk_transport_from(transport);
        build_client(config, instruments, sdk_transport)
    }

    fn arg_i64(operation: &Operation, key: &str) -> Option<i64> {
        operation.args.as_ref()?.get(key)?.as_i64()
    }

    /// `AuthInfo` projected onto the harness's language-neutral shape. `ClientToken` is
    /// `redacted`, so a fixture asserting on it must use the `$redacted` sentinel and can
    /// never assert the literal.
    fn auth_info_to_actual(auth: AuthInfo) -> ActualValue {
        ActualValue::object([
            ("ClientToken", ActualValue::redacted(auth.client_token.reveal())),
            (
                "Policies",
                ActualValue::array(auth.policies.iter().map(|policy| ActualValue::string(policy.clone()))),
            ),
            (
                "Metadata",
                ActualValue::object(
                    auth.metadata
                        .iter()
                        .map(|(key, value)| (key.clone(), ActualValue::string(value.clone()))),
                ),
            ),
            ("LeaseDuration", ActualValue::number(auth.lease_duration.as_secs() as i64)),
            ("Renewable", ActualValue::boolean(auth.renewable)),
        ])
    }

    /// `TokenInfo` projected onto the harness's shape. Durations are rendered as ISO-8601,
    /// which is the vocabulary `auth.token.lookup-self-remaining-ttl`'s `"PT1H"` uses, and
    /// `Id` is `redacted` because D-M2-12 made it a secret type.
    fn token_info_to_actual(info: TokenInfo) -> ActualValue {
        let mut fields: Vec<(String, ActualValue)> = vec![
            (
                "Id".to_owned(),
                match &info.id {
                    Some(id) => ActualValue::redacted(id.reveal()),
                    None => ActualValue::null(),
                },
            ),
            (
                "Policies".to_owned(),
                ActualValue::array(info.policies.iter().map(|policy| ActualValue::string(policy.clone()))),
            ),
            (
                "Path".to_owned(),
                info.path.clone().map_or_else(ActualValue::null, ActualValue::string),
            ),
            (
                "Meta".to_owned(),
                ActualValue::object(
                    info.meta
                        .iter()
                        .map(|(key, value)| (key.clone(), ActualValue::string(value.clone()))),
                ),
            ),
            (
                "DisplayName".to_owned(),
                info.display_name.clone().map_or_else(ActualValue::null, ActualValue::string),
            ),
            ("NumUses".to_owned(), ActualValue::number(info.num_uses)),
            ("CreationTtl".to_owned(), iso_duration(Some(info.creation_ttl))),
            ("ExplicitMaxTtl".to_owned(), iso_duration(Some(info.explicit_max_ttl))),
            ("Period".to_owned(), iso_duration(info.period)),
            ("RemainingTtl".to_owned(), iso_duration(info.remaining_ttl)),
        ];
        fields.sort_by(|left, right| left.0.cmp(&right.0));
        ActualValue::object(fields)
    }

    /// The `PT…S`/`PT…H` spelling the fixtures use for a duration (Appendix C), so
    /// `"PT1H"` compares equal to one hour rather than to `3600`.
    fn iso_duration(value: Option<Duration>) -> ActualValue {
        match value {
            None => ActualValue::null(),
            Some(duration) => {
                let seconds = duration.as_secs();
                let text = if seconds > 0 && seconds % 3600 == 0 {
                    format!("PT{}H", seconds / 3600)
                } else if seconds > 0 && seconds % 60 == 0 {
                    format!("PT{}M", seconds / 60)
                } else {
                    format!("PT{seconds}S")
                };
                ActualValue::string(text)
            }
        }
    }

    /// `Auth.CurrentToken` as a fixture can assert it: `$absent` when the client holds no
    /// token, and a `redacted` value when it does — `auth.token.revoke-self-clears-token`
    /// asserts the former.
    fn client_state(client: &Client) -> ActualValue {
        ActualValue::object([(
            "Auth.CurrentToken",
            match client.auth().current_token() {
                Some(token) => ActualValue::redacted(token.reveal()),
                None => ActualValue::null(),
            },
        )])
    }

    pub(super) fn auth_token_use(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        let token = arg_str(operation, "token").unwrap_or_default().to_owned();
        client
            .auth()
            .token()
            .r#use(SecretString::new(token))
            .map_err(error_to_actual)?;
        Ok(client_state(&client))
    }

    pub(super) fn auth_token_verify(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        _operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        runtime()
            .block_on(client.auth().token().verify(None))
            .map(token_info_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn auth_token_create(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        let request = create_request_from(operation);
        runtime()
            .block_on(client.auth().token().create(&request, None))
            .map(auth_info_to_actual)
            .map_err(error_to_actual)
    }

    /// Decodes the fixture's `request` argument onto [`CreateTokenRequest`], using the
    /// canonical (`PascalCase`) property names the fixtures spell.
    fn create_request_from(operation: &Operation) -> CreateTokenRequest {
        let Some(request) = operation.args.as_ref().and_then(|args| args.get("request")) else {
            return CreateTokenRequest::default();
        };
        CreateTokenRequest {
            policies: request.get("Policies").and_then(Value::as_array).map(|values| {
                values.iter().filter_map(|value| value.as_str().map(str::to_owned)).collect()
            }),
            ttl: request.get("Ttl").and_then(Value::as_i64).map(|seconds| Duration::from_secs(seconds as u64)),
            num_uses: request.get("NumUses").and_then(Value::as_i64),
            renewable: request.get("Renewable").and_then(Value::as_bool).unwrap_or(true),
            meta: request.get("Meta").and_then(Value::as_object).map(|values| {
                values
                    .iter()
                    .filter_map(|(key, value)| value.as_str().map(|value| (key.clone(), value.to_owned())))
                    .collect::<HashMap<String, String>>()
            }),
            display_name: request.get("DisplayName").and_then(Value::as_str).map(str::to_owned),
            use_result: request.get("UseResult").and_then(Value::as_bool).unwrap_or(false),
            ..CreateTokenRequest::default()
        }
    }

    pub(super) fn auth_token_lookup(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        let token = arg_str(operation, "token").unwrap_or_default().to_owned();
        runtime()
            .block_on(client.auth().token().lookup(&token, None))
            .map(token_info_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn auth_token_lookup_self(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        _operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        runtime()
            .block_on(client.auth().token().lookup_self(None))
            .map(token_info_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn auth_token_renew(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        let token = arg_str(operation, "token").unwrap_or_default().to_owned();
        let increment = arg_i64(operation, "increment").unwrap_or(0);
        runtime()
            .block_on(client.auth().token().renew(&token, increment, None))
            .map(auth_info_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn auth_token_renew_self(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        let increment = arg_i64(operation, "increment").unwrap_or(0);
        runtime()
            .block_on(client.auth().token().renew_self(increment, None))
            .map(auth_info_to_actual)
            .map_err(error_to_actual)
    }

    pub(super) fn auth_token_revoke(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        let token = arg_str(operation, "token").unwrap_or_default().to_owned();
        runtime()
            .block_on(client.auth().token().revoke(&token, None))
            .map_err(error_to_actual)?;
        Ok(client_state(&client))
    }

    pub(super) fn auth_token_revoke_orphan(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        let token = arg_str(operation, "token").unwrap_or_default().to_owned();
        runtime()
            .block_on(client.auth().token().revoke_orphan(&token, None))
            .map_err(error_to_actual)?;
        Ok(client_state(&client))
    }

    pub(super) fn auth_token_revoke_self(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        _operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        runtime()
            .block_on(client.auth().token().revoke_self(None))
            .map_err(error_to_actual)?;
        Ok(client_state(&client))
    }

    pub(super) fn auth_token_audit_login(
        config: &DriverConfig,
        instruments: &Instruments,
        transport: &mut FakeTransport,
        _operation: &Operation,
    ) -> Result<ActualValue, ActualError> {
        let client = auth_client(config, instruments, transport)?;
        runtime()
            .block_on(client.auth().token().audit_login(None))
            .map_err(error_to_actual)?;
        Ok(client_state(&client))
    }
}


/// D-M2-27 item 3's honour predicate, four disjuncts. The fourth is deliberate and not a
/// loophole: a fixture may legitimately grant zero waits and read the clock zero times, and
/// a three-disjunct guard would false-fail it. It costs nothing because
/// [`assert_waits_matched`] then makes a *positive* claim about the waits — `expectWaits: []`
/// asserts "nothing was granted", which is not silence.
fn assert_clock_was_honoured(fixture: &Fixture, clock: &FixtureClock) -> Result<(), String> {
    let honoured = !clock.is_scripted()
        || clock.reads() > 0
        || !clock.granted_waits().is_empty()
        || clock.expected_waits().is_some();
    if honoured {
        return Ok(());
    }
    Err(format!(
        "fixture {} declares a `clock` block that the code under test never read: the clock \
         instrument is not wired into this operation (D-M2-7)",
        fixture.id
    ))
}

/// Whenever `clock.expectWaits` is present the granted waits must match it — ordered,
/// element-wise, at ±1 ms. Independent of the honour predicate, and the reason
/// `delay: "virtual"` requires `expectWaits`: a virtual fixture with no declared waits would
/// be honoured by any incidental `now_utc()` call on the request path while asserting nothing
/// about the wait it exists to test (D-M2-27 item 3).
///
/// The tolerance is what makes the field portable: durations are authored in whole
/// milliseconds, representable exactly in .NET's 100 ns ticks, Rust's nanoseconds and
/// Python's microseconds alike, so ±1 ms absorbs all three roundings without admitting a
/// wrong schedule.
fn assert_waits_matched(fixture: &Fixture, clock: &FixtureClock) -> Result<(), String> {
    const TOLERANCE: Duration = Duration::from_millis(1);
    let Some(expected) = clock.expected_waits() else {
        return Ok(());
    };
    let granted = clock.granted_waits();
    let mut failures = Vec::new();
    if expected.len() != granted.len() {
        failures.push(format!(
            "expected {} wait(s), the operation asked for {}",
            expected.len(),
            granted.len()
        ));
    }
    for (index, (want, got)) in expected.iter().zip(granted.iter()).enumerate() {
        let difference = if want > got { *want - *got } else { *got - *want };
        if difference > TOLERANCE {
            failures.push(format!("wait[{index}]: expected {want:?}, the operation asked for {got:?}"));
        }
    }
    if failures.is_empty() {
        return Ok(());
    }
    Err(format!(
        "fixture {} declares `clock.expectWaits` and the waits it granted do not match it: {}. \
         Granted, in order: {granted:?} (D-M2-27)",
        fixture.id,
        failures.join("; ")
    ))
}

/// D-M2-27 item 2's post-run latch check: a spin-guard trip that the operation swallowed
/// still fails the run. Returning an error from `delay` alone is not a gate when the code
/// under test is required to absorb failures.
fn assert_no_harness_failure(clock: &FixtureClock) -> Result<(), String> {
    match clock.harness_failure() {
        Some(failure) => Err(format!(
            "a harness assertion failed during the run and was not surfaced by the operation: {failure}"
        )),
        None => Ok(()),
    }
}
