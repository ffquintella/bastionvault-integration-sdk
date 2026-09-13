//! Client configuration: the mutable [`ClientConfigBuilder`] input and the immutable,
//! fully-resolved [`ClientConfig`] output (D-M1a-2).
//!
//! Resolution ([`ClientConfigBuilder::build`]) runs the settings table
//! (`specifications/02-client-configuration.md`) exactly once, in its declared order
//! (D-M1a-4), then validates in the fixed order of D-M1a-5, stopping at the first
//! failure.

use std::collections::HashMap;
use std::fmt;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use crate::env::EnvironmentSource;
use crate::error::config_errors::{
    client_cert_incomplete, file_not_readable, insecure_http_not_allowed, invalid_address,
    invalid_namespace, invalid_pem, invalid_setting_value, reserved_header,
};
use crate::error::Error;
use crate::logger::{default_logger, ClientLogger};
use crate::parse::{parse_bool, parse_duration, parse_int};
use crate::pem;
use crate::rate::RateGate;
use crate::retry::RetryPolicy;
use crate::tls::{ClientCertificate, TlsParameters};
use crate::transport::Transport;

pub(crate) const DEFAULT_ADDRESS: &str = "https://127.0.0.1:8200";

/// Reserved header names (TRN-012/CFG-017), matched case-insensitively.
const RESERVED_HEADERS: [&str; 7] = [
    "x-bastionvault-token",
    "x-vault-token",
    "authorization",
    "cookie",
    "x-bastionvault-namespace",
    "host",
    "content-length",
];

/// `ApiPrefix` — the default logical-operation prefix.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ApiPrefix {
    #[default]
    V1,
    V2,
}

/// Background token renewal settings (D-M1a-13). Materialised, disabled by default; the
/// renewal loop is M2.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct AutoRenew {
    pub enabled: bool,
}

fn default_token_file() -> PathBuf {
    default_token_file_with_home(std::env::var_os("HOME").or_else(|| std::env::var_os("USERPROFILE")))
}

/// Split out from [`default_token_file`] so the "no home directory known" fallback is
/// testable without mutating the real process environment (D-M1a-16 permits reading the
/// home directory outside `EnvironmentSource`, but a test still must not touch it).
fn default_token_file_with_home(home: Option<std::ffi::OsString>) -> PathBuf {
    match home {
        Some(home) => PathBuf::from(home).join(".vault-token"),
        None => PathBuf::from(".vault-token"),
    }
}

fn default_user_agent() -> String {
    format!("bastionvault-sdk-rust/{}", env!("CARGO_PKG_VERSION"))
}

/// The mutable options object an application fills in (canonical "options input",
/// D-M1a-2). `ClientConfigBuilder::build` resolves and validates it exactly once,
/// producing an immutable [`ClientConfig`] (CFG-002).
#[derive(Clone)]
pub struct ClientConfigBuilder {
    environment: EnvironmentSource,
    address: Option<String>,
    token: Option<String>,
    token_file: Option<PathBuf>,
    use_token_helper: Option<bool>,
    namespace: Option<String>,
    ca_cert_path: Option<PathBuf>,
    ca_cert_pem: Option<String>,
    ca_cert_replaces_system_roots: Option<bool>,
    client_cert_path: Option<PathBuf>,
    client_key_path: Option<PathBuf>,
    tls_skip_verify: Option<bool>,
    tls_server_name: Option<String>,
    allow_insecure_http: Option<bool>,
    timeout: Option<Duration>,
    connect_timeout: Option<Duration>,
    retry_policy: Option<RetryPolicy>,
    rate_gate: Option<RateGate>,
    cluster_discovery: Option<bool>,
    discovery_probe_timeout: Option<Duration>,
    headers: Option<HashMap<String, String>>,
    user_agent: Option<String>,
    api_prefix: Option<ApiPrefix>,
    auto_renew: Option<AutoRenew>,
    logger: Option<Arc<dyn ClientLogger>>,
    transport: Option<Arc<dyn Transport>>,
}

impl fmt::Debug for ClientConfigBuilder {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("ClientConfigBuilder")
            .field("address", &self.address)
            .field("token", &self.token.as_ref().map(|_| "[REDACTED]"))
            .field("namespace", &self.namespace)
            .finish_non_exhaustive()
    }
}

impl Default for ClientConfigBuilder {
    fn default() -> Self {
        Self {
            environment: EnvironmentSource::Process,
            address: None,
            token: None,
            token_file: None,
            use_token_helper: None,
            namespace: None,
            ca_cert_path: None,
            ca_cert_pem: None,
            ca_cert_replaces_system_roots: None,
            client_cert_path: None,
            client_key_path: None,
            tls_skip_verify: None,
            tls_server_name: None,
            allow_insecure_http: None,
            timeout: None,
            connect_timeout: None,
            retry_policy: None,
            rate_gate: None,
            cluster_discovery: None,
            discovery_probe_timeout: None,
            headers: None,
            user_agent: None,
            api_prefix: None,
            auto_renew: None,
            logger: None,
            transport: None,
        }
    }
}

impl ClientConfigBuilder {
    /// A builder whose primary resolution reads the real process environment
    /// (CFG-005: the default is env-reading; pass
    /// `.with_environment(EnvironmentSource::None)` for the env-free form).
    pub fn new() -> Self {
        Self::default()
    }

    /// Exposed for tests and for embedders that want to confirm which source a builder
    /// currently uses.
    pub fn environment_source(&self) -> &EnvironmentSource {
        &self.environment
    }

    pub fn with_environment(mut self, source: EnvironmentSource) -> Self {
        self.environment = source;
        self
    }

    pub fn address(mut self, address: impl Into<String>) -> Self {
        self.address = Some(address.into());
        self
    }

    pub fn token(mut self, token: impl Into<String>) -> Self {
        self.token = Some(token.into());
        self
    }

    pub fn token_file(mut self, path: impl Into<PathBuf>) -> Self {
        self.token_file = Some(path.into());
        self
    }

    pub fn use_token_helper(mut self, value: bool) -> Self {
        self.use_token_helper = Some(value);
        self
    }

    pub fn namespace(mut self, namespace: impl Into<String>) -> Self {
        self.namespace = Some(namespace.into());
        self
    }

