//! The production `Transport` (D-M1b-3): `hyper` + `hyper-util` **legacy client**
//! (the pool, TRN-090) + `rustls` on `tokio`, built directly from `ClientConfig`'s
//! materialised TLS parameters (D-M1a-7/D-M1a-14) so CFG-040's *add* (not replace)
//! semantics and CFG-042's SNI override stay under this crate's control end to end.
//!
//! D-M1b-25 (coordinator review, recorded in `decisions/0004-m1b-transport.md`)
//! corrected two defects in the first pass:
//! 1. The root store was populated *only* from configured CA material, so with no
//!    `CaCertPath`/`CaCertPem` set — the default — verification could never succeed
//!    against any real deployment. `rustls-native-certs` now seeds the platform trust
//!    store, and configured CA material is **added** to it (CFG-040), unless
//!    `CaCertReplacesSystemRoots` is set, in which case the store holds only the
//!    configured material.
//! 2. The transport opened a new `TcpStream` per request, which cannot demonstrate
//!    TRN-090 connection reuse (a fact no fake transport can prove either way). It now
//!    routes every request through one `hyper_util::client::legacy::Client` — the pool
//!    itself — built once per `HttpTransport` and shared across calls, with a custom
//!    `tower_service::Service<Uri>` connector supplying the TLS behaviour above plus
//!    the `TlsServerName` SNI override (CFG-042/RES-010).

use std::future::Future;
use std::io;
use std::pin::Pin;
use std::sync::Arc;
use std::task::{Context as TaskContext, Poll};
use std::time::Duration;

use bytes::Bytes;
use http_body_util::{BodyExt, Full, Limited};
use hyper::body::Incoming;
use hyper::{Method, Uri};
use hyper_util::client::legacy::connect::{Connected, Connection};
use hyper_util::client::legacy::{Client as LegacyClient, Error as LegacyError};
use hyper_util::rt::{TokioExecutor, TokioIo};
use rustls::client::danger::{HandshakeSignatureValid, ServerCertVerified, ServerCertVerifier};
use rustls::pki_types::{CertificateDer, PrivateKeyDer, PrivatePkcs8KeyDer, ServerName, UnixTime};
use rustls::{ClientConfig as RustlsClientConfig, DigitallySignedStruct, RootCertStore, SignatureScheme};
use tokio::io::{AsyncRead, AsyncWrite, ReadBuf};
use tokio::net::TcpStream;
use tokio_rustls::TlsConnector;

use crate::config::ClientConfig;
use crate::error::mapping_errors::{
    transport_connection_failed, transport_response_too_large, transport_timeout, transport_tls_error,
};
use crate::error::Error;
use crate::transport::{Transport, TransportRequest, TransportResponse};

/// Any owned, boxed, pinned async stream — the shape both a plain `TcpStream` and a
/// `tokio_rustls` `TlsStream<TcpStream>` are erased to, so the connector has one
/// concrete response type regardless of scheme.
trait AsyncReadWrite: AsyncRead + AsyncWrite + Send {}
impl<T: AsyncRead + AsyncWrite + Send> AsyncReadWrite for T {}

/// Type-erased connection handed back to `hyper_util`'s legacy client. Holding the
/// inner stream as `Pin<Box<dyn AsyncReadWrite>>` means every poll method below is a
/// safe delegation through `Pin::as_mut` — no `unsafe` pin projection, matching this
/// crate's `unsafe_code = "deny"` lint. `Pin<Box<T>>` is `Unpin` regardless of `T`, so
/// this whole struct is `Unpin`, which is what `hyper_util`'s `Connect` blanket impl
/// requires of a connector's `Response` type.
struct BoxedIo(Pin<Box<dyn AsyncReadWrite>>);

impl AsyncRead for BoxedIo {
    fn poll_read(mut self: Pin<&mut Self>, cx: &mut TaskContext<'_>, buf: &mut ReadBuf<'_>) -> Poll<io::Result<()>> {
        self.0.as_mut().poll_read(cx, buf)
    }
}

impl AsyncWrite for BoxedIo {
    fn poll_write(mut self: Pin<&mut Self>, cx: &mut TaskContext<'_>, buf: &[u8]) -> Poll<io::Result<usize>> {
        self.0.as_mut().poll_write(cx, buf)
    }

    fn poll_flush(mut self: Pin<&mut Self>, cx: &mut TaskContext<'_>) -> Poll<io::Result<()>> {
        self.0.as_mut().poll_flush(cx)
    }

    fn poll_shutdown(mut self: Pin<&mut Self>, cx: &mut TaskContext<'_>) -> Poll<io::Result<()>> {
        self.0.as_mut().poll_shutdown(cx)
    }
}

