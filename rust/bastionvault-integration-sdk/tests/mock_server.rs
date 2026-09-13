mod harness;

use std::time::Duration;

use harness::mock_server::{MockScenario, MockServer, MockServerConfig};
use harness::raw_client::{RawClientConfig, RawHttpClient};

#[tokio::test]
async fn https_listener_accepts_list_and_reaches_handler_tst_020_tst_021() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo))
        .await
        .expect("HTTPS mock server must start");
    let mut client = RawHttpClient::connect(&server, RawClientConfig::pinned(server.ca_pem()))
        .await
        .expect("pinned raw client must connect");
    let response = client
        .request("LIST", "/v1/items", false)
        .await
        .expect("LIST request must complete");
    assert_eq!(response.status, 200);
    assert_eq!(response.header("content-type"), Some("application/json"));
    assert_eq!(response.header("content-length"), Some("17"));
    assert_eq!(
        String::from_utf8(response.body).expect("JSON body"),
        r#"{"method":"LIST"}"#
    );
    assert_eq!(server.handled_methods().await, vec!["LIST"]);
}

#[tokio::test]
async fn https_listener_simulates_each_tst_021_response_tst_020_tst_021() {
    let cases = [
        (
            MockScenario::Sealed {
                message: "sealed".to_owned(),
            },
            503,
            None,
            r#"{"errors":["sealed"]}"#,
        ),
        (
            MockScenario::StandbyHealth,
            429,
            None,
            r#"{"errors":["standby"]}"#,
        ),
        (
            MockScenario::Uninitialized,
            501,
            None,
            r#"{"errors":["uninitialized"]}"#,
        ),
        (
            MockScenario::DosBan {
                retry_after: "30".to_owned(),
            },
            429,
            Some("30"),
            r#"{"errors":["dos ban"]}"#,
        ),
        (
            MockScenario::NamespaceQuota,
            429,
            None,
            r#"{"errors":["namespace quota"]}"#,
        ),
        (MockScenario::NotFound, 404, None, ""),
        (MockScenario::MethodNotAllowed, 405, None, ""),
        (MockScenario::NoContent, 204, None, ""),
        (
            MockScenario::LoginFailure {
                message: "invalid login".to_owned(),
            },
            200,
            None,
            r#"{"data":{"error":"invalid login"}}"#,
        ),
    ];
    for (scenario, status, retry_after, body) in cases {
        let server = MockServer::start(MockServerConfig::new(scenario))
            .await
            .expect("HTTPS mock server must start");
        let mut client = RawHttpClient::connect(&server, RawClientConfig::pinned(server.ca_pem()))
            .await
            .expect("pinned raw client must connect");
        let response = client
            .request("GET", "/v1/test", false)
            .await
            .expect("scenario request must complete");
        assert_eq!(response.status, status);
        assert_eq!(response.header("retry-after"), retry_after);
        assert_eq!(
            String::from_utf8(response.body.clone()).expect("scenario body"),
            body
        );
        let expected_length = body.len().to_string();
        if status == 204 {
            // RFC 9110 section 6.4.1 (and section 8.6): a server MUST NOT send
            // Content-Length on a 204 response. hyper's h1 encoder enforces this
            // by stripping the header even though the mock server code sets it,
            // so a 204 must not be held to the same expectation as the other
            // empty-body statuses below.
            assert_eq!(response.header("content-length"), None);
        } else {
            assert_eq!(
                response.header("content-length"),
                Some(expected_length.as_str())
            );
        }
        if status == 204 || status == 404 || status == 405 {
            assert_eq!(response.header("content-type"), None);
        } else {
            assert_eq!(response.header("content-type"), Some("application/json"));
        }
    }
}

#[tokio::test]
async fn https_listener_enforces_ca_hostname_and_mtls_tst_020_tst_050() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo))
        .await
        .expect("HTTPS mock server must start");
    assert!(
        RawHttpClient::connect(&server, RawClientConfig::default())
            .await
            .is_err()
    );
    let mut pinned = RawHttpClient::connect(&server, RawClientConfig::pinned(server.ca_pem()))
        .await
        .expect("CA pinning must permit the TLS handshake");
    assert_eq!(
        pinned
            .request("GET", "/", false)
            .await
            .expect("request")
            .status,
        200
    );

    let mismatch = MockServer::start(MockServerConfig::new(MockScenario::Echo).hostname_mismatch())
        .await
        .expect("hostname-mismatch server must start");
    assert!(
        RawHttpClient::connect(&mismatch, RawClientConfig::pinned(mismatch.ca_pem()))
            .await
            .is_err()
    );
    let mut skipped = RawHttpClient::connect(
        &mismatch,
        RawClientConfig::pinned(mismatch.ca_pem()).with_skip_verify(),
    )
    .await
    .expect("TlsSkipVerify must bypass hostname verification");
    assert_eq!(
        skipped
            .request("GET", "/", false)
            .await
            .expect("request")
            .status,
        200
    );

    let mtls =
        MockServer::start(MockServerConfig::new(MockScenario::Echo).require_client_certificate())
            .await
            .expect("mTLS server must start");
    if let Ok(mut unauthenticated) =
        RawHttpClient::connect(&mtls, RawClientConfig::pinned(mtls.ca_pem())).await
    {
        assert!(unauthenticated.request("GET", "/", false).await.is_err());
    }
    let config =
        RawClientConfig::pinned(mtls.ca_pem()).with_client_identity(mtls.client_identity());
    let mut authenticated = RawHttpClient::connect(&mtls, config)
        .await
        .expect("client certificate must satisfy mTLS");
    assert_eq!(
        authenticated
            .request("GET", "/", false)
            .await
            .expect("request")
            .status,
        200
    );
}

#[tokio::test]
async fn https_listener_reuses_keep_alive_and_counts_connections_tst_020() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo))
        .await
        .expect("HTTPS mock server must start");
    let mut client = RawHttpClient::connect(&server, RawClientConfig::pinned(server.ca_pem()))
        .await
        .expect("pinned raw client must connect");
    assert_eq!(
        client
            .request("GET", "/one", true)
            .await
            .expect("first request")
            .status,
        200
    );
    assert_eq!(
        client
            .request("GET", "/two", true)
            .await
            .expect("second request")
            .status,
        200
    );
    assert_eq!(server.accepted_connections(), 1);
}

#[tokio::test]
async fn https_listener_supports_delay_and_response_size_cap_tst_020_tst_021() {
    let server =
        MockServer::start(MockServerConfig::new(MockScenario::Echo).oversized_response(4096))
            .await
            .expect("configured HTTPS mock server must start");
    let config = RawClientConfig::pinned(server.ca_pem()).with_max_response_size(128);
    let mut client = RawHttpClient::connect(&server, config)
        .await
        .expect("pinned raw client must connect");
    let error = client
        .request("GET", "/large", false)
        .await
        .expect_err("oversized response must hit the client cap");
    assert!(error.contains("size cap"));
    drop(server);

    let delayed = MockServer::start(
        MockServerConfig::new(MockScenario::Echo).response_delay(Duration::from_millis(1)),
    )
    .await
    .expect("delayed HTTPS mock server must start");
    assert_eq!(delayed.response_delay(), Some(Duration::from_millis(1)));
}

#[tokio::test]
async fn generated_certificate_files_are_temporary_tst_020_tst_050() {
    let server = MockServer::start(MockServerConfig::new(MockScenario::Echo))
        .await
        .expect("HTTPS mock server must start");
    let directory = server.certificate_directory().to_path_buf();
    assert!(directory.is_dir());
    drop(server);
    assert!(!directory.exists());
}