    pub fn ca_cert_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.ca_cert_path = Some(path.into());
        self
    }

    pub fn ca_cert_pem(mut self, pem: impl Into<String>) -> Self {
        self.ca_cert_pem = Some(pem.into());
        self
    }

    pub fn ca_cert_replaces_system_roots(mut self, value: bool) -> Self {
        self.ca_cert_replaces_system_roots = Some(value);
        self
    }

    pub fn client_cert_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.client_cert_path = Some(path.into());
        self
    }

    pub fn client_key_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.client_key_path = Some(path.into());
        self
    }

    pub fn tls_skip_verify(mut self, value: bool) -> Self {
        self.tls_skip_verify = Some(value);
        self
    }

    pub fn tls_server_name(mut self, name: impl Into<String>) -> Self {
        self.tls_server_name = Some(name.into());
        self
    }

    pub fn allow_insecure_http(mut self, value: bool) -> Self {
        self.allow_insecure_http = Some(value);
        self
    }

    pub fn timeout(mut self, value: Duration) -> Self {
        self.timeout = Some(value);
        self
    }

    pub fn connect_timeout(mut self, value: Duration) -> Self {
        self.connect_timeout = Some(value);
        self
    }

    /// Sets the whole retry policy object (D-M1a-21). Explicit beats environment: when
    /// this is set, `BASTIONVAULT_MAX_RETRIES`/`VAULT_MAX_RETRIES` are not consulted at
    /// all (CFG-001) — there is exactly one way to set this setting.
    pub fn retry_policy(mut self, value: RetryPolicy) -> Self {
        self.retry_policy = Some(value);
        self
    }

    /// Sets the whole rate-gate object (D-M1a-21). Explicit beats environment: when
    /// this is set, `BASTIONVAULT_RATE_PER_SEC`/`BASTIONVAULT_RATE_BURST` are not
    /// consulted at all (CFG-001).
    pub fn rate_gate(mut self, value: RateGate) -> Self {
        self.rate_gate = Some(value);
        self
    }

    pub fn cluster_discovery(mut self, value: bool) -> Self {
        self.cluster_discovery = Some(value);
        self
    }

    pub fn discovery_probe_timeout(mut self, value: Duration) -> Self {
        self.discovery_probe_timeout = Some(value);
        self
    }

    /// Takes an owned copy of `headers` (collected fresh from the iterator), never a
    /// handle to caller-owned mutable state — this is what keeps
    /// `ClientConfig::headers` from aliasing the caller's map (the defect found in
    /// .NET/Python review, D-M1a addendum).
    pub fn headers(mut self, headers: impl IntoIterator<Item = (String, String)>) -> Self {
        self.headers = Some(headers.into_iter().collect());
        self
    }

    pub fn user_agent(mut self, value: impl Into<String>) -> Self {
        self.user_agent = Some(value.into());
        self
    }

    pub fn api_prefix(mut self, value: ApiPrefix) -> Self {
        self.api_prefix = Some(value);
        self
    }

    /// Sets the whole auto-renew object (D-M1a-21). There is no environment variable
    /// for this setting (D-M1a-13); the built-in default (disabled) applies whenever
    /// this is not called.
    pub fn auto_renew(mut self, value: AutoRenew) -> Self {
        self.auto_renew = Some(value);
        self
    }

    pub fn logger(mut self, logger: Arc<dyn ClientLogger>) -> Self {
        self.logger = Some(logger);
        self
    }

    pub fn transport(mut self, transport: Arc<dyn Transport>) -> Self {
        self.transport = Some(transport);
        self
    }

    /// Resolves (CFG-001..005) and validates (CFG-010..018) this builder into an
    /// immutable [`ClientConfig`]. Called exactly once; the result never re-reads the
    /// environment (CFG-002).
    pub fn build(self) -> Result<ClientConfig, Error> {
        let resolved = resolve(&self)?;
        validate(resolved)
    }
}

/// Intermediate, fully-typed values after CFG-001..005 resolution but before CFG-010..018
/// validation. Every field is populated in settings-table order; a bad value fails here
/// (`BV-CONFIG-003`) before any validation step runs (D-M1a-5, step 0).
struct Resolved {
    address_raw: String,
    token: Option<String>,
    token_file: PathBuf,
    use_token_helper: bool,
    namespace: String,
    ca_cert_path: Option<PathBuf>,
    ca_cert_pem: Option<String>,
    ca_cert_replaces_system_roots: bool,
    client_cert_path: Option<PathBuf>,
    client_key_path: Option<PathBuf>,
    tls_skip_verify: bool,
    tls_server_name: Option<String>,
    allow_insecure_http: bool,
    timeout: Duration,
    connect_timeout: Duration,
    retry_policy: RetryPolicy,
    rate_gate: RateGate,
    cluster_discovery: bool,
    discovery_probe_timeout: Duration,
    headers: HashMap<String, String>,
    user_agent: String,
    api_prefix: ApiPrefix,
    auto_renew: AutoRenew,
    logger: Arc<dyn ClientLogger>,
    transport: Option<Arc<dyn Transport>>,
}

fn read_token_file(path: &Path) -> Option<String> {
    // CFG-013's exception: TokenFile absence is "no token", never an error.
    std::fs::read_to_string(path)
        .ok()
        .map(|contents| contents.trim().to_owned())
        .filter(|value| !value.is_empty())
}

