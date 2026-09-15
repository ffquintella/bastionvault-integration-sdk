use std::convert::Infallible;
use std::fs;
use std::net::SocketAddr;
use std::path::Path;
use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

use bytes::Bytes;
use http_body_util::Full;
use hyper::body::Incoming;
use hyper::server::conn::http1;
use hyper::service::service_fn;
use hyper::{Method, Request, Response, StatusCode};
use hyper_util::rt::TokioIo;
use rcgen::{
    BasicConstraints, CertificateParams, ExtendedKeyUsagePurpose, IsCa, Issuer, KeyPair,
    KeyUsagePurpose,
};
use rustls::pki_types::{CertificateDer, PrivateKeyDer, PrivatePkcs8KeyDer};
use rustls::server::WebPkiClientVerifier;
use rustls::{RootCertStore, ServerConfig};
use tempfile::TempDir;
use tokio::net::TcpListener;
use tokio::sync::{Mutex, Notify};
use tokio::time::sleep;
use tokio_rustls::TlsAcceptor;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum MockScenario {
    Echo,
    Sealed { message: String },
    StandbyHealth,
    Uninitialized,
    DosBan { retry_after: String },
    NamespaceQuota,
    NotFound,
    MethodNotAllowed,
    NoContent,
    LoginFailure { message: String },
}

#[derive(Debug, Clone)]
pub struct MockServerConfig {
    pub scenario: MockScenario,
    pub certificate_hostname: String,
    pub require_client_certificate: bool,
    pub response_delay: Option<Duration>,
    pub oversized_response: Option<usize>,
}

impl MockServerConfig {
    pub fn new(scenario: MockScenario) -> Self {
        Self {
            scenario,
            certificate_hostname: "localhost".to_owned(),
            require_client_certificate: false,
            response_delay: None,
            oversized_response: None,
        }
    }

    pub fn hostname_mismatch(mut self) -> Self {
        self.certificate_hostname = "certificate-only.test".to_owned();
        self
    }

    pub fn require_client_certificate(mut self) -> Self {
        self.require_client_certificate = true;
        self
    }

    pub fn response_delay(mut self, delay: Duration) -> Self {
        self.response_delay = Some(delay);
        self
    }

    pub fn oversized_response(mut self, bytes: usize) -> Self {
        self.oversized_response = Some(bytes);
        self
    }
}

#[derive(Debug, Clone)]
pub struct ClientIdentity {
    pub(crate) certificate_der: Vec<u8>,
    pub(crate) private_key_der: Vec<u8>,
}

#[derive(Debug)]
struct ServerState {
    config: MockServerConfig,
    accepted_connections: AtomicUsize,
    handled_methods: Mutex<Vec<String>>,
}

#[derive(Debug)]
pub struct MockServer {
    address: SocketAddr,
    server_name: String,
    ca_pem: String,
    client_identity: ClientIdentity,
    certificate_directory: TempDir,
    state: Arc<ServerState>,
    shutdown: Arc<Notify>,
    task: tokio::task::JoinHandle<()>,
}

impl MockServer {
    pub async fn start(config: MockServerConfig) -> Result<Self, String> {
        let _ = rustls::crypto::ring::default_provider().install_default();
        let certificate_directory = tempfile::tempdir().map_err(|error| {
            format!("unable to create temporary certificate directory: {error}")
        })?;
        let material = CertificateMaterial::generate(&config.certificate_hostname)?;
        material
            .write_to(&certificate_directory)
            .map_err(|error| format!("unable to write temporary certificate material: {error}"))?;
        let server_config = build_server_config(&material, config.require_client_certificate)?;
        let listener = TcpListener::bind(("127.0.0.1", 0))
            .await
            .map_err(|error| format!("unable to bind HTTPS mock server: {error}"))?;
        let address = listener
            .local_addr()
            .map_err(|error| format!("unable to read HTTPS mock server address: {error}"))?;
        let state = Arc::new(ServerState {
            config,
            accepted_connections: AtomicUsize::new(0),
            handled_methods: Mutex::new(Vec::new()),
        });
        let shutdown = Arc::new(Notify::new());
        let task = spawn_accept_loop(
            listener,
            Arc::clone(&state),
            Arc::clone(&shutdown),
            server_config,
        );
        Ok(Self {
            address,
            server_name: "localhost".to_owned(),
            ca_pem: material.ca_pem,
            client_identity: material.client_identity,
            certificate_directory,
            state,
            shutdown,
            task,
        })
    }

    pub fn address(&self) -> SocketAddr {
        self.address
    }

    pub fn server_name(&self) -> &str {
        &self.server_name
    }

    pub fn ca_pem(&self) -> &str {
        &self.ca_pem
    }

