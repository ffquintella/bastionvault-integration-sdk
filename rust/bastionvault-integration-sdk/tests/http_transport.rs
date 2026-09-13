//! Integration tests for the production `HttpTransport` (D-M1b-3) against the harness's
//! real HTTPS mock server and a plain-TCP mock, so the transport that ships to callers
//! is exercised end to end and not only through `FakeTransport`.

mod harness;

use std::convert::Infallible;
use std::time::Duration;

use bastionvault_integration_sdk::{ClientConfigBuilder, EnvironmentSource, HttpTransport, Transport, TransportRequest};
use bytes::Bytes;
use harness::mock_server::{MockScenario, MockServer, MockServerConfig};
use http_body_util::Full;
use hyper::service::service_fn;
use hyper::{Request, Response};
use hyper_util::rt::TokioIo;
use tokio::net::TcpListener;

/// Base64-encodes `der` into a PEM block with `label` (e.g. `"CERTIFICATE"`), matching
/// the shape `pem.rs` parses. Used to feed the mock server's DER-only client identity
/// into `ClientConfigBuilder`, which only accepts PEM.
fn pem_encode(label: &str, der: &[u8]) -> String {
    use base64::Engine;
    let encoded = base64::engine::general_purpose::STANDARD.encode(der);
    let mut body = String::new();
    for chunk in encoded.as_bytes().chunks(64) {
        body.push_str(std::str::from_utf8(chunk).expect("base64 is ASCII"));
        body.push('\n');
    }
    format!("-----BEGIN {label}-----\n{body}-----END {label}-----\n")
}

/// A minimal plain-HTTP (no TLS) loopback server, so `HttpTransport`'s non-TLS branch
/// is exercised by a real socket rather than only by unit-level URL parsing.
async fn start_plain_http_echo() -> std::net::SocketAddr {
    let listener = TcpListener::bind(("127.0.0.1", 0)).await.expect("bind must succeed");
    let address = listener.local_addr().expect("local addr must resolve");
    tokio::spawn(async move {
        loop {
            let Ok((stream, _)) = listener.accept().await else {
                break;
            };
            tokio::spawn(async move {
                let service = service_fn(|_req: Request<hyper::body::Incoming>| async move {
                    Ok::<_, Infallible>(Response::new(Full::new(Bytes::from_static(b"plain-ok"))))
                });
                let _ = hyper::server::conn::http1::Builder::new()
                    .serve_connection(TokioIo::new(stream), service)
                    .await;
            });
        }
    });
    address
}

fn request(method: &str, url: String) -> TransportRequest {
    TransportRequest {
        method: method.to_owned(),
        url,
        headers: Vec::new(),
        body: None,
        timeout: Duration::from_secs(5),
        connect_timeout: Duration::from_secs(5),
        max_response_bytes: 1_000_000,
    }
}

fn https_url(server: &MockServer, path: &str) -> String {
    format!("https://{}{}", server.address(), path)
}

#[tokio::test]
async fn get_against_a_real_https_server_with_a_pinned_ca_succeeds() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo))
        .await
        .expect("mock server must start");
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .ca_cert_pem(server.ca_pem())
        .tls_server_name("localhost")
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);
    let response = transport
        .send(request("GET", https_url(&server, "/v1/secret/data/x")))
        .await
        .expect("request must succeed");
    assert_eq!(response.status, 200);
    assert!(String::from_utf8_lossy(&response.body).contains("GET"));
}

#[tokio::test]
async fn custom_list_verb_reaches_the_server_trn_010() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo))
        .await
        .expect("mock server must start");
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .ca_cert_pem(server.ca_pem())
        .tls_server_name("localhost")
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);
    let response = transport
        .send(request("LIST", https_url(&server, "/v1/secret/metadata/app/")))
        .await
        .expect("LIST must succeed");
    assert!(String::from_utf8_lossy(&response.body).contains("LIST"));
}

#[tokio::test]
async fn tls_skip_verify_accepts_a_hostname_mismatched_certificate_cfg_018() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo).hostname_mismatch())
        .await
        .expect("mock server must start");
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .tls_skip_verify(true)
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);
    let response = transport
        .send(request("GET", https_url(&server, "/v1/x")))
        .await
        .expect("skip-verify must accept a mismatched certificate");
    assert_eq!(response.status, 200);
}

#[tokio::test]
async fn a_response_over_max_response_bytes_is_rejected_by_content_length_trn_033_d_m1b_20() {
    let server = MockServer::start(
        MockServerConfig::new(MockScenario::Echo).oversized_response(4096),
    )
    .await
    .expect("mock server must start");
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .ca_cert_pem(server.ca_pem())
        .tls_server_name("localhost")
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);
    let mut req = request("GET", https_url(&server, "/v1/x"));
    req.max_response_bytes = 1024;
    let error = transport.send(req).await.expect_err("must be rejected");
    assert_eq!(error.code(), "BV-TRANSPORT-004");
}

