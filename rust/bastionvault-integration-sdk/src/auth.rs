//! The `Auth` area (`OVR-008`), reached from [`crate::Client::auth`]: the token source
//! (`AUT-001`), the current credential (`AUT-004`), the nine token-store operations
//! (`AUT-020`, `AUT-080`…`AUT-085`) and the token-helper write path (`CFG-031`,
//! `CFG-032`).
//!
//! This is the project's first sub-API grouping, so it fixes the shape every later engine
//! grouping copies. It is a lightweight borrowing view over the `Client`, exactly as
//! [`crate::Logical`] is, so a [`crate::Client::with_namespace`] view's `auth()` sees the
//! same token state as its parent's (`CFG-071`).
//!
//! **Every operation issues its request through the same executor the logical layer uses**
//! (D-M2-4): none of them builds an HTTP path, a status mapping or a recognition table of
//! its own. The two refinements the section-05 requirements add (`AUT-084`, `AUT-085`)
//! live in the shared [`crate::mapping`], not here.
//!
//! **Deferred to M6, and therefore absent rather than stubbed** (D-M2-8, D-M1c-25):
//! `Auth.Cert.Login`, the FerroGate operations, the OIDC/SAML operations,
//! `Auth.AppId.Admin`, and FIDO2. `Auth.Userpass` and `Auth.AppId` are M2b's.

use std::collections::{BTreeMap, HashMap};
use std::time::{Duration, SystemTime};

use serde_json::{Map, Value};

use crate::client::Client;
use crate::clock::from_unix_seconds;
use crate::error::Error;
use crate::logical::{AuthInfo, Logical, Response};
use crate::secret::SecretString;
use crate::token_source::TokenSource;
use crate::transport::RequestOptions;

/// `AUT-081`'s reserved `meta` keys, verbatim from `05-authentication.md:200-205`, plus
/// the `approle_env_` prefix rule.
///
/// The server refuses them too (`meta key(s) … are reserved` → `BV-INPUT-009`); refusing
/// client-side means the caller is told before a request is spent, and it is the same code
/// either way.
const RESERVED_META_KEYS: [&str; 19] = [
    "spiffe_id",
    "machine_id",
    "username",
    "entity_id",
    "mount_path",
    "role_name",
    "role",
    "namespace_path",
    "namespace_id",
    "child_visible",
    "auth_method",
    "groups",
    "subject",
    "name_id",
    "name_id_format",
    "ferrogate_kid",
    "session_id",
    "approle_machine_bypass",
    "machine_identity_exempt",
];

const RESERVED_META_KEY_PREFIX: &str = "approle_env_";

/// A token-store lookup's `data` object (`05-authentication.md:190-196`).
///
/// **The wire `ttl` is not here and never will be**: it is always `0` on the wire and the
/// specification forbids exposing it. [`Self::remaining_ttl`] is the computed value
/// `AUT-014` names instead.
#[derive(Debug, Clone, PartialEq)]
pub struct TokenInfo {
    /// The token itself, as a redacting secret type (D-M2-12).
    ///
    /// A lookup's `id` **is** token material; a plain `String` would be a second,
    /// non-redacting way to read the very token `AUT-004` requires `current_token` to
    /// redact (`CNF-031`/`CNF-032`).
    pub id: Option<SecretString>,
    pub policies: Vec<String>,
    pub path: Option<String>,
    pub meta: HashMap<String, String>,
    pub display_name: Option<String>,
    pub num_uses: i64,
    pub creation_time: Option<SystemTime>,
    pub creation_ttl: Duration,
    pub explicit_max_ttl: Duration,
    pub period: Option<Duration>,
    /// `AUT-014`: `creation_time + creation_ttl − now_utc`, and `None` when
    /// `creation_ttl == 0`.
    ///
    /// Both operands come from the response and the injected clock, never from the wire
    /// `ttl`. An already-expired token yields `Some(Duration::ZERO)` rather than a negative
    /// value, because [`Duration`] is unsigned — `None` keeps the one meaning the
    /// requirement gives it, "this token has no TTL at all".
    pub remaining_ttl: Option<Duration>,
}

/// The `auth/token/create` request body (`AUT-082`, `05-authentication.md:184`).
#[derive(Debug, Clone, PartialEq)]
pub struct CreateTokenRequest {
    pub policies: Option<Vec<String>>,
    pub ttl: Option<Duration>,
    pub period: Option<Duration>,
    pub num_uses: Option<i64>,
    pub renewable: bool,
    pub meta: Option<HashMap<String, String>>,
    pub display_name: Option<String>,
    pub explicit_max_ttl: Option<Duration>,
    pub no_default_policy: Option<bool>,
    /// Root only.
    pub no_parent: Option<bool>,
    /// Root only.
    pub id: Option<String>,
    pub r#type: Option<String>,
    pub child_visible: Option<bool>,
    /// `AUT-082`'s opt-in that switches the client's token. A client-side switch, so it is
    /// never sent on the wire.
    pub use_result: bool,
}