/// `hyper_util`'s legacy client requires the connector's response type to report pool
/// metadata; a fresh, unproxied connection is all this transport ever produces.
impl Connection for BoxedIo {
    fn connected(&self) -> Connected {
        Connected::new()
    }
}

/// Distinguishes a TCP-level failure from a TLS-level one *inside* the connector, so
/// the outer `send` can still map to `BV-TRANSPORT-001` vs `BV-TRANSPORT-003`
/// (D-M1b-4a) even though `hyper_util`'s legacy client only ever surfaces one boxed
/// error type at the call site.
#[derive(Debug)]
enum ConnectError {
    Tcp(io::Error),
    Tls(io::Error),
}

impl std::fmt::Display for ConnectError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Tcp(error) => write!(f, "tcp connect failed: {error}"),
            Self::Tls(error) => write!(f, "tls handshake failed: {error}"),
        }
    }
}

impl std::error::Error for ConnectError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Tcp(error) | Self::Tls(error) => Some(error),
        }
    }
}

/// The connector: dials TCP, then optionally performs the TLS handshake with the
/// `rustls::ClientConfig` `HttpTransport::new` built (CFG-040/041/044) and the SNI
/// override `HttpTransport::new` recorded (CFG-042).
#[derive(Clone)]
struct RustlsConnector {
    tls_config: Arc<RustlsClientConfig>,
    server_name_override: Option<String>,
    connect_timeout: Duration,
}

impl tower_service::Service<Uri> for RustlsConnector {
    type Response = TokioIo<BoxedIo>;
    type Error = ConnectError;
    type Future = Pin<Box<dyn Future<Output = Result<Self::Response, Self::Error>> + Send>>;

    fn poll_ready(&mut self, _cx: &mut TaskContext<'_>) -> Poll<Result<(), Self::Error>> {
        Poll::Ready(Ok(()))
    }

    fn call(&mut self, uri: Uri) -> Self::Future {
        let connector = self.clone();
        Box::pin(async move {
            let is_https = uri.scheme_str() == Some("https");
            let host = uri
                .host()
                .ok_or_else(|| ConnectError::Tcp(io::Error::other("request URI has no host")))?
                .to_owned();
            let port = uri.port_u16().unwrap_or(if is_https { 443 } else { 80 });

            let tcp = tokio::time::timeout(connector.connect_timeout, TcpStream::connect((host.as_str(), port)))
                .await
                .map_err(|_| ConnectError::Tcp(io::Error::new(io::ErrorKind::TimedOut, "connect timed out")))?
                .map_err(ConnectError::Tcp)?;

            if !is_https {
                return Ok(TokioIo::new(BoxedIo(Box::pin(tcp))));
            }

            let tls_connector = TlsConnector::from(connector.tls_config.clone());
            let name_text = connector.server_name_override.clone().unwrap_or(host);
            let server_name = ServerName::try_from(name_text)
                .map_err(|error| ConnectError::Tls(io::Error::other(error.to_string())))?;
            let tls_stream = tls_connector
                .connect(server_name, tcp)
                .await
                .map_err(|error| ConnectError::Tls(io::Error::other(error.to_string())))?;
            Ok(TokioIo::new(BoxedIo(Box::pin(tls_stream))))
        })
    }
}

