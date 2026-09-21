//! The nine token-store operations, each driven against the SDK's own `FakeTransport` and
//! asserted on **the request it actually issued** — method, path and body.
//!
//! The shared fixtures cover five of them (`AUT-014`, `AUT-080`, `AUT-081`, `AUT-083`, and
//! `AUT-084`'s 404). This file covers the rest, because `05-authentication.md:172-174` is
//! emphatic that **only** the nine paths the server has may be exposed, and a wrong path is
//! the defect class that a green envelope-parsing test cannot see.

/// Fake tokens assembled rather than written as literals, so `CNF-025`'s secret scan
/// stays strict (D-M1c-15). This test binary does not link the shared harness, so the
/// constants it needs are repeated here; the values are byte-identical to the
/// harness's and to the conformance fixtures'.
mod fake_tokens {
    pub const CLIENT: &str = concat!("s.", "FAKEtoken0000000000000000");
    pub const CREATED: &str = concat!("s.", "FAKEcreated00000000000000");
    pub const OTHER: &str = concat!("s.", "FAKEother000000000000000000");
}

use std::collections::HashMap;
use std::sync::Arc;
use std::time::Duration;

use bastionvault_integration_sdk::{
    Client, ClientConfigBuilder, CreateTokenRequest, EnvironmentSource, FakeTransport, RetryPolicy,
    SecretString, TransportResponse,
};

const TOKEN: &str = fake_tokens::CLIENT;
const OTHER: &str = fake_tokens::OTHER;

fn client_with(responses: Vec<TransportResponse>) -> (Client, Arc<FakeTransport>) {
    let transport = Arc::new(FakeTransport::new());
    for response in responses {
        transport.script_response(response);
    }
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address("https://vault.example.com:8200")
        .token(TOKEN)
        .transport(Arc::clone(&transport) as Arc<dyn bastionvault_integration_sdk::Transport>)
        .retry_policy(RetryPolicy {
            max_attempts: 1,
            ..RetryPolicy::default()
        })
        .build()
        .expect("valid config");
    (Client::new(config).expect("valid client"), transport)
}

fn ok(body: &str) -> TransportResponse {
    TransportResponse {
        status: 200,
        headers: vec![("Content-Type".to_owned(), "application/json".to_owned())],
        body: body.as_bytes().to_vec(),
    }
}

fn no_content() -> TransportResponse {
    TransportResponse {
        status: 204,
        headers: Vec::new(),
        body: Vec::new(),
    }
}

fn auth_envelope() -> &'static str {
    concat!(
        r#"{"auth":{"client_token":"s."#,
        r#"FAKEcreated00000000000000","policies":["default"],
        "metadata":{"purpose":"x"},"lease_duration":3600,"renewable":true},"data":{}}"#
    )
}

fn lookup_envelope() -> &'static str {
    concat!(
        r#"{"data":{"id":"s."#,
        r#"FAKEtoken0000000000000000","policies":["default","app-read"],
        "path":"auth/userpass/login/alice","meta":{"username":"alice"},"display_name":"alice",
        "num_uses":3,"ttl":0,"creation_time":1789300800,"creation_ttl":3600,
        "explicit_max_ttl":7200,"period":600}}"#
    )
}

/// The URL each operation must issue, verbatim from `05-authentication.md`'s table.
#[tokio::test]
async fn every_token_store_operation_uses_the_path_the_specification_names_aut_080_aut_082_aut_083() {
    // (description, expected method, expected URL)
    let expectations: Vec<(&str, &str, String)> = vec![
        (
            "create",
            "POST",
            "https://vault.example.com:8200/v1/auth/token/create".to_owned(),
        ),
        (
            "lookup",
            "GET",
            format!("https://vault.example.com:8200/v1/auth/token/lookup/{OTHER}"),
        ),
        (
            "lookup_self",
            "GET",
            "https://vault.example.com:8200/v1/auth/token/lookup-self".to_owned(),
        ),
        (
            "renew",
            "POST",
            format!("https://vault.example.com:8200/v1/auth/token/renew/{OTHER}"),
        ),
        (
            "revoke",
            "POST",
            format!("https://vault.example.com:8200/v1/auth/token/revoke/{OTHER}"),
        ),
        (
            "revoke_orphan",
            "POST",
            format!("https://vault.example.com:8200/v1/auth/token/revoke-orphan/{OTHER}"),
        ),
        (
            "revoke_self",
            "POST",
            "https://vault.example.com:8200/v1/auth/token/revoke-self".to_owned(),
        ),
        (
            "audit_login",
            "POST",
            "https://vault.example.com:8200/v1/auth/token/audit-login".to_owned(),
        ),
        (
            "verify",
            "GET",
            "https://vault.example.com:8200/v1/auth/token/lookup-self".to_owned(),
        ),
    ];

    for (name, method, url) in expectations {
        let (client, transport) = match name {
            "create" | "renew" => client_with(vec![ok(auth_envelope())]),
            "lookup" | "lookup_self" | "verify" => client_with(vec![ok(lookup_envelope())]),
            _ => client_with(vec![no_content()]),
        };
        let auth = client.auth();
        let token = auth.token();
        match name {
            "create" => {
                token.create(&CreateTokenRequest::default(), None).await.expect(name);
            }
            "lookup" => {
                token.lookup(OTHER, None).await.expect(name);
            }
            "lookup_self" => {
                token.lookup_self(None).await.expect(name);
            }
            "verify" => {
                token.verify(None).await.expect(name);
            }
            "renew" => {
                token.renew(OTHER, 3600, None).await.expect(name);
            }
            "revoke" => token.revoke(OTHER, None).await.expect(name),
            "revoke_orphan" => token.revoke_orphan(OTHER, None).await.expect(name),
            "revoke_self" => token.revoke_self(None).await.expect(name),
            "audit_login" => token.audit_login(None).await.expect(name),
            other => panic!("unhandled {other}"),
        }
        let sent = transport.recorded_requests();
        assert_eq!(sent.len(), 1, "{name} must issue exactly one request");
        assert_eq!(sent[0].method, method, "{name} method");
        assert_eq!(sent[0].url, url, "{name} url");
    }
}