fn resolve(builder: &ClientConfigBuilder) -> Result<Resolved, Error> {
    let env = &builder.environment;

    let address_raw = builder
        .address
        .clone()
        .or_else(|| env.get_first(&["BASTIONVAULT_ADDR", "VAULT_ADDR"]))
        .unwrap_or_else(|| DEFAULT_ADDRESS.to_owned());

    let token_from_explicit_or_env = builder
        .token
        .clone()
        .or_else(|| env.get_first(&["BASTIONVAULT_TOKEN", "VAULT_TOKEN"]));

    let token_file = builder
        .token_file
        .clone()
        .or_else(|| env.get_first(&["BASTIONVAULT_TOKEN_FILE"]).map(PathBuf::from))
        .unwrap_or_else(default_token_file);

    let use_token_helper = match builder.use_token_helper {
        Some(value) => value,
        None => match env.get_first(&["BASTIONVAULT_USE_TOKEN_HELPER"]) {
            Some(raw) => parse_bool("UseTokenHelper", &raw)?,
            None => false,
        },
    };

    let token = token_from_explicit_or_env.or_else(|| {
        if use_token_helper {
            read_token_file(&token_file)
        } else {
            None
        }
    });

    let namespace_raw = builder
        .namespace
        .clone()
        .or_else(|| env.get_first(&["BASTIONVAULT_NAMESPACE", "VAULT_NAMESPACE"]))
        .unwrap_or_default();
    let namespace = namespace_raw.trim_end_matches('/').to_owned();

    let ca_cert_path = builder.ca_cert_path.clone().or_else(|| {
        env.get_first(&["BASTIONVAULT_CACERT", "VAULT_CACERT"])
            .map(PathBuf::from)
    });
    let ca_cert_pem = builder.ca_cert_pem.clone();
    let ca_cert_replaces_system_roots = builder.ca_cert_replaces_system_roots.unwrap_or(false);

    let client_cert_path = builder.client_cert_path.clone().or_else(|| {
        env.get_first(&["BASTIONVAULT_CLIENT_CERT", "VAULT_CLIENT_CERT"])
            .map(PathBuf::from)
    });
    let client_key_path = builder.client_key_path.clone().or_else(|| {
        env.get_first(&["BASTIONVAULT_CLIENT_KEY", "VAULT_CLIENT_KEY"])
            .map(PathBuf::from)
    });

    let tls_skip_verify = match builder.tls_skip_verify {
        Some(value) => value,
        None => match env.get_first(&["BASTIONVAULT_SKIP_VERIFY", "VAULT_SKIP_VERIFY"]) {
            Some(raw) => parse_bool("TlsSkipVerify", &raw)?,
            None => false,
        },
    };

    let tls_server_name = builder
        .tls_server_name
        .clone()
        .or_else(|| env.get_first(&["BASTIONVAULT_TLS_SERVER_NAME", "VAULT_TLS_SERVER_NAME"]));

    let allow_insecure_http = match builder.allow_insecure_http {
        Some(value) => value,
        None => match env.get_first(&["BASTIONVAULT_ALLOW_INSECURE_HTTP"]) {
            Some(raw) => parse_bool("AllowInsecureHttp", &raw)?,
            None => false,
        },
    };

    let timeout = match builder.timeout {
        Some(value) => value,
        None => match env.get_first(&["BASTIONVAULT_TIMEOUT", "VAULT_CLIENT_TIMEOUT"]) {
            Some(raw) => parse_duration("Timeout", &raw)?,
            None => Duration::from_secs(30),
        },
    };

    let connect_timeout = match builder.connect_timeout {
        Some(value) => value,
        None => match env.get_first(&["BASTIONVAULT_CONNECT_TIMEOUT"]) {
            Some(raw) => parse_duration("ConnectTimeout", &raw)?,
            None => Duration::from_secs(10),
        },
    };

    // D-M1a-21: `RetryPolicy` and `RateGate` are object-valued settings (CFG-001), each
    // reachable through exactly one setter. An explicit object wins outright — the
    // environment variables that would otherwise seed one field of the object are not
    // consulted at all once the caller has supplied the whole object in code.
    let retry_policy = match &builder.retry_policy {
        Some(explicit) => explicit.clone(),
        None => {
            let mut retry_policy = RetryPolicy::default();
            if let Some(raw) = env.get_first(&["BASTIONVAULT_MAX_RETRIES", "VAULT_MAX_RETRIES"]) {
                let value = parse_int("RetryPolicy.MaxAttempts", &raw)?;
                if value < 0 {
                    return Err(
                        invalid_setting_value().with_detail("setting", "RetryPolicy.MaxAttempts")
                    );
                }
                retry_policy.max_attempts = value as u32 + 1;
            }
            retry_policy
        }
    };

    let rate_gate = match builder.rate_gate {
        Some(explicit) => explicit,
        None => {
            let rate_per_second = match env.get_first(&["BASTIONVAULT_RATE_PER_SEC"]) {
                Some(raw) => parse_int("RateGate.RatePerSecond", &raw)?,
                None => 8,
            };
            if rate_per_second < 0 {
                return Err(invalid_setting_value().with_detail("setting", "RateGate.RatePerSecond"));
            }
            let rate_burst = match env.get_first(&["BASTIONVAULT_RATE_BURST"]) {
                Some(raw) => parse_int("RateGate.Burst", &raw)?,
                None => 16,
            };
            if rate_burst < 0 {
                return Err(invalid_setting_value().with_detail("setting", "RateGate.Burst"));
            }
            RateGate {
                rate_per_second,
                burst: rate_burst,
            }
        }
    };

    // D-M1a-4: NO_CLUSTER_DISCOVERY is inverted — parsed as a CFG-003 boolean and
    // negated, so `=0` leaves discovery enabled. Presence alone does not disable it.
    let cluster_discovery = match builder.cluster_discovery {
        Some(value) => value,
        None => {
            match env.get_first(&["BASTIONVAULT_NO_CLUSTER_DISCOVERY", "VAULT_NO_CLUSTER_DISCOVERY"])
            {
                Some(raw) => !parse_bool("ClusterDiscovery", &raw)?,
                None => true,
            }
        }
    };

    let discovery_probe_timeout = match builder.discovery_probe_timeout {
        Some(value) => value,
        None => match env.get_first(&["BASTIONVAULT_DISCOVERY_PROBE_TIMEOUT"]) {
            Some(raw) => parse_duration("DiscoveryProbeTimeout", &raw)?,
            None => Duration::from_millis(1500),
        },
    };

    let headers = builder.headers.clone().unwrap_or_default();
    let user_agent = builder.user_agent.clone().unwrap_or_else(default_user_agent);
    let api_prefix = builder.api_prefix.unwrap_or_default();
    let auto_renew = builder.auto_renew.unwrap_or_default();
    let logger = builder.logger.clone().unwrap_or_else(default_logger);
    let transport = builder.transport.clone();

    Ok(Resolved {
        address_raw,
        token,
        token_file,
        use_token_helper,
        namespace,
        ca_cert_path,
        ca_cert_pem,
        ca_cert_replaces_system_roots,
        client_cert_path,
        client_key_path,
        tls_skip_verify,
        tls_server_name,
        allow_insecure_http,
        timeout,
        connect_timeout,
        retry_policy,
        rate_gate,
        cluster_discovery,
        discovery_probe_timeout,
        headers,
        user_agent,
        api_prefix,
        auto_renew,
        logger,
        transport,
    })
}