/// Finds a [`ConnectError`] anywhere in `error`'s source chain, so a connector failure
/// surfaced through `hyper_util::client::legacy::Error` (which boxes it) still maps to
/// the right `BV-TRANSPORT-*` code (D-M1b-4a) instead of a generic one.
fn find_connect_error<'a>(error: &'a (dyn std::error::Error + 'static)) -> Option<&'a ConnectError> {
    let mut current: Option<&(dyn std::error::Error + 'static)> = Some(error);
    while let Some(candidate) = current {
        if let Some(connect_error) = candidate.downcast_ref::<ConnectError>() {
            return Some(connect_error);
        }
        current = candidate.source();
    }
    None
}

fn classify_legacy_error(error: &LegacyError) -> Error {
    match find_connect_error(error) {
        Some(ConnectError::Tls(_)) => transport_tls_error(),
        Some(ConnectError::Tcp(_)) | None => transport_connection_failed(),
    }
}

/// Builds the `rustls::ClientConfig` once per `HttpTransport` (CFG-040/041/042/044,
/// D-M1b-25's fix #1): the platform trust store, plus configured CA material **added**
/// to it (never substituted) unless `CaCertReplacesSystemRoots` is set.
fn build_root_store(config: &ClientConfig) -> RootCertStore {
    let tls = config.tls();
    let mut roots = RootCertStore::empty();
    if !tls.ca_replaces_system_roots {
        let native = rustls_native_certs::load_native_certs();
        for certificate in native.certs {
            let _ = roots.add(certificate);
        }
        // Native-store load failures (e.g. an unusual container image with no system
        // trust store configured) are not fatal: the client still works with whatever
        // roots loaded, plus any explicitly configured CA (CFG-040 does not require
        // failing construction when the platform store is incomplete).
    }
    for der in &tls.ca_certificates {
        let _ = roots.add(CertificateDer::from(der.clone()));
    }
    roots
}

fn build_rustls_config(config: &ClientConfig) -> RustlsClientConfig {
    let tls = config.tls();
    let versions: &[&rustls::SupportedProtocolVersion] = if tls.offers_tls13 {
        &[&rustls::version::TLS12, &rustls::version::TLS13]
    } else {
        &[&rustls::version::TLS12]
    };

    let builder = RustlsClientConfig::builder_with_protocol_versions(versions);
    let base_builder = if config.tls_skip_verify() {
        builder
            .dangerous()
            .with_custom_certificate_verifier(Arc::new(NoVerifier))
    } else {
        builder.with_root_certificates(build_root_store(config))
    };

    if let Some(certificate) = &tls.client_certificate {
        let certs: Vec<CertificateDer<'static>> = certificate
            .certificate_der
            .iter()
            .cloned()
            .map(CertificateDer::from)
            .collect();
        let key = PrivateKeyDer::Pkcs8(PrivatePkcs8KeyDer::from(certificate.private_key_der.clone()));
        if let Ok(with_cert) = base_builder.with_client_auth_cert(certs, key) {
            return with_cert;
        }
        // Falls through to a no-client-auth config below only if the certificate pair
        // (already validated at CFG-014) somehow fails here; this cannot happen for a
        // config that passed construction, so no test exercises this arm.
    }

    if config.tls_skip_verify() {
        RustlsClientConfig::builder_with_protocol_versions(versions)
            .dangerous()
            .with_custom_certificate_verifier(Arc::new(NoVerifier))
            .with_no_client_auth()
    } else {
        RustlsClientConfig::builder_with_protocol_versions(versions)
            .with_root_certificates(build_root_store(config))
            .with_no_client_auth()
    }
}

#[derive(Debug)]
pub struct HttpTransport {
    client: LegacyClient<RustlsConnector, Full<Bytes>>,
}

impl HttpTransport {
    pub fn new(config: &ClientConfig) -> Self {
        let _ = rustls::crypto::ring::default_provider().install_default();
        let rustls_config = build_rustls_config(config);
        let connector = RustlsConnector {
            tls_config: Arc::new(rustls_config),
            server_name_override: config.tls().server_name.clone(),
            connect_timeout: config.connect_timeout(),
        };
        // This one `LegacyClient` *is* the connection pool (TRN-090): it is built once
        // here and every `send` call below reuses it, so two requests to the same
        // authority reuse one underlying connection instead of dialing twice.
        let client = LegacyClient::builder(TokioExecutor::new()).build(connector);
        Self { client }
    }
}

impl Transport for HttpTransport {
    fn send<'a>(
        &'a self,
        request: TransportRequest,
    ) -> Pin<Box<dyn Future<Output = Result<TransportResponse, Error>> + Send + 'a>> {
        Box::pin(async move { send_one(self, request).await })
    }
}

async fn send_one(transport: &HttpTransport, request: TransportRequest) -> Result<TransportResponse, Error> {
    let method = Method::from_bytes(request.method.as_bytes()).map_err(|_| transport_connection_failed())?;
    let uri: Uri = request.url.parse().map_err(|_| transport_connection_failed())?;

    let mut builder = hyper::Request::builder().method(method).uri(uri);
    for (name, value) in &request.headers {
        builder = builder.header(name.as_str(), value.as_str());
    }
    let body = Full::new(Bytes::from(request.body.clone().unwrap_or_default()));
    let hyper_request = builder.body(body).map_err(|_| transport_connection_failed())?;

    let response = tokio::time::timeout(request.timeout, transport.client.request(hyper_request))
        .await
        .map_err(|_| transport_timeout())?
        .map_err(|error| classify_legacy_error(&error))?;

    let status = response.status().as_u16();
    let headers: Vec<(String, String)> = response
        .headers()
        .iter()
        .map(|(name, value)| (name.to_string(), value.to_str().unwrap_or_default().to_owned()))
        .collect();

    if let Some(content_length) = response
        .headers()
        .get("content-length")
        .and_then(|value| value.to_str().ok())
        .and_then(|value| value.parse::<u64>().ok())
    {
        if content_length > request.max_response_bytes {
            return Err(transport_response_too_large());
        }
    }

    let incoming: Incoming = response.into_body();
    let limited = Limited::new(incoming, request.max_response_bytes as usize);
    let collected = limited.collect().await.map_err(|_| transport_response_too_large())?;
    let body = collected.to_bytes().to_vec();

    Ok(TransportResponse { status, headers, body })
}