impl Default for CreateTokenRequest {
    /// `renewable` defaults to `true` and `use_result` to `false`, both per D-M2-6.
    fn default() -> Self {
        Self {
            policies: None,
            ttl: None,
            period: None,
            num_uses: None,
            renewable: true,
            meta: None,
            display_name: None,
            explicit_max_ttl: None,
            no_default_policy: None,
            no_parent: None,
            id: None,
            r#type: None,
            child_visible: None,
            use_result: false,
        }
    }
}

/// The `Auth` grouping (`OVR-008`). Created by [`crate::Client::auth`].
pub struct Auth<'a> {
    pub(crate) client: &'a Client,
}

impl<'a> Auth<'a> {
    /// `AUT-001`: the one [`TokenSource`] this client holds. `Client::set_token` and
    /// `TokenOps::use` (spelled `r#use`) replace it with a `TokenSourceKind::Static` one.
    pub fn token_source(&self) -> std::sync::Arc<TokenSource> {
        self.client.token_source()
    }

    /// `AUT-004`: the current token as a redacting secret type, or `None` when the client
    /// holds none. Reading this **never** performs a resolution, so it can neither trigger
    /// a login nor call an application callback.
    pub fn current_token(&self) -> Option<SecretString> {
        self.client.current_token()
    }

    /// `AUT-004`: the last [`TokenOps::lookup_self`] result, if any.
    pub fn token_info(&self) -> Option<TokenInfo> {
        self.client.token_info()
    }

    /// The token-store operations (`AUT-020`, `AUT-080`…`AUT-085`).
    pub fn token(&self) -> TokenOps<'a> {
        TokenOps { client: self.client }
    }

    /// `CFG-031`: writes the current token to `TokenFile` with owner-only permissions.
    ///
    /// This is the **only** way the SDK ever writes that file — a login never does, which
    /// is why the requirement makes it an explicit call rather than a side effect.
    ///
    /// Fails with `BV-INPUT-001` when the client holds no token, because persisting "no
    /// token" would silently leave a stale one on disk, and with `BV-CONFIG-011` when the
    /// file cannot be written (D-M2-16).
    pub fn persist_token(&self) -> Result<(), Error> {
        let Some(token) = self.client.current_token() else {
            return Err(invalid_argument(
                "current_token",
                "The client holds no token to persist.",
            ));
        };
        crate::token_files::write(self.client.config().token_file(), token.reveal())
    }

    /// `CFG-032`: deletes `TokenFile` if it is present, and does not fail if it is absent.
    pub fn forget_persisted_token(&self) -> Result<(), Error> {
        crate::token_files::delete(self.client.config().token_file())
    }
}

/// The token-store operations (`auth/token/*`), reached from [`Auth::token`].
///
/// Only the nine paths the server actually has are exposed. `lookup-accessor`,
/// `renew-self`, `renew-accessor`, `revoke-accessor`, `create-orphan` as a distinct path,
/// `roles` and `tidy` do **not** exist on the server and `05-authentication.md:172-174`
/// forbids exposing operations for them.
pub struct TokenOps<'a> {
    client: &'a Client,
}