fn is_loopback_host(host: &str) -> bool {
    let normalized = host.trim_start_matches('[').trim_end_matches(']');
    normalized.eq_ignore_ascii_case("127.0.0.1")
        || normalized.eq_ignore_ascii_case("::1")
        || normalized.eq_ignore_ascii_case("localhost")
}

/// The parsed shape of `Address` — either a literal node URL or a bare cluster name
/// (cluster discovery itself is M5; M1a only proves the address is well-formed).
#[derive(Debug, Clone, PartialEq)]
enum ParsedAddress {
    Url(url::Url),
    ClusterName(String),
}

fn parse_address(raw: &str) -> Result<ParsedAddress, Error> {
    if raw.trim().is_empty() {
        return Err(invalid_address());
    }
    if raw.contains("://") {
        let url = url::Url::parse(raw).map_err(|_| invalid_address())?;
        if url.scheme() != "http" && url.scheme() != "https" {
            return Err(invalid_address());
        }
        if url.host_str().is_none() {
            return Err(invalid_address());
        }
        Ok(ParsedAddress::Url(url))
    } else if raw.chars().any(|c| c.is_whitespace() || c == '/' || c.is_control()) {
        Err(invalid_address())
    } else {
        Ok(ParsedAddress::ClusterName(raw.to_owned()))
    }
}

fn validate_namespace(namespace: &str) -> Result<(), Error> {
    if namespace.starts_with('/')
        || namespace.contains("//")
        || namespace.chars().any(|c| c.is_whitespace() || c.is_control())
    {
        return Err(invalid_namespace());
    }
    Ok(())
}

fn validate_headers(headers: &HashMap<String, String>) -> Result<(), Error> {
    for name in headers.keys() {
        if RESERVED_HEADERS.contains(&name.to_ascii_lowercase().as_str()) {
            return Err(reserved_header());
        }
    }
    Ok(())
}

fn read_file_or_config_error(path: &Path) -> Result<String, Error> {
    std::fs::read_to_string(path).map_err(|cause| {
        file_not_readable()
            .with_detail("path", path.display().to_string())
            .with_cause(cause)
    })
}

fn validate(resolved: Resolved) -> Result<ClientConfig, Error> {
    // Step 1 (CFG-010): Address present, parseable, scheme http/https.
    let parsed_address = parse_address(&resolved.address_raw)?;

    // Step 2 (CFG-011/CNF-035): http:// to a non-loopback host without AllowInsecureHttp.
    if let ParsedAddress::Url(url) = &parsed_address {
        if url.scheme() == "http" && !resolved.allow_insecure_http {
            let host = url.host_str().unwrap_or_default();
            if !is_loopback_host(host) {
                return Err(insecure_http_not_allowed());
            }
        }
    }

    // Step 3 (CFG-015): Namespace well-formed (trailing '/' already stripped).
    validate_namespace(&resolved.namespace)?;

    // Step 4 (CFG-017): Headers contain no reserved name.
    validate_headers(&resolved.headers)?;

    // Step 5 (CFG-016): Timeout, ConnectTimeout > 0.
    if resolved.timeout.is_zero() {
        return Err(invalid_setting_value().with_detail("setting", "Timeout"));
    }
    if resolved.connect_timeout.is_zero() {
        return Err(invalid_setting_value().with_detail("setting", "ConnectTimeout"));
    }

    // Step 6 (CFG-012): ClientCertPath/ClientKeyPath both or neither.
    let client_cert_pair = match (&resolved.client_cert_path, &resolved.client_key_path) {
        (Some(cert), Some(key)) => Some((cert.clone(), key.clone())),
        (None, None) => None,
        _ => return Err(client_cert_incomplete()),
    };

    // Step 7 (CFG-013): referenced files readable, in order CaCertPath, ClientCertPath,
    // ClientKeyPath. CaCertPath is skipped entirely when CaCertPem is set (D-M1a-17):
    // the inline PEM wins, so the path is not a "referenced file". TokenFile is exempt.
    let ca_cert_source_text = if let Some(pem_text) = &resolved.ca_cert_pem {
        Some(pem_text.clone())
    } else if let Some(path) = &resolved.ca_cert_path {
        Some(read_file_or_config_error(path)?)
    } else {
        None
    };
    let client_cert_key_text = match &client_cert_pair {
        Some((cert_path, key_path)) => {
            let cert_text = read_file_or_config_error(cert_path)?;
            let key_text = read_file_or_config_error(key_path)?;
            Some((cert_text, key_text))
        }
        None => None,
    };

    // Step 8 (CFG-014): PEM parses, same order. An empty parse result is
    // `BV-CONFIG-006` (D-M1a-17), not success.
    let ca_certificates = match &ca_cert_source_text {
        Some(text) => {
            let certs = pem::parse_certificates(text).map_err(|_| invalid_pem())?;
            if certs.is_empty() {
                return Err(invalid_pem());
            }
            certs
        }
        None => Vec::new(),
    };
    let client_certificate = match &client_cert_key_text {
        Some((cert_text, key_text)) => {
            let cert_der = pem::parse_certificates(cert_text).map_err(|_| invalid_pem())?;
            if cert_der.is_empty() {
                return Err(invalid_pem());
            }
            let key_ders = pem::parse_private_keys(key_text).map_err(|_| invalid_pem())?;
            let key_der = key_ders.into_iter().next().ok_or_else(invalid_pem)?;
            Some(ClientCertificate {
                certificate_der: cert_der,
                private_key_der: key_der,
            })
        }
        None => None,
    };

    // Step 9 (CFG-018/CNF-030): TlsSkipVerify is a warning, not a failure.
    if resolved.tls_skip_verify {
        resolved
            .logger
            .warn("TLS certificate verification is disabled (TlsSkipVerify = true); this client is insecure.");
    }

    let address_display = match &parsed_address {
        ParsedAddress::Url(url) => url.as_str().trim_end_matches('/').to_owned(),
        ParsedAddress::ClusterName(name) => name.clone(),
    };

    Ok(ClientConfig {
        address: address_display,
        token: resolved.token.map(crate::secret::SecretString::new),
        token_file: resolved.token_file,
        use_token_helper: resolved.use_token_helper,
        namespace: resolved.namespace,
        tls: TlsParameters {
            minimum_version: crate::tls::TlsVersion::Tls12,
            offers_tls13: true,
            server_name: resolved.tls_server_name,
            ca_certificates,
            ca_replaces_system_roots: resolved.ca_cert_replaces_system_roots,
            client_certificate,
        },
        tls_skip_verify: resolved.tls_skip_verify,
        allow_insecure_http: resolved.allow_insecure_http,
        timeout: resolved.timeout,
        connect_timeout: resolved.connect_timeout,
        retry_policy: resolved.retry_policy,
        rate_gate: resolved.rate_gate,
        cluster_discovery: resolved.cluster_discovery,
        discovery_probe_timeout: resolved.discovery_probe_timeout,
        headers: resolved.headers,
        user_agent: resolved.user_agent,
        api_prefix: resolved.api_prefix,
        auto_renew: resolved.auto_renew,
        logger: resolved.logger,
        transport: resolved.transport,
    })
}