    pub fn client_identity(&self) -> ClientIdentity {
        self.client_identity.clone()
    }

    pub fn accepted_connections(&self) -> usize {
        self.state.accepted_connections.load(Ordering::SeqCst)
    }

    pub async fn handled_methods(&self) -> Vec<String> {
        self.state.handled_methods.lock().await.clone()
    }

    pub fn certificate_directory(&self) -> &Path {
        self.certificate_directory.path()
    }

    pub fn response_delay(&self) -> Option<Duration> {
        self.state.config.response_delay
    }
}

impl Drop for MockServer {
    fn drop(&mut self) {
        self.shutdown.notify_waiters();
        self.task.abort();
    }
}

fn spawn_accept_loop(
    listener: TcpListener,
    state: Arc<ServerState>,
    shutdown: Arc<Notify>,
    server_config: Arc<ServerConfig>,
) -> tokio::task::JoinHandle<()> {
    let acceptor = TlsAcceptor::from(server_config);
    tokio::spawn(async move {
        loop {
            tokio::select! {
                _ = shutdown.notified() => break,
                accepted = listener.accept() => {
                    let Ok((stream, _peer)) = accepted else { break };
                    state.accepted_connections.fetch_add(1, Ordering::SeqCst);
                    let acceptor = acceptor.clone();
                    let state = Arc::clone(&state);
                    tokio::spawn(async move {
                        let Ok(stream) = acceptor.accept(stream).await else { return };
                        let service = service_fn(move |request| handle_request(request, Arc::clone(&state)));
                        let _ = http1::Builder::new()
                            .keep_alive(true)
                            .serve_connection(TokioIo::new(stream), service)
                            .await;
                    });
                }
            }
        }
    })
}

async fn handle_request(
    request: Request<Incoming>,
    state: Arc<ServerState>,
) -> Result<Response<Full<Bytes>>, Infallible> {
    state
        .handled_methods
        .lock()
        .await
        .push(request.method().as_str().to_owned());
    if let Some(delay) = state.config.response_delay {
        sleep(delay).await;
    }
    let (status, headers, body) = response_parts(&state.config, request.method());
    let mut builder = Response::builder().status(status);
    for (name, value) in headers {
        builder = builder.header(name, value);
    }
    let body = Bytes::from(body);
    let body_length = body.len();
    let body = Full::new(body);
    Ok(builder
        .header("content-length", body_length)
        .body(body)
        .expect("mock response builder is valid"))
}