impl TokenOps<'_> {
    fn logical(&self) -> Logical<'_> {
        self.client.logical()
    }

    /// `AUT-020`: installs `token` as a `Static` source. **No network.** An empty or
    /// whitespace token is rejected with `BV-INPUT-001`.
    ///
    /// Spelled with a raw identifier because `use` is a Rust keyword: this is the canonical
    /// name `Auth.Token.Use` after the `00-overview.md` idiom mapping, not a renaming of
    /// it.
    pub fn r#use(&self, token: SecretString) -> Result<(), Error> {
        if token.reveal().trim().is_empty() {
            return Err(invalid_argument(
                "token",
                "A token must be a non-empty, non-whitespace string.",
            ));
        }
        self.client.set_token(token);
        Ok(())
    }

    /// `Auth.Token.Verify`: a [`Self::lookup_self`] whose purpose is to fail with
    /// `BV-AUTHZ-001` when the token is invalid (`05-authentication.md:181`).
    pub async fn verify(&self, options: Option<RequestOptions>) -> Result<TokenInfo, Error> {
        self.lookup_self(options).await
    }

    /// `AUT-082`: `POST auth/token/create`. Returns the created token's [`AuthInfo`] and
    /// does **not** switch the client's token unless
    /// [`CreateTokenRequest::use_result`] is set. Reserved `meta` keys are refused before
    /// any request (`AUT-081`).
    pub async fn create(
        &self,
        request: &CreateTokenRequest,
        options: Option<RequestOptions>,
    ) -> Result<AuthInfo, Error> {
        guard_reserved_meta(request.meta.as_ref())?;
        let response = self
            .logical()
            .execute_shaped("POST", "auth/token/create", Some(serialise(request)), options, false, false)
            .await?;
        let auth = require_auth(response, "auth/token/create")?;
        if request.use_result {
            self.client.set_token(auth.client_token.clone());
        }
        Ok(auth)
    }

    /// `GET auth/token/lookup/{token}`. A `404` with an empty body is
    /// `BV-NOTFOUND-006 TokenNotFound`, not absence (`AUT-084`), and the token segment of
    /// the path is redacted in the error and in the observer event (`ERR-003`, `CFG-080`).
    pub async fn lookup(&self, token: &str, options: Option<RequestOptions>) -> Result<TokenInfo, Error> {
        require_non_blank(token, "token")?;
        let response = self
            .logical()
            .execute_shaped("GET", &format!("auth/token/lookup/{token}"), None, options, true, false)
            .await?;
        read_token_info(response, "auth/token/lookup", self.client)
    }

    /// `GET auth/token/lookup-self`. The result is also recorded as [`Auth::token_info`]
    /// (`AUT-004`).
    pub async fn lookup_self(&self, options: Option<RequestOptions>) -> Result<TokenInfo, Error> {
        let response = self
            .logical()
            .execute_shaped("GET", "auth/token/lookup-self", None, options, true, false)
            .await?;
        let info = read_token_info(response, "auth/token/lookup-self", self.client)?;
        self.client.set_token_info(info.clone());
        Ok(info)
    }

    /// `POST auth/token/renew/{token}` with the **required** `increment` body. An unknown
    /// or expired token yields `BV-AUTH-015 TokenNotRenewable` (`AUT-085`).
    pub async fn renew(
        &self,
        token: &str,
        increment: i64,
        options: Option<RequestOptions>,
    ) -> Result<AuthInfo, Error> {
        require_non_blank(token, "token")?;
        self.renew_path(token, increment, options).await
    }

    /// `AUT-080`: `RenewSelf` goes through `renew/{currentToken}` — there is no
    /// `renew-self` path on the server — so the live token appears in the request path, and
    /// the path is therefore redacted wherever it is surfaced (`ERR-003` in the error,
    /// `CFG-080` in the observer event).
    ///
    /// With no token this sends `auth/token/renew/`. That is correct for M2a: the
    /// `CFG-020`/`ERR-022` preflight is M2b's, so this is the absence of an unimplemented
    /// requirement rather than a guess at one (D-M1c-25).
    pub async fn renew_self(
        &self,
        increment: i64,
        options: Option<RequestOptions>,
    ) -> Result<AuthInfo, Error> {
        // Resolved exactly **once**, and then pinned onto the request so the executor's own
        // resolution is skipped: `RequestOptions.token` is the first thing resolution
        // honours. Resolving twice — once here for the path, once there for the header —
        // was a defect in the .NET pass. AUT-080 says "the current token in the path", and
        // a `Callback` source, whose contract is to be called on every resolution, can
        // return two different tokens for two resolutions: the request would then renew
        // token A while authenticating as token B. A per-call `RequestOptions.token` is
        // itself "the current token" for this call (`CFG-060`), so it is used as-is and the
        // client's source is not resolved at all.
        let mut options = options.unwrap_or_default();
        let current = match options.token.clone() {
            Some(pinned) => pinned,
            None => self
                .client
                .resolve_token()
                .await?
                .unwrap_or_else(|| SecretString::new("")),
        };
        let path_token = current.reveal().to_owned();
        options.token = Some(current);
        self.renew_path(&path_token, increment, Some(options)).await
    }

    /// `POST auth/token/revoke/{token}`.
    pub async fn revoke(&self, token: &str, options: Option<RequestOptions>) -> Result<(), Error> {
        require_non_blank(token, "token")?;
        self.logical()
            .execute_shaped("POST", &format!("auth/token/revoke/{token}"), None, options, false, false)
            .await?;
        Ok(())
    }

    /// `POST auth/token/revoke-orphan/{token}` (sudo).
    pub async fn revoke_orphan(&self, token: &str, options: Option<RequestOptions>) -> Result<(), Error> {
        require_non_blank(token, "token")?;
        self.logical()
            .execute_shaped("POST", &format!("auth/token/revoke-orphan/{token}"), None, options, false, false)
            .await?;
        Ok(())
    }

    /// `AUT-083`: `POST auth/token/revoke-self`, then clears the local token.
    ///
    /// A root-policy token is accepted by the server but not actually revoked (the logout
    /// is only recorded); the SDK clears its token either way, because the server's
    /// response is identical and the client cannot tell the two apart.
    pub async fn revoke_self(&self, options: Option<RequestOptions>) -> Result<(), Error> {
        self.logical()
            .execute_shaped("POST", "auth/token/revoke-self", None, options, false, false)
            .await?;
        self.client.clear_token();
        Ok(())
    }

    /// `POST auth/token/audit-login`: records a login event for a token sign-in.
    pub async fn audit_login(&self, options: Option<RequestOptions>) -> Result<(), Error> {
        self.logical()
            .execute_shaped("POST", "auth/token/audit-login", None, options, false, false)
            .await?;
        Ok(())
    }

    async fn renew_path(
        &self,
        token: &str,
        increment: i64,
        options: Option<RequestOptions>,
    ) -> Result<AuthInfo, Error> {
        let body = Value::Object(Map::from_iter([("increment".to_owned(), Value::from(increment))]));
        let response = self
            .logical()
            .execute_shaped(
                "POST",
                &format!("auth/token/renew/{token}"),
                Some(body),
                options,
                false,
                false,
            )
            .await?;
        require_auth(response, "auth/token/renew")
    }
}