/// The immutable, fully-resolved configuration (D-M1a-2). Every default is
/// materialised — nothing is "unset". This is the only thing the rest of the SDK reads.
pub struct ClientConfig {
    address: String,
    token: Option<crate::secret::SecretString>,
    token_file: PathBuf,
    use_token_helper: bool,
    namespace: String,
    tls: TlsParameters,
    tls_skip_verify: bool,
    allow_insecure_http: bool,
    timeout: Duration,
    connect_timeout: Duration,
    retry_policy: RetryPolicy,
    rate_gate: RateGate,
    cluster_discovery: bool,
    discovery_probe_timeout: Duration,
    headers: HashMap<String, String>,
    user_agent: String,
    api_prefix: ApiPrefix,
    auto_renew: AutoRenew,
    logger: Arc<dyn ClientLogger>,
    transport: Option<Arc<dyn Transport>>,
}

impl fmt::Debug for ClientConfig {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("ClientConfig")
            .field("address", &self.address)
            .field("token", &self.token.as_ref().map(|_| "[REDACTED]"))
            .field("namespace", &self.namespace)
            .field("tls_skip_verify", &self.tls_skip_verify)
            .finish_non_exhaustive()
    }
}

impl ClientConfig {
    pub fn address(&self) -> &str {
        &self.address
    }

    pub fn token(&self) -> Option<&crate::secret::SecretString> {
        self.token.as_ref()
    }

    pub fn token_file(&self) -> &Path {
        &self.token_file
    }

    pub fn use_token_helper(&self) -> bool {
        self.use_token_helper
    }

    pub fn namespace(&self) -> &str {
        &self.namespace
    }

    pub fn tls(&self) -> &TlsParameters {
        &self.tls
    }

    pub fn tls_skip_verify(&self) -> bool {
        self.tls_skip_verify
    }

    pub fn allow_insecure_http(&self) -> bool {
        self.allow_insecure_http
    }

    pub fn timeout(&self) -> Duration {
        self.timeout
    }

    pub fn connect_timeout(&self) -> Duration {
        self.connect_timeout
    }

    pub fn retry_policy(&self) -> &RetryPolicy {
        &self.retry_policy
    }

    pub fn rate_gate(&self) -> &RateGate {
        &self.rate_gate
    }

    pub fn cluster_discovery(&self) -> bool {
        self.cluster_discovery
    }

    pub fn discovery_probe_timeout(&self) -> Duration {
        self.discovery_probe_timeout
    }

    /// An owned copy taken at resolution time (never the caller's original map): see
    /// [`ClientConfigBuilder::headers`].
    pub fn headers(&self) -> &HashMap<String, String> {
        &self.headers
    }

    pub fn user_agent(&self) -> &str {
        &self.user_agent
    }

    pub fn api_prefix(&self) -> ApiPrefix {
        self.api_prefix
    }

    pub fn auto_renew(&self) -> AutoRenew {
        self.auto_renew
    }

    pub fn logger(&self) -> &Arc<dyn ClientLogger> {
        &self.logger
    }

    pub fn transport(&self) -> Option<&Arc<dyn Transport>> {
        self.transport.as_ref()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn map_env(pairs: &[(&str, &str)]) -> EnvironmentSource {
        EnvironmentSource::Map(
            pairs
                .iter()
                .map(|(k, v)| ((*k).to_owned(), (*v).to_owned()))
                .collect(),
        )
    }

    #[test]
    fn resolves_settings_in_precedence_order_explicit_then_bastionvault_then_vault_then_default_cfg_001()
    {
        // Explicit beats environment.
        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_ADDR", "https://from-env:8200")]))
            .address("https://explicit:8200")
            .build()
            .expect("valid");
        assert_eq!(config.address(), "https://explicit:8200");

        // BASTIONVAULT_* beats VAULT_*.
        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[
                ("BASTIONVAULT_ADDR", "https://primary:8200"),
                ("VAULT_ADDR", "https://compat:8200"),
            ]))
            .build()
            .expect("valid");
        assert_eq!(config.address(), "https://primary:8200");