/// `05-authentication.md:172-174`: `renew-self`, `lookup-accessor`, `renew-accessor`,
/// `revoke-accessor`, `create-orphan` and `tidy` do **not** exist on the server, and the
/// SDK MUST NOT expose operations for them. `renew_self` in particular must go through
/// `renew/{token}` (`AUT-080`) — the whole point of that requirement.
#[tokio::test]
async fn no_operation_ever_issues_a_path_the_server_does_not_have_aut_080() {
    let (client, transport) = client_with(vec![ok(auth_envelope())]);
    client.auth().token().renew_self(3600, None).await.expect("renew_self");
    let sent = transport.recorded_requests();
    assert_eq!(sent[0].url, format!("https://vault.example.com:8200/v1/auth/token/renew/{TOKEN}"));
    for absent in ["renew-self", "lookup-accessor", "renew-accessor", "revoke-accessor", "create-orphan", "tidy"] {
        assert!(!sent[0].url.contains(absent), "{absent} does not exist on the server");
    }
    // The required `increment` body (the table marks it **required**).
    let body = sent[0].body.as_ref().expect("renew carries a body");
    assert_eq!(
        serde_json::from_slice::<serde_json::Value>(body).expect("json"),
        serde_json::json!({ "increment": 3600 })
    );
}

/// `AUT-082`: `Create` returns `AuthInfo` and **does not** switch the client's token unless
/// the caller asks with `use_result`.
#[tokio::test]
async fn create_returns_auth_info_and_does_not_switch_the_token_unless_asked_aut_082() {
    let (client, _) = client_with(vec![ok(auth_envelope())]);
    let auth = client
        .auth()
        .token()
        .create(&CreateTokenRequest::default(), None)
        .await
        .expect("create");
    assert_eq!(auth.client_token.reveal(), fake_tokens::CREATED);
    assert_eq!(auth.policies, vec!["default".to_owned()]);
    assert_eq!(auth.metadata, HashMap::from([("purpose".to_owned(), "x".to_owned())]));
    assert_eq!(auth.lease_duration, Duration::from_secs(3600));
    assert!(auth.renewable);
    // The default is `false`, so the client keeps the token it had.
    assert_eq!(
        client.auth().current_token().map(|token| token.reveal().to_owned()),
        Some(TOKEN.to_owned()),
        "AUT-082: Create must not switch the client's token unless UseResult is set"
    );

    let (client, _) = client_with(vec![ok(auth_envelope())]);
    let request = CreateTokenRequest {
        use_result: true,
        ..CreateTokenRequest::default()
    };
    client.auth().token().create(&request, None).await.expect("create");
    assert_eq!(
        client.auth().current_token().map(|token| token.reveal().to_owned()),
        Some(fake_tokens::CREATED.to_owned()),
        "UseResult = true is the opt-in that switches it"
    );
}

