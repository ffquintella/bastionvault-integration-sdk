use std::collections::{BTreeMap, BTreeSet};
use std::fmt::{Display, Formatter};

use serde_json::{Map, Value};

use super::fixture::{Fixture, Operation};
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

/// A registered operation resolves to a *handler* (D-M1a-12), matching the shape .NET
/// and Python's registries already had at M0: `configure()` the fixture's client/env,
/// hand the handler a `FakeTransport`, and let it produce a language-neutral result or
/// error the harness can compare against `fixture.expect`.
pub type OperationHandler =
    fn(&DriverConfig, &mut FakeTransport, &Operation) -> Result<ActualValue, ActualError>;

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

    pub fn run(&self, fixture: &Fixture) -> Result<RunOutcome, String> {
        let configuration = configure(fixture);
        let mut transport = FakeTransport::from_fixture(fixture)?;
        match self.registry.resolve(&fixture.operation.name) {
            OperationResolution::Pending { operation } => Ok(RunOutcome::Pending { operation }),
            OperationResolution::Registered(handler) => Ok(RunOutcome::Ran {
                result: handler(&configuration, &mut transport, &fixture.operation),
            }),
        }
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
    use bastionvault_integration_sdk::{ApiPrefix, Client, ClientConfigBuilder, EnvironmentSource};

    use super::{ActualError, ActualValue, DriverConfig, FakeTransport, Operation};
    use std::collections::BTreeMap;

    pub(super) fn client_construct(
        config: &DriverConfig,
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
            Ok(client_config) => {
                let client = Client::new(client_config);
                Ok(ActualValue::object([(
                    "isInsecure",
                    ActualValue::boolean(client.is_insecure()),
                )]))
            }
            Err(error) => Err(ActualError {
                code: error.code().to_owned(),
                status_code: error.status_code().map(i64::from),
                retryable: Some(error.retryable()),
                attempts: Some(i64::from(error.attempts())),
                retry_after: None,
                details: BTreeMap::new(),
                hint: Some(error.hint().to_owned()),
                server_message: error.server_message().map(str::to_owned),
            }),
        }
    }
}