#[derive(Debug)]
struct NoVerifier;

impl ServerCertVerifier for NoVerifier {
    fn verify_server_cert(
        &self,
        _end_entity: &CertificateDer<'_>,
        _intermediates: &[CertificateDer<'_>],
        _server_name: &ServerName<'_>,
        _ocsp_response: &[u8],
        _now: UnixTime,
    ) -> Result<ServerCertVerified, rustls::Error> {
        Ok(ServerCertVerified::assertion())
    }

    fn verify_tls12_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, rustls::Error> {
        let provider = rustls::crypto::ring::default_provider();
        rustls::crypto::verify_tls12_signature(message, cert, dss, &provider.signature_verification_algorithms)
    }

    fn verify_tls13_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, rustls::Error> {
        let provider = rustls::crypto::ring::default_provider();
        rustls::crypto::verify_tls13_signature(message, cert, dss, &provider.signature_verification_algorithms)
    }

    fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
        rustls::crypto::ring::default_provider()
            .signature_verification_algorithms
            .supported_schemes()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn connect_error_display_and_source_distinguish_tcp_from_tls() {
        let tcp = ConnectError::Tcp(io::Error::other("refused"));
        assert!(tcp.to_string().contains("tcp connect failed"));
        let tls = ConnectError::Tls(io::Error::other("bad cert"));
        assert!(tls.to_string().contains("tls handshake failed"));
        use std::error::Error as _;
        assert!(tls.source().is_some());
    }

    #[test]
    fn find_connect_error_locates_a_wrapped_error_in_the_source_chain() {
        #[derive(Debug)]
        struct Wrapper(ConnectError);
        impl std::fmt::Display for Wrapper {
            fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
                write!(f, "wrapped: {}", self.0)
            }
        }
        impl std::error::Error for Wrapper {
            fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
                Some(&self.0)
            }
        }
        let wrapped = Wrapper(ConnectError::Tls(io::Error::other("bad cert")));
        let found = find_connect_error(&wrapped).expect("must find the connect error");
        assert!(matches!(found, ConnectError::Tls(_)));
    }

    #[test]
    fn find_connect_error_returns_none_when_absent() {
        let plain = io::Error::other("unrelated");
        assert!(find_connect_error(&plain).is_none());
    }

    #[test]
    fn build_root_store_seeds_the_platform_trust_store_by_default_d_m1b_25() {
        // D-M1b-25 fix #1: with no `CaCertPath`/`CaCertPem` configured — the default —
        // the store must not be empty, or every HTTPS connection fails verification
        // against any ordinary deployment.
        let config = crate::ClientConfigBuilder::new()
            .with_environment(crate::EnvironmentSource::None)
            .build()
            .expect("valid config");
        let roots = build_root_store(&config);
        assert!(
            !roots.roots.is_empty(),
            "the platform trust store must be loaded by default (CFG-040)"
        );
    }

    #[test]
    fn build_root_store_adds_configured_ca_to_the_platform_store_not_in_place_of_it_cfg_040() {
        const SAMPLE_CERT: &str =
            "-----BEGIN CERTIFICATE-----\nSGVsbG8sIHdvcmxkIQ==\n-----END CERTIFICATE-----\n";
        let config = crate::ClientConfigBuilder::new()
            .with_environment(crate::EnvironmentSource::None)
            .ca_cert_pem(SAMPLE_CERT)
            .build()
            .expect("valid config");
        let without_ca = build_root_store(
            &crate::ClientConfigBuilder::new()
                .with_environment(crate::EnvironmentSource::None)
                .build()
                .expect("valid config"),
        );
        let with_ca = build_root_store(&config);
        // The configured (unparseable-as-a-real-anchor) sample does not necessarily
        // add a usable trust anchor, but the platform roots must still all be present
        // — this is the "add", not "replace", half of CFG-040.
        assert!(with_ca.roots.len() >= without_ca.roots.len());
    }

    #[test]
    fn ca_replaces_system_roots_produces_a_store_without_the_platform_roots_cfg_040() {
        const SAMPLE_CERT: &str =
            "-----BEGIN CERTIFICATE-----\nSGVsbG8sIHdvcmxkIQ==\n-----END CERTIFICATE-----\n";
        let config = crate::ClientConfigBuilder::new()
            .with_environment(crate::EnvironmentSource::None)
            .ca_cert_pem(SAMPLE_CERT)
            .ca_cert_replaces_system_roots(true)
            .build()
            .expect("valid config");
        let roots = build_root_store(&config);
        // The sample "certificate" is not a parseable trust anchor, so with the
        // platform store excluded the result is empty — proving the platform roots
        // were genuinely left out, not merely appended to.
        assert!(roots.roots.is_empty());
    }
}