/// `AUT-004`: `lookup_self` records its result as `Auth.TokenInfo`, and `lookup` — which is
/// about *another* token — does not.
#[tokio::test]
async fn lookup_self_records_token_info_and_lookup_does_not_aut_004() {
    let (client, _) = client_with(vec![ok(lookup_envelope())]);
    assert!(client.auth().token_info().is_none());
    let info = client.auth().token().lookup_self(None).await.expect("lookup_self");
    let recorded = client.auth().token_info().expect("AUT-004 records the last LookupSelf");
    assert_eq!(recorded, info);
    assert_eq!(recorded.policies, vec!["default".to_owned(), "app-read".to_owned()]);
    assert_eq!(recorded.path.as_deref(), Some("auth/userpass/login/alice"));
    assert_eq!(recorded.display_name.as_deref(), Some("alice"));
    assert_eq!(recorded.num_uses, 3);
    assert_eq!(recorded.explicit_max_ttl, Duration::from_secs(7200));
    assert_eq!(recorded.period, Some(Duration::from_secs(600)));
    assert_eq!(recorded.meta, HashMap::from([("username".to_owned(), "alice".to_owned())]));

    let (client, _) = client_with(vec![ok(lookup_envelope())]);
    client.auth().token().lookup(OTHER, None).await.expect("lookup");
    assert!(
        client.auth().token_info().is_none(),
        "AUT-004 names the last LookupSelf, not the last Lookup of somebody else's token"
    );
}

/// `AUT-083`: `RevokeSelf` clears the local token. A root-policy token is accepted by the
/// server but not actually revoked; the SDK clears its token either way, because the
/// server's response is identical and the client cannot tell the two apart.
#[tokio::test]
async fn revoke_self_clears_the_local_token_aut_083() {
    let (client, _) = client_with(vec![no_content()]);
    assert!(client.auth().current_token().is_some());
    client.auth().token().revoke_self(None).await.expect("revoke_self");
    assert!(client.auth().current_token().is_none());
    // AUT-001: the source is still exactly one, and it is Static.
    assert_eq!(
        client.auth().token_source().kind(),
        bastionvault_integration_sdk::TokenSourceKind::Static
    );
}

/// Revoking **another** token leaves the client's own credential alone. Asserted because
/// `revoke` and `revoke_self` differ by one word and share a code path.
#[tokio::test]
async fn revoking_another_token_does_not_clear_the_local_one_aut_083() {
    for (name, response) in [("revoke", no_content()), ("revoke_orphan", no_content())] {
        let (client, _) = client_with(vec![response]);
        match name {
            "revoke" => client.auth().token().revoke(OTHER, None).await.expect(name),
            _ => client.auth().token().revoke_orphan(OTHER, None).await.expect(name),
        }
        assert_eq!(
            client.auth().current_token().map(|token| token.reveal().to_owned()),
            Some(TOKEN.to_owned()),
            "{name} must not touch the client's own token"
        );
    }
}

/// Every operation taking a token argument refuses a blank one client-side with
/// `BV-INPUT-001`, before a request is spent.
#[tokio::test]
async fn a_blank_token_argument_is_refused_client_side_aut_020() {
    for blank in ["", "   "] {
        let (client, transport) = client_with(vec![no_content()]);
        let auth = client.auth();
        let token = auth.token();
        for error in [
            token.lookup(blank, None).await.err(),
            token.renew(blank, 1, None).await.err(),
            token.revoke(blank, None).await.err(),
            token.revoke_orphan(blank, None).await.err(),
        ] {
            let error = error.expect("a blank token argument must be refused");
            assert_eq!(error.code(), "BV-INPUT-001");
            assert_eq!(error.attempts(), 0);
            assert_eq!(error.status_code(), None);
        }
        assert!(transport.recorded_requests().is_empty(), "nothing may reach the wire");
    }
}

/// `AUT-085`, end to end through the real request path: a `400 Request is invalid.` from
/// `renew/{token}` is `BV-AUTH-015`, and the same body from `create` is not.
#[tokio::test]
async fn a_renew_of_an_unknown_token_is_token_not_renewable_aut_085() {
    let invalid = TransportResponse {
        status: 400,
        headers: vec![("Content-Type".to_owned(), "application/json".to_owned())],
        body: br#"{"error":"Request is invalid."}"#.to_vec(),
    };
    let (client, _) = client_with(vec![invalid.clone()]);
    let error = client
        .auth()
        .token()
        .renew(OTHER, 3600, None)
        .await
        .expect_err("an unknown token cannot be renewed");
    assert_eq!(error.code(), "BV-AUTH-015");
    assert_eq!(error.status_code(), Some(400));
    assert!(!error.retryable());
    // ERR-003: the token segment of the path is redacted everywhere it is surfaced.
    assert!(!format!("{error}").contains("FAKEother"), "{error}");

    // The same body off the renew endpoint keeps the generic Appendix B row.
    let (client, _) = client_with(vec![invalid]);
    let error = client
        .auth()
        .token()
        .create(&CreateTokenRequest::default(), None)
        .await
        .expect_err("a 400 is an error");
    assert_eq!(error.code(), "BV-INPUT-100", "AUT-085 is scoped to the renew endpoint");
}