#[tokio::test]
async fn mtls_client_certificate_is_presented_when_required_cfg_044() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo).require_client_certificate())
        .await
        .expect("mock server must start");

    // Without a client certificate, the handshake must fail (the server requires one).
    let no_cert_config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .ca_cert_pem(server.ca_pem())
        .tls_server_name("localhost")
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&no_cert_config);
    let error = transport
        .send(request("GET", https_url(&server, "/v1/x")))
        .await
        .expect_err("mTLS must be required");
    // Depending on exactly where the peer tears down the connection (TLS alert during
    // the handshake vs. a reset once the HTTP/1.1 handshake tries to proceed over it),
    // this surfaces as either a TLS failure or a connection failure — both are
    // transport-level failures raised as the SDK `Error` (D-M1b-1), never a raw
    // runtime exception, which is the property this test actually protects.
    assert!(matches!(error.code(), "BV-TRANSPORT-003" | "BV-TRANSPORT-001"));
}

#[tokio::test]
async fn connection_refused_on_an_unused_port_maps_to_bv_transport_001() {
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);
    // Port 1 is a reserved low port that nothing listens on locally.
    let error = transport
        .send(request("GET", "https://127.0.0.1:1/v1/x".to_owned()))
        .await
        .expect_err("must fail to connect");
    assert_eq!(error.code(), "BV-TRANSPORT-001");
    assert!(error.retryable());
}

#[tokio::test]
async fn two_sequential_requests_on_one_client_reuse_one_connection_trn_090_d_m1b_25() {
    // D-M1b-25: `FakeTransport` opens no sockets at all, so it cannot demonstrate
    // connection *reuse* either way — this has to be proved against a real server.
    // `HttpTransport` now routes every `send` through one `hyper_util::client::legacy`
    // pool built once in `HttpTransport::new`, so two requests on the same transport
    // must arrive over one accepted TCP connection, not two.
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo))
        .await
        .expect("mock server must start");
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .ca_cert_pem(server.ca_pem())
        .tls_server_name("localhost")
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);

    let first = transport
        .send(request("GET", https_url(&server, "/v1/a")))
        .await
        .expect("first request must succeed");
    let second = transport
        .send(request("GET", https_url(&server, "/v1/b")))
        .await
        .expect("second request must succeed");

    assert_eq!(first.status, 200);
    assert_eq!(second.status, 200);
    assert_eq!(
        server.accepted_connections(),
        1,
        "two requests on one HttpTransport must reuse one pooled connection, not open two"
    );
}

#[tokio::test]
async fn plain_http_without_tls_reaches_a_real_server() {
    let address = start_plain_http_echo().await;
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);
    let mut req = request("GET", format!("http://{address}/v1/x"));
    req.headers.push(("X-Custom".to_owned(), "value".to_owned()));
    let response = transport.send(req).await.expect("plain HTTP must succeed");
    assert_eq!(response.status, 200);
    assert_eq!(response.body, b"plain-ok");
}

#[tokio::test]
async fn mtls_client_certificate_succeeds_when_presented_cfg_044() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo).require_client_certificate())
        .await
        .expect("mock server must start");
    let identity = server.client_identity();
    let cert_pem = pem_encode("CERTIFICATE", &identity.certificate_der);
    let key_pem = pem_encode("PRIVATE KEY", &identity.private_key_der);
    let cert_file = tempfile::NamedTempFile::new().expect("temp file");
    let key_file = tempfile::NamedTempFile::new().expect("temp file");
    std::fs::write(cert_file.path(), &cert_pem).expect("write cert");
    std::fs::write(key_file.path(), &key_pem).expect("write key");

    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .ca_cert_pem(server.ca_pem())
        .tls_server_name("localhost")
        .client_cert_path(cert_file.path())
        .client_key_path(key_file.path())
        .build()
        .expect("valid config with a client certificate");
    let transport = HttpTransport::new(&config);
    let response = transport
        .send(request("GET", https_url(&server, "/v1/x")))
        .await
        .expect("mTLS handshake must succeed when a matching client certificate is presented");
    assert_eq!(response.status, 200);
}

#[tokio::test]
async fn an_unsupported_scheme_is_a_connection_failure() {
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .build()
        .expect("valid config");
    let transport = HttpTransport::new(&config);
    let error = transport
        .send(request("GET", "ftp://127.0.0.1:1/v1/x".to_owned()))
        .await
        .expect_err("must fail");
    assert_eq!(error.code(), "BV-TRANSPORT-001");
}