/// `BV-INPUT-001`, raised client-side before any request: `attempts = 0`, no status code.
fn invalid_argument(argument: &str, reason: &str) -> Error {
    crate::error::catalog_errors::input_invalid_argument()
        .with_detail("argument", argument.to_owned())
        .with_detail("reason", reason.to_owned())
}

fn require_non_blank(value: &str, argument: &str) -> Result<(), Error> {
    if value.trim().is_empty() {
        return Err(invalid_argument(
            argument,
            "A token must be a non-empty, non-whitespace string.",
        ));
    }
    Ok(())
}

/// `AUT-081`'s client-side refusal.
///
/// The catalogue hint for `BV-INPUT-009` points at `Details.keys`, so the offending keys
/// are interpolated into it on the same `ERR-034` principle the path uses: a hint that
/// names a details key must name the value the SDK actually saw.
fn guard_reserved_meta(meta: Option<&HashMap<String, String>>) -> Result<(), Error> {
    let Some(meta) = meta else {
        return Ok(());
    };
    let mut offending: Vec<String> = meta
        .keys()
        .filter(|key| is_reserved_meta_key(key))
        .cloned()
        .collect();
    if offending.is_empty() {
        return Ok(());
    }
    // Ordered, so the error is the same whichever way the caller's map happened to iterate.
    offending.sort();

    let error = crate::error::catalog_errors::input_reserved_token_meta_key();
    let hint = crate::enrichment::interpolate_keys(error.hint(), &offending);
    Err(error.with_detail("keys", offending).with_hint(hint))
}

fn is_reserved_meta_key(key: &str) -> bool {
    key.starts_with(RESERVED_META_KEY_PREFIX) || RESERVED_META_KEYS.contains(&key)
}

/// An operation whose response contract is an envelope `auth` object got something else.
///
/// Reported as `BV-PROTOCOL-002`, which is what `04-error-model.md` names for a response
/// that does not match the documented envelope — not as an absent value the caller would
/// have to unwrap, and not as a fabricated empty [`AuthInfo`] (D-M1c-25).
fn require_auth(response: Option<Response>, path: &str) -> Result<AuthInfo, Error> {
    response
        .and_then(|response| response.auth)
        .ok_or_else(|| envelope_mismatch(path, "auth"))
}

fn envelope_mismatch(path: &str, field: &str) -> Error {
    crate::error::catalog_errors::protocol_unexpected_response()
        .with_detail("path", path.to_owned())
        .with_detail("expected", format!("an envelope `{field}` object"))
}

/// Maps a lookup's `data` object, computing `AUT-014`'s
/// [`TokenInfo::remaining_ttl`] from the injected clock.
///
/// The wire `ttl` field is read by nothing: it is always `0` and the specification forbids
/// exposing it.
fn read_token_info(response: Option<Response>, path: &str, client: &Client) -> Result<TokenInfo, Error> {
    let Some(data) = response.and_then(|response| response.data) else {
        return Err(envelope_mismatch(path, "data"));
    };

    let creation_time = read_unix_time(&data, "creation_time");
    let creation_ttl = read_seconds(&data, "creation_ttl").unwrap_or(Duration::ZERO);
    // AUT-014: creation_time + creation_ttl − now_utc, and None when creation_ttl is 0.
    let remaining_ttl = match (creation_time, creation_ttl) {
        (_, Duration::ZERO) => None,
        (None, _) => None,
        (Some(creation_time), creation_ttl) => {
            let expiry = creation_time + creation_ttl;
            // Saturating rather than signed: `Duration` is unsigned, so an already-expired
            // token reports zero remaining rather than borrowing `None`, which the
            // requirement reserves for "no TTL at all".
            Some(expiry.duration_since(client.config().clock().now_utc()).unwrap_or(Duration::ZERO))
        }
    };

    Ok(TokenInfo {
        id: read_string(&data, "id").map(SecretString::new),
        policies: read_string_array(&data, "policies"),
        path: read_string(&data, "path"),
        meta: read_string_map(&data, "meta"),
        display_name: read_string(&data, "display_name"),
        num_uses: data.get("num_uses").and_then(Value::as_i64).unwrap_or(0),
        creation_time,
        creation_ttl,
        explicit_max_ttl: read_seconds(&data, "explicit_max_ttl").unwrap_or(Duration::ZERO),
        period: read_seconds(&data, "period"),
        remaining_ttl,
    })
}