/// `AUT-084`, end to end: a `404` **with an empty body** from `lookup/{token}` is
/// `BV-NOTFOUND-006`; a `404` carrying a body is the server saying something else, and
/// `lookup-self` is a different endpoint the specification names no refinement for.
#[tokio::test]
async fn a_lookup_of_an_unknown_token_is_token_not_found_aut_084() {
    let empty_404 = || TransportResponse {
        status: 404,
        headers: Vec::new(),
        body: Vec::new(),
    };
    let (client, _) = client_with(vec![empty_404()]);
    let error = client
        .auth()
        .token()
        .lookup(OTHER, None)
        .await
        .expect_err("an unknown token is not found");
    assert_eq!(error.code(), "BV-NOTFOUND-006");
    assert_eq!(error.status_code(), Some(404));

    // `lookup-self` — a different endpoint, so the generic 404 row.
    let (client, _) = client_with(vec![empty_404()]);
    let error = client
        .auth()
        .token()
        .lookup_self(None)
        .await
        .expect_err("a 404 is an error");
    assert_eq!(error.code(), "BV-NOTFOUND-001", "lookup-self has no AUT-084 refinement");

    // A 404 carrying a body: the generic row again.
    let (client, _) = client_with(vec![TransportResponse {
        status: 404,
        headers: vec![("Content-Type".to_owned(), "application/json".to_owned())],
        body: br#"{"error":"Router mount not found."}"#.to_vec(),
    }]);
    let error = client.auth().token().lookup(OTHER, None).await.expect_err("a 404 is an error");
    assert_eq!(error.code(), "BV-NOTFOUND-002", "a recognised body wins over the refinement");
}

/// An operation whose response contract is an envelope `auth`/`data` object, given
/// something else, reports `BV-PROTOCOL-002` rather than fabricating a value (D-M1c-25).
#[tokio::test]
async fn an_envelope_mismatch_is_reported_not_fabricated_d_m1c_25() {
    let (client, _) = client_with(vec![no_content()]);
    let error = client
        .auth()
        .token()
        .create(&CreateTokenRequest::default(), None)
        .await
        .expect_err("a 204 carries no auth object");
    assert_eq!(error.code(), "BV-PROTOCOL-002");

    let (client, _) = client_with(vec![ok(r#"{"auth":{"client_token":"s.FAKEx"}}"#)]);
    let error = client
        .auth()
        .token()
        .lookup_self(None)
        .await
        .expect_err("an auth-only envelope carries no data object");
    assert_eq!(error.code(), "BV-PROTOCOL-002");
}

/// `AUT-020`: `Use` installs the token with **no network**, and a per-call
/// `RequestOptions` is still optional everywhere (`CFG-060`).
#[tokio::test]
async fn use_installs_the_token_with_no_network_aut_020_cfg_060() {
    let (client, transport) = client_with(vec![no_content()]);
    client
        .auth()
        .token()
        .r#use(SecretString::new(OTHER))
        .expect("a real token is accepted");
    assert!(transport.recorded_requests().is_empty(), "AUT-020: no network");
    assert_eq!(
        client.auth().current_token().map(|token| token.reveal().to_owned()),
        Some(OTHER.to_owned())
    );
    // And the installed token is the one the next request sends.
    client.auth().token().audit_login(None).await.expect("audit_login");
    let sent = transport.recorded_requests();
    assert!(
        sent[0]
            .headers
            .iter()
            .any(|(name, value)| name.eq_ignore_ascii_case("X-BastionVault-Token") && value == OTHER)
    );
}

/// `CFG-032`: `ForgetPersistedToken` deletes the file if present and does not fail if it is
/// absent, and `CFG-031`'s write is the only thing that ever creates it.
#[tokio::test]
async fn the_token_helper_write_path_is_explicit_only_cfg_031_cfg_032() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let path = directory.path().join(".vault-token");
    let transport = Arc::new(FakeTransport::new());
    transport.script_response(no_content());
    let config = ClientConfigBuilder::new()
        .with_environment(EnvironmentSource::None)
        .address("https://vault.example.com:8200")
        .token(TOKEN)
        .token_file(path.clone())
        .transport(Arc::clone(&transport) as Arc<dyn bastionvault_integration_sdk::Transport>)
        .build()
        .expect("valid config");
    let client = Client::new(config).expect("valid client");

    // A request does not write the file; only PersistToken does (CFG-031).
    client.auth().token().audit_login(None).await.expect("audit_login");
    assert!(!path.exists());
    client.auth().persist_token().expect("persist");
    assert_eq!(std::fs::read_to_string(&path).expect("readable"), TOKEN);
    client.auth().forget_persisted_token().expect("forget");
    assert!(!path.exists());
}
