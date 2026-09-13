use std::sync::Arc;

use rustls::client::danger::{HandshakeSignatureValid, ServerCertVerified, ServerCertVerifier};
use rustls::pki_types::{
    CertificateDer, PrivateKeyDer, PrivatePkcs8KeyDer, ServerName, UnixTime, pem::PemObject,
};
use rustls::{
    ClientConfig, DigitallySignedStruct, Error as TlsError, RootCertStore, SignatureScheme,
};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio_rustls::TlsConnector;
use tokio_rustls::client::TlsStream;

use super::mock_server::{ClientIdentity, MockServer};

#[derive(Debug, Clone, Default)]
pub struct RawClientConfig {
    pub ca_pem: Option<String>,
    pub skip_verify: bool,
    pub client_identity: Option<ClientIdentity>,
    pub max_response_size: Option<usize>,
}

impl RawClientConfig {
    pub fn pinned(ca_pem: &str) -> Self {
        Self {
            ca_pem: Some(ca_pem.to_owned()),
            ..Self::default()
        }
    }

    pub fn with_skip_verify(mut self) -> Self {
        self.skip_verify = true;
        self
    }

    pub fn with_client_identity(mut self, identity: ClientIdentity) -> Self {
        self.client_identity = Some(identity);
        self
    }

    pub fn with_max_response_size(mut self, size: usize) -> Self {
        self.max_response_size = Some(size);
        self
    }
}

pub struct RawHttpClient {
    stream: TlsStream<TcpStream>,
    buffered: Vec<u8>,
    max_response_size: Option<usize>,
}

impl RawHttpClient {
    pub async fn connect(server: &MockServer, config: RawClientConfig) -> Result<Self, String> {
        let stream = TcpStream::connect(server.address())
            .await
            .map_err(|error| format!("raw client TCP connection failed: {error}"))?;
        let mut roots = RootCertStore::empty();
        if let Some(ca_pem) = config.ca_pem.as_deref() {
            let certificate = CertificateDer::from_pem_slice(ca_pem.as_bytes())
                .map_err(|error| format!("raw client CA PEM parse failed: {error}"))?;
            roots
                .add(certificate)
                .map_err(|error| format!("raw client CA setup failed: {error}"))?;
        }
        let client_config = make_client_config(roots, config.skip_verify, config.client_identity)?;
        let connector = TlsConnector::from(Arc::new(client_config));
        let server_name = ServerName::try_from(server.server_name().to_owned())
            .map_err(|error| format!("raw client server-name setup failed: {error}"))?;
        let stream = connector
            .connect(server_name, stream)
            .await
            .map_err(|error| format!("raw client TLS handshake failed: {error}"))?;
        Ok(Self {
            stream,
            buffered: Vec::new(),
            max_response_size: config.max_response_size,
        })
    }

    pub async fn request(
        &mut self,
        method: &str,
        path: &str,
        keep_alive: bool,
    ) -> Result<RawResponse, String> {
        let connection = if keep_alive { "keep-alive" } else { "close" };
        let request = format!(
            "{method} {path} HTTP/1.1\r\nHost: localhost\r\nConnection: {connection}\r\nContent-Length: 0\r\n\r\n"
        );
        self.stream
            .write_all(request.as_bytes())
            .await
            .map_err(|error| format!("raw client write failed: {error}"))?;
        self.read_response().await
    }

    async fn read_response(&mut self) -> Result<RawResponse, String> {
        let header_end = self.read_until(b"\r\n\r\n").await?;
        let header_text = String::from_utf8(self.buffered[..header_end].to_vec())
            .map_err(|error| format!("raw client response headers were not UTF-8: {error}"))?;
        let mut lines = header_text.split("\r\n");
        let status_line = lines
            .next()
            .ok_or_else(|| "raw client response had no status line".to_owned())?;
        let status = status_line
            .split_whitespace()
            .nth(1)
            .ok_or_else(|| "raw client response status line was malformed".to_owned())?
            .parse::<u16>()
            .map_err(|error| format!("raw client response status was malformed: {error}"))?;
        let mut headers = Vec::new();
        let mut content_length = 0usize;
        for line in lines.filter(|line| !line.is_empty()) {
            let (name, value) = line
                .split_once(':')
                .ok_or_else(|| "raw client response header was malformed".to_owned())?;
            let name = name.trim().to_ascii_lowercase();
            let value = value.trim().to_owned();
            if name == "content-length" {
                content_length = value
                    .parse::<usize>()
                    .map_err(|error| format!("raw client content length was malformed: {error}"))?;
            }
            headers.push((name, value));
        }
        if self
            .max_response_size
            .is_some_and(|limit| content_length > limit)
        {
            return Err(format!(
                "raw client response exceeded configured size cap: {content_length} bytes"
            ));
        }
        let body_start = header_end;
        self.read_exactly(body_start, content_length).await?;
        let body = self.buffered[body_start..body_start + content_length].to_vec();
        self.buffered.drain(..body_start + content_length);
        Ok(RawResponse {
            status,
            headers,
            body,
        })
    }