        // VAULT_* alone still applies.
        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[("VAULT_ADDR", "https://compat:8200")]))
            .build()
            .expect("valid");
        assert_eq!(config.address(), "https://compat:8200");

        // Absent everything falls back to the built-in default.
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        assert_eq!(config.address(), DEFAULT_ADDRESS);
    }

    #[test]
    fn token_helper_file_is_the_precedence_step_between_env_and_default_cfg_001_cfg_030() {
        let mut file = tempfile::NamedTempFile::new().expect("temp file");
        writeln!(file, "  s.from-helper-file  ").expect("write token");
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .use_token_helper(true)
            .token_file(file.path())
            .build()
            .expect("valid");
        assert_eq!(
            config.token().map(crate::secret::SecretString::reveal),
            Some("s.from-helper-file")
        );
    }

    #[test]
    fn missing_token_file_is_not_an_error_cfg_013() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .use_token_helper(true)
            .token_file("/this/path/does/not/exist/at/all")
            .build()
            .expect("absence of the token file must not fail construction");
        assert!(config.token().is_none());
    }

    #[test]
    fn environment_is_resolved_exactly_once_at_construction_cfg_002() {
        let mut map = std::collections::BTreeMap::new();
        map.insert("BASTIONVAULT_ADDR".to_owned(), "https://first:8200".to_owned());
        let config_a = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::Map(map.clone()))
            .build()
            .expect("valid");
        map.insert("BASTIONVAULT_ADDR".to_owned(), "https://second:8200".to_owned());
        // `config_a` was already resolved against the first map; building a config from
        // the *changed* map produces an independent, differently-resolved value. The
        // first `ClientConfig` never re-reads anything (it holds no `EnvironmentSource`
        // at all), which is what CFG-002 requires: env vars are read once, at
        // construction, not on every request.
        let config_b = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::Map(map))
            .build()
            .expect("valid");
        assert_eq!(config_a.address(), "https://first:8200");
        assert_eq!(config_b.address(), "https://second:8200");
    }

    #[test]
    fn malformed_boolean_raises_config_003_naming_the_setting_cfg_003() {
        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_SKIP_VERIFY", "maybe")]))
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str("TlsSkipVerify".to_owned()))
        );
    }

    #[test]
    fn malformed_duration_raises_config_003_naming_the_setting_cfg_004() {
        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_TIMEOUT", "soon")]))
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str("Timeout".to_owned()))
        );
    }

    #[test]
    fn env_ignoring_constructor_never_reads_the_environment_cfg_005() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        // Regardless of anything set in the real process environment, `None` produces
        // the built-in default.
        assert_eq!(config.address(), DEFAULT_ADDRESS);
        assert_eq!(config.namespace(), "");
    }

    #[test]
    fn default_builder_reads_the_process_environment_by_default_cfg_005() {
        assert_eq!(
            ClientConfigBuilder::new().environment_source(),
            &EnvironmentSource::Process
        );
    }

    #[test]
    fn client_cert_and_key_must_be_set_together_cfg_012() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .client_cert_path("/tmp/cert.pem")
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-004");
    }

    #[test]
    fn unreadable_ca_cert_path_raises_config_005_with_path_detail_cfg_013() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .ca_cert_path("/this/path/does/not/exist/at/all.pem")
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-005");
        assert!(error.details().contains_key("path"));
    }

    #[test]
    fn every_failure_mode_of_opening_the_file_becomes_config_005_not_a_generic_exception_cfg_013()
    {
        // A directory cannot be opened as a file: this is a different OS failure mode
        // than "not found", and must still become BV-CONFIG-005 (D-M1a addendum defect
        // #2), never a raw std::io::Error bubbling up as something else.
        let directory = tempfile::tempdir().expect("temp dir");
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .ca_cert_path(directory.path())
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-005");
        assert_eq!(
            error.details().get("path"),
            Some(&crate::error::DetailValue::Str(
                directory.path().display().to_string()
            ))
        );
    }

    #[test]
    fn malformed_pem_raises_config_006_cfg_014() {
        let mut file = tempfile::NamedTempFile::new().expect("temp file");
        writeln!(file, "not a pem file at all").expect("write");
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .ca_cert_path(file.path())
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-006");
    }

    #[test]
    fn ca_cert_pem_takes_precedence_over_ca_cert_path_and_the_path_is_never_opened_cfg_014() {
        const SAMPLE_CERT: &str =
            "-----BEGIN CERTIFICATE-----\nSGVsbG8sIHdvcmxkIQ==\n-----END CERTIFICATE-----\n";
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            // A path that does not exist: if it were opened, construction would fail.
            .ca_cert_path("/this/path/does/not/exist/at/all.pem")
            .ca_cert_pem(SAMPLE_CERT)
            .build()
            .expect("inline PEM must win; the path must not be opened");
        assert_eq!(config.tls().ca_certificates.len(), 1);
    }

    #[test]
    fn namespace_with_leading_slash_or_control_characters_is_rejected_cfg_015() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .namespace("/leading")
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-007");
    }

    #[test]
    fn trailing_slash_is_stripped_silently_cfg_015() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .namespace("team-a/")
            .build()
            .expect("valid");
        assert_eq!(config.namespace(), "team-a");
    }

    #[test]
    fn zero_timeout_raises_config_003_cfg_016() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .timeout(Duration::ZERO)
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
    }

    #[test]
    fn reserved_header_is_rejected_case_insensitively_cfg_017() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .headers([("X-Vault-Token".to_owned(), "x".to_owned())])
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-008");
        assert!(error.hint().contains("X-Vault-Token"));
    }

    #[test]
    fn headers_are_copied_and_never_alias_the_callers_map_regression() {
        let mut original = HashMap::new();
        original.insert("X-Custom".to_owned(), "one".to_owned());
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .headers(original.clone())
            .build()
            .expect("valid");
        original.insert("X-Vault-Token".to_owned(), "hijacked".to_owned());
        // Mutating the caller's original map after construction must never reach the
        // already-resolved `ClientConfig` (the aliasing defect found in .NET/Python).
        assert_eq!(config.headers().len(), 1);
        assert!(!config.headers().contains_key("X-Vault-Token"));
    }

    #[test]
    fn tls_skip_verify_succeeds_but_warns_and_marks_the_config_insecure_cfg_018_cnf_030() {
        #[derive(Debug, Default)]
        struct CapturingLogger {
            warnings: std::sync::Mutex<Vec<String>>,
        }
        impl ClientLogger for CapturingLogger {
            fn warn(&self, message: &str) {
                self.warnings.lock().unwrap().push(message.to_owned());
            }
        }
        let logger = Arc::new(CapturingLogger::default());
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .tls_skip_verify(true)
            .logger(logger.clone())
            .build()
            .expect("TlsSkipVerify must not fail construction");
        assert!(config.tls_skip_verify());
        assert_eq!(logger.warnings.lock().unwrap().len(), 1);
    }

    #[test]
    fn http_to_loopback_is_allowed_without_the_flag_cfg_010() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("http://127.0.0.1:8200")
            .build()
            .expect("loopback http must be allowed");
        assert_eq!(config.address(), "http://127.0.0.1:8200");
    }

    #[test]
    fn address_with_unsupported_scheme_is_rejected_cfg_010() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("ftp://vault.example.com")
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-001");
    }

    #[test]
    fn empty_address_is_rejected_cfg_010() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("")
            .build()
            .expect_err("must fail");
        assert_eq!(error.code(), "BV-CONFIG-001");
    }

    #[test]
    fn http_to_non_loopback_without_the_flag_is_rejected_cfg_011_cnf_035() {
        let error = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("http://127.0.0.2:8200")
            .build()
            .expect_err("127.0.0.2 is not the literal loopback exemption");
        assert_eq!(error.code(), "BV-CONFIG-002");

        let ok = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("http://vault.example.com")
            .allow_insecure_http(true)
            .build();
        assert!(ok.is_ok());
    }

    #[test]
    fn loopback_forms_are_recognised_case_insensitively_and_bracketed_cfg_011() {
        for address in [
            "http://127.0.0.1:8200",
            "http://LOCALHOST:8200",
            "http://[::1]:8200",
        ] {
            ClientConfigBuilder::new()
                .with_environment(EnvironmentSource::None)
                .address(address)
                .build()
                .unwrap_or_else(|_| panic!("{address} must be treated as loopback"));
        }
    }

    #[test]
    fn max_retries_env_sets_max_attempts_to_value_plus_one() {
        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_MAX_RETRIES", "5")]))
            .build()
            .expect("valid");
        assert_eq!(config.retry_policy().max_attempts, 6);
    }

    #[test]
    fn max_retries_failures_report_the_canonical_retry_policy_setting_name() {
        // D-M1a-18: `Details.setting` must name `RetryPolicy.MaxAttempts`, never the
        // `BASTIONVAULT_MAX_RETRIES`/`VAULT_MAX_RETRIES` env var that supplied it.
        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_MAX_RETRIES", "-1")]))
            .build()
            .expect_err("negative max retries must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str(
                "RetryPolicy.MaxAttempts".to_owned()
            ))
        );

        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_MAX_RETRIES", "not-a-number")]))
            .build()
            .expect_err("malformed max retries must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str(
                "RetryPolicy.MaxAttempts".to_owned()
            ))
        );
    }

    #[test]
    fn no_cluster_discovery_env_inverts_and_zero_leaves_it_enabled() {
        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_NO_CLUSTER_DISCOVERY", "1")]))
            .build()
            .expect("valid");
        assert!(!config.cluster_discovery());

        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_NO_CLUSTER_DISCOVERY", "0")]))
            .build()
            .expect("valid");
        assert!(config.cluster_discovery());
    }

    #[test]
    fn rate_gate_defaults_and_negative_values_are_rejected() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        assert_eq!(config.rate_gate().rate_per_second, 8);
        assert_eq!(config.rate_gate().burst, 16);

        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_RATE_PER_SEC", "-1")]))
            .build()
            .expect_err("negative rate must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        // D-M1a-18: `Details.setting` is the canonical setting name from the spec
        // table, never the environment variable that happened to supply the value —
        // a caller who set this in code through the builder never had an env var at
        // all.
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str(
                "RateGate.RatePerSecond".to_owned()
            ))
        );

        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_RATE_PER_SEC", "not-a-number")]))
            .build()
            .expect_err("malformed rate must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str(
                "RateGate.RatePerSecond".to_owned()
            ))
        );

        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_RATE_BURST", "-1")]))
            .build()
            .expect_err("negative burst must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str("RateGate.Burst".to_owned()))
        );

        let error = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_RATE_BURST", "not-a-number")]))
            .build()
            .expect_err("malformed burst must fail");
        assert_eq!(error.code(), "BV-CONFIG-003");
        assert_eq!(
            error.details().get("setting"),
            Some(&crate::error::DetailValue::Str("RateGate.Burst".to_owned()))
        );
    }

    #[test]
    fn full_settings_table_is_materialised_with_no_unset_defaults_cfg_001() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        assert_eq!(config.address(), DEFAULT_ADDRESS);
        assert!(config.token().is_none());
        assert!(config.token_file().to_string_lossy().ends_with(".vault-token"));
        assert!(!config.use_token_helper());
        assert_eq!(config.namespace(), "");
        assert!(!config.tls_skip_verify());
        assert!(!config.allow_insecure_http());
        assert_eq!(config.timeout(), Duration::from_secs(30));
        assert_eq!(config.connect_timeout(), Duration::from_secs(10));
        assert_eq!(config.retry_policy().max_attempts, 3);
        assert_eq!(config.rate_gate().rate_per_second, 8);
        assert_eq!(config.rate_gate().burst, 16);
        assert!(config.cluster_discovery());
        assert_eq!(config.discovery_probe_timeout(), Duration::from_millis(1500));
        assert!(config.headers().is_empty());
        assert!(config.user_agent().starts_with("bastionvault-sdk-rust/"));
        assert_eq!(config.api_prefix(), ApiPrefix::V1);
        assert!(!config.auto_renew().enabled);
    }

    #[test]
    fn minimum_tls_version_is_1_2_with_1_3_offered_cfg_041() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        assert_eq!(config.tls().minimum_version, crate::tls::TlsVersion::Tls12);
        assert!(config.tls().offers_tls13);
    }

    #[test]
    fn ca_cert_is_added_to_the_trust_store_unless_replace_is_set_cfg_040() {
        const SAMPLE_CERT: &str =
            "-----BEGIN CERTIFICATE-----\nSGVsbG8sIHdvcmxkIQ==\n-----END CERTIFICATE-----\n";
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .ca_cert_pem(SAMPLE_CERT)
            .build()
            .expect("valid");
        assert!(!config.tls().ca_replaces_system_roots);

        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .ca_cert_pem(SAMPLE_CERT)
            .ca_cert_replaces_system_roots(true)
            .build()
            .expect("valid");
        assert!(config.tls().ca_replaces_system_roots);
    }

    #[test]
    fn tls_server_name_is_recorded_for_sni_and_verification_cfg_042() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .tls_server_name("vault.internal")
            .build()
            .expect("valid");
        assert_eq!(config.tls().server_name.as_deref(), Some("vault.internal"));
    }

    #[test]
    fn retry_policy_defaults_are_materialised_cfg_050() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .build()
            .expect("valid");
        assert_eq!(*config.retry_policy(), RetryPolicy::default());
    }

    #[test]
    fn use_token_helper_does_not_write_a_token_file_cnf_033() {
        let directory = tempfile::tempdir().expect("temp dir");
        let token_file_path = directory.path().join("token-that-must-not-be-written");
        let _config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .use_token_helper(true)
            .token("s.explicit-token")
            .token_file(&token_file_path)
            .build()
            .expect("valid");
        assert!(
            !token_file_path.exists(),
            "M1a ships no code that writes a token; PersistToken is M2"
        );
    }

    #[test]
    fn two_client_configs_are_fully_independent_ovr_004() {
        let config_a = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_ADDR", "https://a:8200")]))
            .token("s.token-a")
            .namespace("team-a")
            .build()
            .expect("valid");
        let config_b = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_ADDR", "https://b:8200")]))
            .token("s.token-b")
            .namespace("team-b")
            .build()
            .expect("valid");
        assert_eq!(config_a.address(), "https://a:8200");
        assert_eq!(config_b.address(), "https://b:8200");
        assert_eq!(config_a.namespace(), "team-a");
        assert_eq!(config_b.namespace(), "team-b");
        assert_ne!(
            config_a.token().map(crate::secret::SecretString::reveal),
            config_b.token().map(crate::secret::SecretString::reveal)
        );
    }

    #[test]
    fn explicit_builder_values_take_precedence_for_every_remaining_setting() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .retry_policy(RetryPolicy {
                max_attempts: 3,
                ..RetryPolicy::default()
            })
            .cluster_discovery(false)
            .build()
            .expect("valid");
        assert_eq!(config.retry_policy().max_attempts, 3);
        assert!(!config.cluster_discovery());
    }

    #[test]
    fn explicit_retry_policy_object_wins_outright_over_the_environment_cfg_001_d_m1a_21() {
        // D-M1a-21: setting the whole object in code must mean the env var that would
        // otherwise seed `max_attempts` is never consulted — not even to fail on it.
        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_MAX_RETRIES", "not-a-number")]))
            .retry_policy(RetryPolicy {
                max_attempts: 9,
                ..RetryPolicy::default()
            })
            .build()
            .expect("an explicit object must bypass the malformed env var entirely");
        assert_eq!(config.retry_policy().max_attempts, 9);
    }

    #[test]
    fn explicit_rate_gate_object_wins_outright_over_the_environment_cfg_001_d_m1a_21() {
        let config = ClientConfigBuilder::new()
            .with_environment(map_env(&[("BASTIONVAULT_RATE_PER_SEC", "not-a-number")]))
            .rate_gate(RateGate {
                rate_per_second: 2,
                burst: 5,
            })
            .build()
            .expect("an explicit object must bypass the malformed env var entirely");
        assert_eq!(config.rate_gate().rate_per_second, 2);
        assert_eq!(config.rate_gate().burst, 5);
    }

    #[test]
    fn explicit_auto_renew_object_is_materialised_cfg_001_d_m1a_21() {
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .auto_renew(AutoRenew { enabled: true })
            .build()
            .expect("valid");
        assert!(config.auto_renew().enabled);
    }

    #[test]
    fn client_certificate_and_key_are_parsed_and_materialised_when_both_are_valid_pem_cfg_012_cfg_014()
    {
        const SAMPLE_CERT: &str =
            "-----BEGIN CERTIFICATE-----\nSGVsbG8sIHdvcmxkIQ==\n-----END CERTIFICATE-----\n";
        const SAMPLE_KEY: &str =
            "-----BEGIN PRIVATE KEY-----\nSGVsbG8sIHdvcmxkIQ==\n-----END PRIVATE KEY-----\n";
        let mut cert_file = tempfile::NamedTempFile::new().expect("temp file");
        write!(cert_file, "{SAMPLE_CERT}").expect("write cert");
        let mut key_file = tempfile::NamedTempFile::new().expect("temp file");
        write!(key_file, "{SAMPLE_KEY}").expect("write key");

        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .client_cert_path(cert_file.path())
            .client_key_path(key_file.path())
            .build()
            .expect("a valid client certificate pair must succeed");
        let client_certificate = config
            .tls()
            .client_certificate
            .as_ref()
            .expect("client certificate must be materialised");
        assert_eq!(client_certificate.certificate_der.len(), 1);
        assert_eq!(client_certificate.private_key_der, b"Hello, world!");
    }

    #[test]
    fn default_token_file_falls_back_when_no_home_directory_is_known() {
        assert_eq!(
            default_token_file_with_home(None),
            PathBuf::from(".vault-token")
        );
        assert_eq!(
            default_token_file_with_home(Some(std::ffi::OsString::from("/home/example"))),
            PathBuf::from("/home/example").join(".vault-token")
        );
    }

    #[test]
    fn builder_and_config_debug_forms_redact_the_token() {
        let builder = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .token("s.super-secret");
        let debug = format!("{builder:?}");
        assert!(!debug.contains("s.super-secret"));

        let config = builder.build().expect("valid");
        let debug = format!("{config:?}");
        assert!(!debug.contains("s.super-secret"));
    }

    #[test]
    fn every_builder_setter_and_config_accessor_round_trips() {
        let discovery_timeout = Duration::from_millis(2500);
        let config = ClientConfigBuilder::new()
            .with_environment(EnvironmentSource::None)
            .address("https://vault.example.test:8200")
            .connect_timeout(Duration::from_secs(7))
            .discovery_probe_timeout(discovery_timeout)
            .user_agent("custom-agent/1.0")
            .api_prefix(ApiPrefix::V2)
            .auto_renew(AutoRenew { enabled: true })
            .rate_gate(RateGate {
                rate_per_second: 4,
                burst: 9,
            })
            .build()
            .expect("valid");

        assert_eq!(config.connect_timeout(), Duration::from_secs(7));
        assert_eq!(config.discovery_probe_timeout(), discovery_timeout);
        assert_eq!(config.user_agent(), "custom-agent/1.0");
        assert_eq!(config.api_prefix(), ApiPrefix::V2);
        assert!(config.auto_renew().enabled);
        assert_eq!(config.rate_gate().rate_per_second, 4);
        assert_eq!(config.rate_gate().burst, 9);
        assert!(!config.allow_insecure_http());
        let _logger = config.logger();
        assert!(config.transport().is_none());
        assert!(!config.token_file().as_os_str().is_empty());
    }
}