fn read_string(data: &Map<String, Value>, key: &str) -> Option<String> {
    data.get(key)
        .and_then(Value::as_str)
        .filter(|value| !value.is_empty())
        .map(str::to_owned)
}

fn read_string_array(data: &Map<String, Value>, key: &str) -> Vec<String> {
    data.get(key)
        .and_then(Value::as_array)
        .map(|values| values.iter().filter_map(|value| value.as_str().map(str::to_owned)).collect())
        .unwrap_or_default()
}

fn read_string_map(data: &Map<String, Value>, key: &str) -> HashMap<String, String> {
    data.get(key)
        .and_then(Value::as_object)
        .map(|values| {
            values
                .iter()
                .filter_map(|(key, value)| value.as_str().map(|value| (key.clone(), value.to_owned())))
                .collect()
        })
        .unwrap_or_default()
}

/// A whole-seconds duration field. `0` is a real value here (`creation_ttl: 0` is what
/// makes `remaining_ttl` `None`), so only an absent or non-numeric field yields `None`.
fn read_seconds(data: &Map<String, Value>, key: &str) -> Option<Duration> {
    data.get(key)
        .and_then(Value::as_i64)
        .map(|seconds| Duration::from_secs(seconds.max(0) as u64))
}

fn read_unix_time(data: &Map<String, Value>, key: &str) -> Option<SystemTime> {
    data.get(key).and_then(Value::as_i64).map(from_unix_seconds)
}

/// Serialises a [`CreateTokenRequest`] to the wire field names of
/// `05-authentication.md`'s table, omitting every field the caller left unset (`OVR-007`).
///
/// [`CreateTokenRequest::use_result`] is a client-side switch and is never sent.
fn serialise(request: &CreateTokenRequest) -> Value {
    // A BTreeMap so the emitted object is key-ordered and the fixtures' body comparison is
    // not at the mercy of insertion order.
    let mut body: BTreeMap<String, Value> = BTreeMap::new();
    if let Some(policies) = &request.policies {
        body.insert("policies".to_owned(), Value::from(policies.clone()));
    }
    insert_seconds(&mut body, "ttl", request.ttl);
    insert_seconds(&mut body, "period", request.period);
    if let Some(num_uses) = request.num_uses {
        body.insert("num_uses".to_owned(), Value::from(num_uses));
    }
    body.insert("renewable".to_owned(), Value::from(request.renewable));
    if let Some(meta) = &request.meta {
        let meta: Map<String, Value> = meta
            .iter()
            .map(|(key, value)| (key.clone(), Value::from(value.clone())))
            .collect();
        body.insert("meta".to_owned(), Value::Object(meta));
    }
    if let Some(display_name) = &request.display_name {
        body.insert("display_name".to_owned(), Value::from(display_name.clone()));
    }
    insert_seconds(&mut body, "explicit_max_ttl", request.explicit_max_ttl);
    if let Some(value) = request.no_default_policy {
        body.insert("no_default_policy".to_owned(), Value::from(value));
    }
    if let Some(value) = request.no_parent {
        body.insert("no_parent".to_owned(), Value::from(value));
    }
    if let Some(id) = &request.id {
        body.insert("id".to_owned(), Value::from(id.clone()));
    }
    if let Some(kind) = &request.r#type {
        body.insert("type".to_owned(), Value::from(kind.clone()));
    }
    if let Some(value) = request.child_visible {
        body.insert("child_visible".to_owned(), Value::from(value));
    }
    Value::Object(body.into_iter().collect())
}