fn response_parts(
    config: &MockServerConfig,
    method: &Method,
) -> (StatusCode, Vec<(&'static str, String)>, Vec<u8>) {
    if let Some(size) = config.oversized_response {
        return (
            StatusCode::OK,
            vec![("content-type", "application/octet-stream".to_owned())],
            vec![b'x'; size],
        );
    }
    match &config.scenario {
        MockScenario::Echo => (
            StatusCode::OK,
            vec![("content-type", "application/json".to_owned())],
            format!(r#"{{"method":"{}"}}"#, method.as_str()).into_bytes(),
        ),
        MockScenario::Sealed { message } => json_error(StatusCode::SERVICE_UNAVAILABLE, message),
        MockScenario::StandbyHealth => json_error(StatusCode::TOO_MANY_REQUESTS, "standby"),
        MockScenario::Uninitialized => json_error(StatusCode::NOT_IMPLEMENTED, "uninitialized"),
        MockScenario::DosBan { retry_after } => (
            StatusCode::TOO_MANY_REQUESTS,
            vec![
                ("content-type", "application/json".to_owned()),
                ("retry-after", retry_after.clone()),
            ],
            br#"{"errors":["dos ban"]}"#.to_vec(),
        ),
        MockScenario::NamespaceQuota => {
            json_error(StatusCode::TOO_MANY_REQUESTS, "namespace quota")
        }
        MockScenario::NotFound => (StatusCode::NOT_FOUND, Vec::new(), Vec::new()),
        MockScenario::MethodNotAllowed => (StatusCode::METHOD_NOT_ALLOWED, Vec::new(), Vec::new()),
        MockScenario::NoContent => (StatusCode::NO_CONTENT, Vec::new(), Vec::new()),
        MockScenario::LoginFailure { message } => (
            StatusCode::OK,
            vec![("content-type", "application/json".to_owned())],
            format!(r#"{{"data":{{"error":"{}"}}}}"#, escape_json(message)).into_bytes(),
        ),
    }
}

fn json_error(
    status: StatusCode,
    message: &str,
) -> (StatusCode, Vec<(&'static str, String)>, Vec<u8>) {
    (
        status,
        vec![("content-type", "application/json".to_owned())],
        format!(r#"{{"errors":["{}"]}}"#, escape_json(message)).into_bytes(),
    )
}

fn escape_json(value: &str) -> String {
    value.replace('\\', "\\\\").replace('"', "\\\"")
}

struct CertificateMaterial {
    ca_der: Vec<u8>,
    ca_pem: String,
    server_der: Vec<u8>,
    server_key_der: Vec<u8>,
    client_identity: ClientIdentity,
}

impl CertificateMaterial {
    fn generate(hostname: &str) -> Result<Self, String> {
        let ca_key =
            KeyPair::generate().map_err(|error| format!("CA key generation failed: {error}"))?;
        let mut ca_params = CertificateParams::default();
        ca_params.is_ca = IsCa::Ca(BasicConstraints::Unconstrained);
        ca_params.use_authority_key_identifier_extension = true;
        ca_params.key_usages = vec![KeyUsagePurpose::KeyCertSign, KeyUsagePurpose::CrlSign];
        let ca_cert = ca_params
            .self_signed(&ca_key)
            .map_err(|error| format!("CA certificate generation failed: {error}"))?;
        let issuer = Issuer::from_params(&ca_params, &ca_key);

        let server_key = KeyPair::generate()
            .map_err(|error| format!("server key generation failed: {error}"))?;
        let mut server_params = CertificateParams::new(vec![hostname.to_owned()])
            .map_err(|error| format!("server certificate parameters failed: {error}"))?;
        server_params.extended_key_usages = vec![ExtendedKeyUsagePurpose::ServerAuth];
        server_params.is_ca = IsCa::ExplicitNoCa;
        server_params.use_authority_key_identifier_extension = true;
        server_params.key_usages = vec![
            KeyUsagePurpose::DigitalSignature,
            KeyUsagePurpose::KeyEncipherment,
        ];
        let server_cert = server_params
            .signed_by(&server_key, &issuer)
            .map_err(|error| format!("server certificate generation failed: {error}"))?;

        let client_key = KeyPair::generate()
            .map_err(|error| format!("client key generation failed: {error}"))?;
        let mut client_params = CertificateParams::new(vec!["client.test".to_owned()])
            .map_err(|error| format!("client certificate parameters failed: {error}"))?;
        client_params.extended_key_usages = vec![ExtendedKeyUsagePurpose::ClientAuth];
        client_params.is_ca = IsCa::ExplicitNoCa;
        client_params.use_authority_key_identifier_extension = true;
        client_params.key_usages = vec![
            KeyUsagePurpose::DigitalSignature,
            KeyUsagePurpose::KeyEncipherment,
        ];
        let client_cert = client_params
            .signed_by(&client_key, &issuer)
            .map_err(|error| format!("client certificate generation failed: {error}"))?;
        Ok(Self {
            ca_der: ca_cert.der().to_vec(),
            ca_pem: ca_cert.pem(),
            server_der: server_cert.der().to_vec(),
            server_key_der: server_key.serialize_der(),
            client_identity: ClientIdentity {
                certificate_der: client_cert.der().to_vec(),
                private_key_der: client_key.serialize_der(),
            },
        })
    }

    fn write_to(&self, directory: &TempDir) -> std::io::Result<()> {
        fs::write(directory.path().join("ca.pem"), &self.ca_pem)?;
        fs::write(directory.path().join("server.der"), &self.server_der)?;
        fs::write(
            directory.path().join("server.key.der"),
            &self.server_key_der,
        )?;
        Ok(())
    }
}

fn build_server_config(
    material: &CertificateMaterial,
    require_client_certificate: bool,
) -> Result<Arc<ServerConfig>, String> {
    let server_certificate = CertificateDer::from(material.server_der.clone());
    let private_key =
        PrivateKeyDer::Pkcs8(PrivatePkcs8KeyDer::from(material.server_key_der.clone()));
    let config = if require_client_certificate {
        let mut roots = RootCertStore::empty();
        roots
            .add(CertificateDer::from(material.ca_der.clone()))
            .map_err(|error| format!("client CA setup failed: {error}"))?;
        let verifier = WebPkiClientVerifier::builder(Arc::new(roots))
            .build()
            .map_err(|error| format!("mTLS verifier setup failed: {error}"))?;
        ServerConfig::builder()
            .with_client_cert_verifier(verifier)
            .with_single_cert(vec![server_certificate], private_key)
            .map_err(|error| format!("HTTPS server configuration failed: {error}"))?
    } else {
        ServerConfig::builder()
            .with_no_client_auth()
            .with_single_cert(vec![server_certificate], private_key)
            .map_err(|error| format!("HTTPS server configuration failed: {error}"))?
    };
    Ok(Arc::new(config))
}
