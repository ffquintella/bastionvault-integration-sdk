//! ERR-034's path interpolation and ERR-040's context-aware hint notes, in one
//! deterministic function applied once, where the error leaves the retry loop
//! (D-M1c-5, D-M1c-14 items 5 and 7).
//!
//! Only the seven rows of
//! `specifications/04-error-model.md#hint-enrichment-from-context` that are decidable
//! from client-side state alone are here. The two rows that need `Sys.CapabilitiesSelf`
//! (M3) and the `Sys.ListMounts` cache (M4) are **absent, not stubbed**: a stub would be
//! a branch no test can reach and the CNF-010 coverage floor allows no exclusion pragma
//! to excuse it. The owning milestone adds the branch and its fixture (D-M1c-5).
//!
//! Enrichment runs in the request path rather than in [`crate::mapping`] because two of
//! the seven rows fire on transport failures, which never reach the mapper (D-M1c-14
//! item 7).

use std::time::Duration;

use crate::generated::error_catalog_data::error_codes;

/// The address `ClientConfigBuilder` falls back to when none is configured (CFG-010).
pub(crate) const DEFAULT_ADDRESS: &str = "https://127.0.0.1:8200";

const NO_NAMESPACE_NOTE: &str =
    "No namespace is set; if the credential is scoped to a namespace, set `Namespace`.";
const API_VERSION_NOTE: &str = "Pin this call to `/v2` (RequestOptions.ApiVersion = 2).";
const SEALED_NOTE: &str =
    "Run `bvault operator unseal` on the node or wait for auto-unseal; the SDK will not retry.";
const UNSUPPORTED_PATH_NOTE: &str = "This server version does not have this endpoint; check \
                                     `Client.ServerVersion()` and use the documented fallback.";
const TLS_NO_CA_NOTE: &str =
    "Provide the server's CA bundle via `CaCertPath` or `BASTIONVAULT_CACERT`.";
const CONNECTION_REFUSED_NOTE: &str = "No `Address` was configured; the default is \
                                       `https://127.0.0.1:8200`. Set `Address` or \
                                       `BASTIONVAULT_ADDR`.";

/// The client-side facts the seven landed rows need; nothing about retry state.
pub(crate) struct Context<'a> {
    pub(crate) status_code: Option<u16>,
    pub(crate) retry_after: Option<Duration>,
    pub(crate) path: &'a str,
    pub(crate) active_namespace: &'a str,
    pub(crate) has_ca_certificate: bool,
    pub(crate) address: &'a str,
}

/// ERR-034: a hint that points at `Details.path` must name the path the SDK actually
/// sent, because most "not found" problems are mount or prefix mistakes. A distinct step
/// before the ERR-040 table, so that table stays exactly the seven D-M1c-5 rows
/// (D-M1c-14 item 5).
pub(crate) fn interpolate_path(hint: &str, redacted_path: &str) -> String {
    if redacted_path.is_empty() || !hint.contains("Details.path") {
        return hint.to_owned();
    }
    append(hint, &format!("The path as sent was `{redacted_path}`."))
}

/// Appends the ERR-040 notes whose condition holds, in the order `04-error-model.md`
/// lists them, separated by a single space. Never rewrites the catalogue hint (D-M1c-5).
pub(crate) fn enrich(code: &str, hint: &str, context: &Context<'_>) -> String {
    let mut result = hint.to_owned();

    if context.status_code == Some(403)
        && context.active_namespace.is_empty()
        && is_under_namespace_scoped_mount(context.path)
    {
        result = append(&result, NO_NAMESPACE_NOTE);
    }

    if context.status_code == Some(400) && code == error_codes::SERVER_API_VERSION_MISMATCH {
        result = append(&result, API_VERSION_NOTE);
    }

    if context.status_code == Some(429)
        && let Some(retry_after) = context.retry_after
    {
        let seconds = (retry_after.as_secs_f64()).round() as i64;
        result = append(
            &result,
            &format!(
                "The client rate gate is now paused for `{seconds}`s; reduce request fan-out \
                 (use `Sys.Batch` or `*-info` pages)."
            ),
        );
    }

    if context.status_code == Some(503) && code == error_codes::SERVER_SEALED {
        result = append(&result, SEALED_NOTE);
    }

    if context.status_code == Some(500) && code == error_codes::SERVER_UNSUPPORTED_BY_SERVER {
        result = append(&result, UNSUPPORTED_PATH_NOTE);
    }

    // The SDK maps a handshake failure and a verification failure to the same
    // BV-TRANSPORT-003 (D-M1b-4a), so the trigger is the code plus "no CA configured"
    // rather than a distinction the error does not carry (D-M1c-14 item 8).
    if code == error_codes::TRANSPORT_TLS_ERROR && !context.has_ca_certificate {
        result = append(&result, TLS_NO_CA_NOTE);
    }

    // "Connection refused to default address": the resolved address is the observable
    // client-side fact; `ClientConfig` does not record whether it was defaulted, and
    // adding a flag to it would be a public API change this row does not justify
    // (D-M1c-14 item 8).
    if code == error_codes::TRANSPORT_CONNECTION_FAILED && context.address == DEFAULT_ADDRESS {
        result = append(&result, CONNECTION_REFUSED_NOTE);
    }

    result
}