fn insert_seconds(body: &mut BTreeMap<String, Value>, name: &str, value: Option<Duration>) {
    if let Some(duration) = value {
        body.insert(name.to_owned(), Value::from(duration.as_secs()));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_reserved_meta_key_and_the_prefix_rule_is_refused_aut_081() {
        for key in RESERVED_META_KEYS {
            assert!(is_reserved_meta_key(key), "{key} is in AUT-081's list");
        }
        assert!(is_reserved_meta_key("approle_env_secret"));
        assert!(is_reserved_meta_key("approle_env_"));
        assert!(!is_reserved_meta_key("purpose"));
        assert!(!is_reserved_meta_key("Username"), "the list is case-sensitive, as the server's is");
    }

    #[test]
    fn the_reserved_meta_guard_names_the_offending_keys_in_order_aut_081_err_034() {
        let meta = HashMap::from([
            ("spiffe_id".to_owned(), "spiffe://a/b".to_owned()),
            ("purpose".to_owned(), "x".to_owned()),
            ("approle_env_secret".to_owned(), "prod".to_owned()),
        ]);
        let error = guard_reserved_meta(Some(&meta)).expect_err("reserved keys must be refused");
        assert_eq!(error.code(), "BV-INPUT-009");
        assert_eq!(error.attempts(), 0);
        assert_eq!(error.status_code(), None, "refused before any request");
        assert_eq!(
            error.details().get("keys"),
            Some(&crate::error::DetailValue::List(vec![
                "approle_env_secret".to_owned(),
                "spiffe_id".to_owned(),
            ]))
        );
        assert!(error.hint().contains("`approle_env_secret`, `spiffe_id`"), "{}", error.hint());
        assert!(!error.hint().contains("purpose"));
    }

    #[test]
    fn a_meta_map_with_no_reserved_key_is_accepted_aut_081() {
        guard_reserved_meta(None).expect("no meta is fine");
        let meta = HashMap::from([("purpose".to_owned(), "x".to_owned())]);
        guard_reserved_meta(Some(&meta)).expect("an application key is fine");
    }

    #[test]
    fn create_serialises_only_the_fields_the_caller_set_plus_renewable_aut_082() {
        let request = CreateTokenRequest {
            policies: Some(vec!["default".to_owned()]),
            ttl: Some(Duration::from_secs(3600)),
            use_result: true,
            ..CreateTokenRequest::default()
        };
        let body = serialise(&request);
        assert_eq!(
            body,
            serde_json::json!({ "policies": ["default"], "ttl": 3600, "renewable": true }),
            "use_result is a client-side switch and is never sent"
        );
    }

    #[test]
    fn create_serialises_every_wire_field_under_its_specified_name_aut_082() {
        let request = CreateTokenRequest {
            policies: Some(vec!["a".to_owned()]),
            ttl: Some(Duration::from_secs(60)),
            period: Some(Duration::from_secs(120)),
            num_uses: Some(3),
            renewable: false,
            meta: Some(HashMap::from([("purpose".to_owned(), "x".to_owned())])),
            display_name: Some("svc".to_owned()),
            explicit_max_ttl: Some(Duration::from_secs(600)),
            no_default_policy: Some(true),
            no_parent: Some(true),
            id: Some("preset".to_owned()),
            r#type: Some("service".to_owned()),
            child_visible: Some(false),
            use_result: false,
        };
        assert_eq!(
            serialise(&request),
            serde_json::json!({
                "policies": ["a"], "ttl": 60, "period": 120, "num_uses": 3, "renewable": false,
                "meta": { "purpose": "x" }, "display_name": "svc", "explicit_max_ttl": 600,
                "no_default_policy": true, "no_parent": true, "id": "preset", "type": "service",
                "child_visible": false
            })
        );
    }

    fn lookup_data(creation_ttl: i64, creation_time: i64) -> Option<Response> {
        Some(Response {
            data: Some(Map::from_iter([
                ("id".to_owned(), Value::from(crate::fake_tokens::CLIENT)),
                ("policies".to_owned(), serde_json::json!(["default"])),
                ("path".to_owned(), Value::from("auth/userpass/login/alice")),
                ("meta".to_owned(), serde_json::json!({ "username": "alice" })),
                ("display_name".to_owned(), Value::from("alice")),
                ("num_uses".to_owned(), Value::from(0)),
                ("ttl".to_owned(), Value::from(0)),
                ("creation_time".to_owned(), Value::from(creation_time)),
                ("creation_ttl".to_owned(), Value::from(creation_ttl)),
                ("explicit_max_ttl".to_owned(), Value::from(0)),
            ])),
            auth: None,
            lease_id: None,
            renewable: None,
            lease_duration: None,
            warnings: Vec::new(),
            status_code: 200,
            headers: HashMap::new(),
            raw: Value::Null,
        })
    }

    /// A clock frozen at a stated wall-clock reading, which is what `AUT-014` needs and
    /// what D-M2-2 added to the trait.
    #[derive(Debug)]
    struct FrozenClock(SystemTime);

    impl crate::Clock for FrozenClock {
        fn now_monotonic(&self) -> std::time::Instant {
            std::time::Instant::now()
        }

        fn now_utc(&self) -> SystemTime {
            self.0
        }

        fn delay(
            &self,
            _duration: Duration,
        ) -> std::pin::Pin<Box<dyn std::future::Future<Output = ()> + Send + 'static>> {
            Box::pin(std::future::ready(()))
        }
    }

    fn client_at(now_unix: i64) -> Client {
        let config = crate::ClientConfigBuilder::new()
            .with_environment(crate::EnvironmentSource::None)
            .address("https://vault.example.com:8200")
            .clock(std::sync::Arc::new(FrozenClock(from_unix_seconds(now_unix))))
            .build()
            .expect("valid config");
        Client::new(config).expect("valid client")
    }

    /// **`auth.token.lookup-self-remaining-ttl` cannot pin this formula, so this test must.**
    ///
    /// That fixture's `clock.start` is *exactly* its `creation_time`, so
    /// `creation_time + creation_ttl − now` degenerates to `creation_ttl` and substituting
    /// the bare field leaves the fixture green. It proves the clock is injected and read —
    /// which is what D-M2-7's instrument is for — and nothing about the arithmetic. (Found
    /// by the Python parity pass. Not fixed by editing the fixture: that is a `FIX-012`
    /// specification change and the Strategic tree's.)
    ///
    /// So every case below offsets `now_utc` from `creation_time`, and each asserts a value
    /// that is **not** equal to `creation_ttl` — which is what makes the three plausible
    /// wrong implementations (return `creation_ttl`, return the wire `ttl`, forget the
    /// clock) all fail here.
    #[test]
    fn remaining_ttl_is_creation_time_plus_creation_ttl_minus_now_aut_014() {
        const CREATED: i64 = 1_789_300_800;
        const TTL: u64 = 3600;
        for elapsed in [1_u64, 1800, 3599] {
            let client = client_at(CREATED + elapsed as i64);
            let info = read_token_info(lookup_data(TTL as i64, CREATED), "auth/token/lookup-self", &client)
                .expect("the envelope carries data");
            let expected = Duration::from_secs(TTL - elapsed);
            assert_eq!(
                info.remaining_ttl,
                Some(expected),
                "AUT-014: creation_time + creation_ttl − now_utc, {elapsed}s after creation"
            );
            assert_ne!(
                info.remaining_ttl,
                Some(Duration::from_secs(TTL)),
                "a value equal to creation_ttl means the clock term was dropped"
            );
            assert_ne!(info.remaining_ttl, Some(Duration::ZERO), "nor is it the wire ttl, which is always 0");
        }
        // The degenerate case the fixture happens to exercise: at `now == creation_time` the
        // formula does equal `creation_ttl`. Asserted so the fixture's own expectation is
        // reproduced here too, and labelled so nobody mistakes it for the formula's proof.
        let client = client_at(CREATED);
        let info = read_token_info(lookup_data(TTL as i64, CREATED), "auth/token/lookup-self", &client)
            .expect("data");
        assert_eq!(info.remaining_ttl, Some(Duration::from_secs(TTL)));
    }

    /// The other half of the formula: it reads `creation_time` from the response, not the
    /// clock's own epoch. A token created *before* the clock's start still computes
    /// correctly, which a "now + creation_ttl" implementation would get wrong.
    #[test]
    fn remaining_ttl_reads_creation_time_from_the_response_aut_014() {
        let client = client_at(1_789_300_800);
        // Created two hours before "now", with a three-hour TTL: one hour left.
        let info = read_token_info(
            lookup_data(10_800, 1_789_300_800 - 7200),
            "auth/token/lookup-self",
            &client,
        )
        .expect("data");
        assert_eq!(info.remaining_ttl, Some(Duration::from_secs(3600)));
    }

    #[test]
    fn remaining_ttl_is_absent_when_creation_ttl_is_zero_and_never_reads_the_wire_ttl_aut_014() {
        let client = client_at(1_789_300_800);
        let info = read_token_info(lookup_data(0, 1_789_300_800), "auth/token/lookup-self", &client)
            .expect("data");
        assert_eq!(info.remaining_ttl, None, "creation_ttl == 0 means no TTL at all");
        assert_eq!(info.creation_ttl, Duration::ZERO);
        // `ttl` is always 0 on the wire and the specification forbids exposing it: there is
        // no field on `TokenInfo` to read it from, which is the assertion. `creation_time`
        // *is* exposed, and is the wire value.
        assert_eq!(info.creation_time, Some(from_unix_seconds(1_789_300_800)));
    }

    #[test]
    fn an_expired_token_reports_zero_remaining_not_a_negative_duration_aut_014() {
        let client = client_at(1_789_300_800 + 7200);
        let info = read_token_info(lookup_data(3600, 1_789_300_800), "auth/token/lookup-self", &client)
            .expect("data");
        assert_eq!(info.remaining_ttl, Some(Duration::ZERO));
    }

    #[test]
    fn a_lookup_id_is_a_redacting_secret_type_d_m2_12() {
        let client = client_at(1_789_300_800);
        let info = read_token_info(lookup_data(3600, 1_789_300_800), "auth/token/lookup-self", &client)
            .expect("data");
        let id = info.id.as_ref().expect("the lookup carried an id");
        assert_eq!(id.reveal(), crate::fake_tokens::CLIENT);
        assert_eq!(format!("{id}"), "[REDACTED]");
        assert!(!format!("{info:?}").contains("FAKEtoken"), "the whole struct's Debug must not leak it");
    }

    #[test]
    fn an_envelope_with_no_data_or_auto_object_is_protocol_002_not_a_fabricated_value_d_m1c_25() {
        let client = client_at(0);
        let error = read_token_info(None, "auth/token/lookup-self", &client).expect_err("must fail");
        assert_eq!(error.code(), "BV-PROTOCOL-002");
        let error = require_auth(None, "auth/token/renew").expect_err("must fail");
        assert_eq!(error.code(), "BV-PROTOCOL-002");
    }

    #[test]
    fn use_rejects_an_empty_or_whitespace_token_aut_020() {
        let client = client_at(0);
        for blank in ["", "   ", "\t"] {
            let error = client
                .auth()
                .token()
                .r#use(SecretString::new(blank))
                .expect_err("AUT-020 refuses a blank token");
            assert_eq!(error.code(), "BV-INPUT-001");
            assert_eq!(error.attempts(), 0);
        }
        assert!(client.auth().current_token().is_none());
    }

    #[test]
    fn use_installs_a_static_source_with_no_network_aut_001_aut_020() {
        let client = client_at(0);
        assert_eq!(client.auth().token_source().kind(), crate::TokenSourceKind::Static);
        client
            .auth()
            .token()
            .r#use(SecretString::new(crate::fake_tokens::USED))
            .expect("a real token is accepted");
        assert_eq!(
            client.auth().current_token().map(|token| token.reveal().to_owned()),
            Some(crate::fake_tokens::USED.to_owned())
        );
        assert_eq!(client.auth().token_source().kind(), crate::TokenSourceKind::Static);
    }

    #[test]
    fn persist_token_with_no_token_is_input_001_rather_than_writing_nothing_cfg_031() {
        let client = client_at(0);
        let error = client.auth().persist_token().expect_err("no token to persist");
        assert_eq!(error.code(), "BV-INPUT-001");
        assert_eq!(
            error.details().get("argument"),
            Some(&crate::error::DetailValue::Str("current_token".to_owned()))
        );
    }

    #[test]
    fn persist_and_forget_round_trip_through_the_configured_token_file_cfg_031_cfg_032() {
        let directory = tempfile::tempdir().expect("a temporary directory");
        let path = directory.path().join(".vault-token");
        let config = crate::ClientConfigBuilder::new()
            .with_environment(crate::EnvironmentSource::None)
            .address("https://vault.example.com:8200")
            .token(crate::fake_tokens::PERSISTED)
            .token_file(path.clone())
            .build()
            .expect("valid config");
        let client = Client::new(config).expect("valid client");
        // CFG-031: nothing has written the file yet — only an explicit call may.
        assert!(!path.exists(), "the SDK must not write the token file as a side effect");
        client.auth().persist_token().expect("persist must succeed");
        assert_eq!(
            std::fs::read_to_string(&path).expect("readable"),
            crate::fake_tokens::PERSISTED
        );
        client.auth().forget_persisted_token().expect("forget must succeed");
        assert!(!path.exists());
        client.auth().forget_persisted_token().expect("absent is success");
    }

    #[test]
    fn the_auth_grouping_of_a_namespace_view_shares_its_parents_token_state_cfg_071() {
        let client = client_at(0);
        let view = client.with_namespace("team-b");
        client.set_token(SecretString::new(crate::fake_tokens::SHARED));
        assert_eq!(
            view.auth().current_token().map(|token| token.reveal().to_owned()),
            Some(crate::fake_tokens::SHARED.to_owned())
        );
        // And token_info, the other half of AUT-004.
        assert!(view.auth().token_info().is_none());
    }

    /// D-M2-8/D-M1c-25: the deferred section-05 surface is **absent**, not stubbed. A
    /// method that exists and throws is the failure mode this asserts against; the only
    /// place a Rust absence is observable is the public-API baseline.
    #[test]
    fn no_deferred_auth_surface_is_stubbed_d_m2_8() {
        let baseline = std::fs::read_to_string(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/public-api-baseline.txt"
        ))
        .expect("the public-API baseline must be readable");
        // Spelled as the identifiers a stub would actually produce. `Cert` on its own is
        // not in the list because `ClientCertificate` (M1a, TLS) legitimately carries it —
        // the deferred surface is `Auth.Cert.Login`, which would read `cert_login`.
        for absent in [
            "cert_login", "FerroGate", "ferrogate", "Fido2", "fido2", "Oidc", "oidc", "Saml",
            "saml", "admin", "authenticate", "Userpass", "userpass", "AppId", "app_id",
        ] {
            assert!(
                !baseline.contains(absent),
                "{absent} is deferred (D-M2-5/D-M2-8) and must be absent from the public surface, not stubbed"
            );
        }
    }
}