    async fn read_until(&mut self, delimiter: &[u8]) -> Result<usize, String> {
        loop {
            if let Some(index) = self
                .buffered
                .windows(delimiter.len())
                .position(|window| window == delimiter)
            {
                return Ok(index + delimiter.len());
            }
            let mut chunk = [0u8; 4096];
            let count = self
                .stream
                .read(&mut chunk)
                .await
                .map_err(|error| format!("raw client read failed: {error}"))?;
            if count == 0 {
                return Err("raw client response ended before headers completed".to_owned());
            }
            self.buffered.extend_from_slice(&chunk[..count]);
        }
    }

    async fn read_exactly(&mut self, start: usize, length: usize) -> Result<(), String> {
        while self.buffered.len().saturating_sub(start) < length {
            let mut chunk = [0u8; 4096];
            let count = self
                .stream
                .read(&mut chunk)
                .await
                .map_err(|error| format!("raw client body read failed: {error}"))?;
            if count == 0 {
                return Err("raw client response ended before body completed".to_owned());
            }
            self.buffered.extend_from_slice(&chunk[..count]);
        }
        Ok(())
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RawResponse {
    pub status: u16,
    pub headers: Vec<(String, String)>,
    pub body: Vec<u8>,
}

impl RawResponse {
    pub fn header(&self, name: &str) -> Option<&str> {
        let name = name.to_ascii_lowercase();
        self.headers
            .iter()
            .find(|(header, _)| header == &name)
            .map(|(_, value)| value.as_str())
    }
}

fn make_client_config(
    roots: RootCertStore,
    skip_verify: bool,
    identity: Option<ClientIdentity>,
) -> Result<ClientConfig, String> {
    if skip_verify {
        let builder = ClientConfig::builder()
            .dangerous()
            .with_custom_certificate_verifier(Arc::new(NoVerifier));
        return match identity {
            Some(identity) => builder
                .with_client_auth_cert(
                    vec![CertificateDer::from(identity.certificate_der)],
                    rustls::pki_types::PrivateKeyDer::Pkcs8(
                        rustls::pki_types::PrivatePkcs8KeyDer::from(identity.private_key_der),
                    ),
                )
                .map_err(|error| format!("raw client identity setup failed: {error}")),
            None => Ok(builder.with_no_client_auth()),
        };
    }
    let builder = ClientConfig::builder().with_root_certificates(roots);
    match identity {
        Some(identity) => builder
            .with_client_auth_cert(
                vec![CertificateDer::from(identity.certificate_der)],
                PrivateKeyDer::Pkcs8(PrivatePkcs8KeyDer::from(identity.private_key_der)),
            )
            .map_err(|error| format!("raw client identity setup failed: {error}")),
        None => Ok(builder.with_no_client_auth()),
    }
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
    ) -> Result<ServerCertVerified, TlsError> {
        Ok(ServerCertVerified::assertion())
    }

    fn verify_tls12_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, TlsError> {
        let provider = rustls::crypto::ring::default_provider();
        rustls::crypto::verify_tls12_signature(
            message,
            cert,
            dss,
            &provider.signature_verification_algorithms,
        )
    }

    fn verify_tls13_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> Result<HandshakeSignatureValid, TlsError> {
        let provider = rustls::crypto::ring::default_provider();
        rustls::crypto::verify_tls13_signature(
            message,
            cert,
            dss,
            &provider.signature_verification_algorithms,
        )
    }

    fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
        rustls::crypto::ring::default_provider()
            .signature_verification_algorithms
            .supported_schemes()
    }
}