fn is_under_namespace_scoped_mount(path: &str) -> bool {
    // The display path may carry the `[ns=…] ` prefix (ERR-001's `Path`), but this row
    // only fires when the namespace is empty, in which case there is no prefix.
    let trimmed = path.trim_start_matches('/');
    trimmed.starts_with("auth/") || trimmed.starts_with("secret/")
}

/// Appends one note after a single space. No guard against a repeat: each ERR-040 row is
/// tested once per error and the rows are disjoint, so a duplicate is unreachable and
/// would only be dead code (D-M1c-5).
fn append(hint: &str, note: &str) -> String {
    format!("{} {note}", hint.trim_end())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn context() -> Context<'static> {
        Context {
            status_code: None,
            retry_after: None,
            path: "secret/data/x",
            active_namespace: "",
            has_ca_certificate: false,
            address: "https://vault.example.com:8200",
        }
    }

    #[test]
    fn a_hint_without_details_path_or_without_a_path_is_left_alone() {
        assert_eq!(interpolate_path("no marker here", "secret/x"), "no marker here");
        assert_eq!(
            interpolate_path("check `Details.path`", ""),
            "check `Details.path`"
        );
        assert_eq!(
            interpolate_path("check `Details.path`", "secret/x"),
            "check `Details.path` The path as sent was `secret/x`."
        );
    }

    #[test]
    fn the_namespace_row_needs_an_auth_or_secret_path_and_an_empty_namespace() {
        let hint = "base";
        let mut ctx = context();
        ctx.status_code = Some(403);
        assert!(enrich("BV-AUTHZ-001", hint, &ctx).contains(NO_NAMESPACE_NOTE));

        ctx.path = "auth/userpass/users/bob";
        assert!(enrich("BV-AUTHZ-001", hint, &ctx).contains(NO_NAMESPACE_NOTE));

        ctx.path = "sys/health";
        assert!(!enrich("BV-AUTHZ-001", hint, &ctx).contains(NO_NAMESPACE_NOTE));

        ctx.path = "secret/data/x";
        ctx.active_namespace = "dti";
        assert!(!enrich("BV-AUTHZ-001", hint, &ctx).contains(NO_NAMESPACE_NOTE));
    }

    #[test]
    fn the_rate_gate_row_rounds_retry_after_to_whole_seconds() {
        let mut ctx = context();
        ctx.status_code = Some(429);
        ctx.retry_after = Some(Duration::from_millis(16_600));
        assert!(enrich("BV-RATE-001", "base", &ctx).contains("paused for `17`s"));

        ctx.retry_after = None;
        assert!(!enrich("BV-RATE-001", "base", &ctx).contains("paused for"));
    }

    #[test]
    fn the_two_transport_rows_key_on_the_code_plus_one_client_side_fact() {
        let mut ctx = context();
        assert!(enrich("BV-TRANSPORT-003", "base", &ctx).contains(TLS_NO_CA_NOTE));
        ctx.has_ca_certificate = true;
        assert!(!enrich("BV-TRANSPORT-003", "base", &ctx).contains(TLS_NO_CA_NOTE));

        assert!(!enrich("BV-TRANSPORT-001", "base", &ctx).contains(CONNECTION_REFUSED_NOTE));
        ctx.address = DEFAULT_ADDRESS;
        assert!(enrich("BV-TRANSPORT-001", "base", &ctx).contains(CONNECTION_REFUSED_NOTE));
    }

    #[test]
    fn the_status_plus_code_rows_need_both_halves() {
        let mut ctx = context();
        ctx.status_code = Some(400);
        assert!(enrich("BV-SERVER-006", "base", &ctx).contains(API_VERSION_NOTE));
        assert!(!enrich("BV-INPUT-100", "base", &ctx).contains(API_VERSION_NOTE));

        ctx.status_code = Some(503);
        assert!(enrich("BV-SERVER-001", "base", &ctx).contains(SEALED_NOTE));
        assert!(!enrich("BV-SERVER-002", "base", &ctx).contains(SEALED_NOTE));

        ctx.status_code = Some(500);
        assert!(enrich("BV-SERVER-004", "base", &ctx).contains(UNSUPPORTED_PATH_NOTE));
        assert!(!enrich("BV-SERVER-005", "base", &ctx).contains(UNSUPPORTED_PATH_NOTE));
    }

    #[test]
    fn the_two_deferred_rows_are_absent_not_stubbed() {
        // D-M1c-5: no branch exists for `Details.namespace_operable == false` (M3) or for
        // a 404 on a KV v2 mount (M4), so neither can fire early.
        let mut ctx = context();
        ctx.status_code = Some(404);
        ctx.path = "secret/app/db";
        let hint = enrich("BV-NOTFOUND-001", "base", &ctx);
        assert!(!hint.contains("This mount is KV v2"));
        assert!(!hint.contains("child-visible"));
    }
}
