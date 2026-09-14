//! The status -> code mapping function (ERR-020, D-M1b-4/4a/4b/21/23, D-M1c-3/12).
//!
//! This is the **one function** the logical layer calls for every HTTP error status; it
//! populates exactly the D-M1b-4 set and never grows a throwing default arm (D-M1b-21).
//! `Retryable` on the returned [`Error`] always follows ERR-006 — it now comes from the
//! generated catalogue row, which the generator cross-checks against ERR-006's own list
//! at generation time (D-M1c-8) — independent of `RetryPolicy.RetryOn` (D-M1b-4b).
//!
//! M1c inserts ERR-020 **step 4**, full Appendix B §2 message recognition, ahead of the
//! status table (D-M1c-3). It does not reshape the status table or any branch D-M1b-4
//! populated; the single exception is D-M1c-12, which corrects the unmapped-4xx arm from
//! `BV-INPUT-001` to the `BV-INPUT-100` that `04-error-model.md` step 5 names.

use crate::error::Error;
use crate::generated::error_catalog_data::error_codes;
use crate::recognition;
use crate::transport::TransportFailureKind;

/// Maps an HTTP status (plus the two discriminators section 03 itself names) to the
/// single mapped `Error` for that response. Callers attach `StatusCode`, `Method`,
/// `Path`, `Attempts`, `RetryAfter`, `ServerMessage` and `ServerErrors` afterwards — this
/// function only decides the code, message, hint and the ERR-035 `Details` captures.
///
/// - `status` MUST NOT be 200/204/304 (those are success paths handled above this
///   function) nor be a status the caller has already special-cased (e.g. 404 empty
///   body on `Read`/`List`, handled as an absent result before this is called).
/// - `server_message` is the extracted TRN-052 message (joined `errors[]` or singular
///   `error`); normalisation for matching happens inside [`recognition`].
/// - `has_retry_after` is whether the response carried a `Retry-After` header
///   (D-M1b-22: the discriminator is the header's presence, not the code).
/// - `path` is the request path. It is new at M1c and is only read by the Appendix B §2
///   path guards (`(409, recordings)`, D-M1c-14 item 3); the status table does not see
///   it.
pub(crate) fn status_to_code(
    status: u16,
    server_message: Option<&str>,
    has_retry_after: bool,
    path: &str,
) -> Error {
    // ERR-020 step 4 (D-M1c-3): the ordered Appendix B §2 rule list runs ahead of the
    // status table. No match falls through to the table below, unchanged.
    if let Some(recognised) = recognition::recognise(server_message, status, path) {
        return Error::from_catalog(recognised.code).with_details(recognised.details);
    }

    let code = match status {
        // TRN-060/CNF-034: any 3xx other than 304 (304 never reaches here).
        300..=399 => error_codes::PROTOCOL_UNEXPECTED_REDIRECT,

        401 => error_codes::AUTH_UNAUTHENTICATED,
        403 => error_codes::AUTHZ_PERMISSION_DENIED,
        404 => error_codes::NOT_FOUND_PATH_NOT_FOUND,
        405 => error_codes::PROTOCOL_METHOD_NOT_ALLOWED,
        // D-M1c-19: `04-error-model.md` step 5 maps `409` to `BV-CONFLICT-001`. D-M1b-4
        // left 409 out of this table for M1c message recognition to answer, and Appendix
        // B §2 now does answer the digest/sha256 and brokered-credential bodies in step 4
        // above. This arm is what an *unrecognised* 409 falls to, and it is the generic
        // conflict code the specification names rather than a guess at which specific
        // conflict it is.
        409 => error_codes::CONFLICT,
        416 => error_codes::INPUT_CHUNK_INDEX_OUT_OF_RANGE,

        // D-M1b-23: 429 discrimination is the `Retry-After` header's presence.
        429 if has_retry_after => error_codes::RATE_LIMITED_BY_DOS_GUARD,
        429 => error_codes::RATE_NAMESPACE_RATE_QUOTA_EXCEEDED,

        // D-M1c-23: no 503 message discrimination. D-M1b-23 put one here because the
        // hand-transcribed catalogue had no way to recognise a sealed *message*;
        // Appendix B §2 now carries `exact bastionvault is sealed` and
        // `contains (5xx) is sealed`, both of which fire at step 4 above. What the old
        // heuristic still answered was a 503 containing `sealed` but not `is sealed` —
        // a case no fixture covers and no specification row describes.
        // `04-error-model.md` step 5 says `503 => BV-SERVER-002`, flatly.
        502..=504 => error_codes::SERVER_UNAVAILABLE,
        507 => error_codes::QUOTA_NAMESPACE_QUOTA_EXCEEDED,

        // D-M1b-21's principle holds: an unmapped status is still a typed SDK error,
        // never a panic. D-M1c-12 corrects which code the 4xx arm produces —
        // `04-error-model.md` step 5 maps `400` *and* "other 4xx" to `BV-INPUT-100`,
        // which D-M1b-21 could not use because the hand-transcribed catalogue did not
        // carry it. There is deliberately no separate `400` branch: the two spec rows
        // name the same code. `BV-INPUT-001` stays what its message says it is —
        // client-side argument validation, raised before any request.
        400..=499 => error_codes::INPUT_SERVER_REJECTED_REQUEST,
        500..=599 => error_codes::SERVER_INTERNAL_ERROR,
        _ => error_codes::PROTOCOL_UNEXPECTED_RESPONSE,
    };

    Error::from_catalog(code)
}

/// D-M1b-4a: the six fixture `fail` kinds (plus caller cancellation) map to fixed
/// codes. The production transport classifies its own stack's failures into the same
/// set (D-M1b-1).
pub(crate) fn transport_failure_to_error(kind: TransportFailureKind) -> Error {
    let code = match kind {
        TransportFailureKind::ConnectionRefused
        | TransportFailureKind::Dns
        | TransportFailureKind::Reset => error_codes::TRANSPORT_CONNECTION_FAILED,
        TransportFailureKind::Timeout => error_codes::TRANSPORT_TIMEOUT,
        TransportFailureKind::TlsVerify | TransportFailureKind::TlsHandshake => {
            error_codes::TRANSPORT_TLS_ERROR
        }
        TransportFailureKind::Cancelled => error_codes::TRANSPORT_CANCELLED,
    };
    Error::from_catalog(code)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::error_catalog::ErrorCatalog;

    /// The path every test that is not about a path guard uses; no Appendix B §2 scope
    /// matches it.
    const PATH: &str = "secret/data/x";

    fn map(status: u16, message: Option<&str>) -> Error {
        status_to_code(status, message, false, PATH)
    }

    #[test]
    fn three_hundreds_other_than_304_map_to_protocol_003() {
        assert_eq!(map(307, None).code(), "BV-PROTOCOL-003");
        assert_eq!(map(301, None).code(), "BV-PROTOCOL-003");
    }

    #[test]
    fn four_two_nine_discriminates_on_retry_after_presence_not_message() {
        assert_eq!(status_to_code(429, None, true, PATH).code(), "BV-RATE-001");
        assert_eq!(
            status_to_code(429, Some("anything unrecognised"), false, PATH).code(),
            "BV-RATE-002"
        );
    }

    #[test]
    fn a_sealed_503_is_recognised_at_step_4_case_insensitively_err_020() {
        // After D-M1c-23 the status table answers every 503 with BV-SERVER-002, so a
        // BV-SERVER-001 result here can only have come from Appendix B §2's
        // `contains (5xx) is sealed` rule — which is what makes this assertion
        // load-bearing rather than a restatement of the status arm.
        assert_eq!(map(503, Some("The node is Sealed right now")).code(), "BV-SERVER-001");
        assert_eq!(map(503, Some("BastionVault is sealed.")).code(), "BV-SERVER-001");
        assert_eq!(map(503, Some("cluster node is unhealthy")).code(), "BV-SERVER-002");
    }

    #[test]
    fn an_unrecognised_503_maps_to_server_002_with_no_message_heuristic_err_020_d_m1c_23() {
        // D-M1c-23: `04-error-model.md` step 5 says `503 => BV-SERVER-002` flatly. The
        // D-M1b-23 heuristic matched the bare substring `sealed`, so it answered bodies
        // Appendix B §2 does not recognise — no fixture covered that, and no spec row
        // described it.
        assert_eq!(map(503, None).code(), error_codes::SERVER_UNAVAILABLE);
        assert_eq!(map(503, Some("sealed")).code(), error_codes::SERVER_UNAVAILABLE);
        assert_eq!(
            map(503, Some("node resealed during a maintenance window")).code(),
            error_codes::SERVER_UNAVAILABLE
        );
        assert_eq!(map(502, None).code(), error_codes::SERVER_UNAVAILABLE);
        assert_eq!(map(504, None).code(), error_codes::SERVER_UNAVAILABLE);
        // ERR-006: BV-SERVER-002 is retryable, BV-SERVER-001 is not — the two arms are
        // not interchangeable and the change is observable on `Retryable`.
        assert!(map(503, Some("sealed")).retryable());
    }

    #[test]
    fn unmapped_4xx_falls_back_to_input_100_never_panics_d_m1b_21_d_m1c_12() {
        // D-M1c-12 amends D-M1b-21: `04-error-model.md` step 5 maps `400` and every other
        // unmapped 4xx to `BV-INPUT-100 ServerRejectedRequest`, which the generated
        // catalogue now carries. The principle D-M1b-21 stands on is unchanged — an
        // unmapped status is still a typed SDK error.
        assert_eq!(map(400, None).code(), "BV-INPUT-100");
        assert_eq!(map(400, Some("unexplained")).code(), "BV-INPUT-100");
        assert_eq!(map(418, Some("unexplained teapot")).code(), "BV-INPUT-100");
    }

    #[test]
    fn an_unrecognised_409_maps_to_the_generic_conflict_code_err_020_d_m1c_19() {
        // D-M1c-19: `04-error-model.md` step 5 names `BV-CONFLICT-001` for 409. Before
        // this ruling Rust fell through to the unmapped-4xx arm and .NET guessed
        // `BV-CONFLICT-002` from a heuristic no fixture reached; neither matched the
        // specification.
        assert_eq!(map(409, None).code(), error_codes::CONFLICT);
        assert_eq!(map(409, Some("")).code(), error_codes::CONFLICT);
        assert_eq!(
            map(409, Some("a conflict the catalogue has never heard of")).code(),
            error_codes::CONFLICT
        );
        // Off a recordings path the `(409, recordings)` guard fails, so even a digest
        // body now lands on the generic conflict code rather than on the 4xx arm.
        assert_eq!(
            status_to_code(409, Some("sha256 mismatch"), false, "rustion/blobs/a").code(),
            error_codes::CONFLICT
        );
        assert_eq!(ErrorCatalog::require(error_codes::CONFLICT).category(), crate::error::ErrorCategory::Conflict);
        assert!(!ErrorCatalog::require(error_codes::CONFLICT).retryable());
    }

    #[test]
    fn unmapped_5xx_falls_back_to_server_005_d_m1b_21() {
        assert_eq!(map(599, None).code(), "BV-SERVER-005");
        assert_eq!(map(500, Some("unexplained")).code(), "BV-SERVER-005");
    }

    #[test]
    fn everything_else_falls_back_to_protocol_002_d_m1b_21() {
        assert_eq!(map(100, None).code(), "BV-PROTOCOL-002");
    }

    #[test]
    fn dedicated_branches_for_401_403_404_and_507_are_reachable() {
        assert_eq!(map(401, Some("authentication required by this listener")).code(), "BV-AUTH-002");
        assert_eq!(map(403, Some("forbidden by the listener")).code(), "BV-AUTHZ-001");
        assert_eq!(map(404, Some("nothing here")).code(), "BV-NOTFOUND-001");
        assert_eq!(map(405, Some("method rejected")).code(), "BV-PROTOCOL-001");
        assert_eq!(map(507, Some("out of capacity")).code(), "BV-QUOTA-001");
        assert_eq!(map(416, None).code(), "BV-INPUT-008");
    }

    #[test]
    fn recognition_runs_before_the_status_table_err_020_step_4() {
        // The DoS-guard message wins over the status table's `429 without Retry-After`
        // arm, which would otherwise say BV-RATE-002 (D-M1c-3).
        assert_eq!(
            map(429, Some("request temporarily blocked by DoS protection")).code(),
            "BV-RATE-001"
        );
        // CNF-043 / D-M1c-6: one recognition row, no bespoke code path.
        assert_eq!(map(500, Some("Logical backend path not supported.")).code(), "BV-SERVER-004");
        assert_eq!(map(403, Some("Permission denied.")).code(), "BV-AUTHZ-001");
    }

    #[test]
    fn a_status_class_guard_keeps_a_5xx_only_rule_off_a_4xx() {
        // `exact bastionvault is sealed` / `contains (5xx) is sealed`.
        assert_eq!(map(503, Some("The vault is sealed right now.")).code(), "BV-SERVER-001");
        assert_eq!(map(400, Some("The vault is sealed right now.")).code(), "BV-INPUT-100");
    }

    #[test]
    fn the_409_recordings_rule_is_scoped_to_a_recordings_path_d_m1c_14_3() {
        assert_eq!(
            status_to_code(409, Some("sha256 mismatch"), false, "rustion/recordings/a/chunk/0").code(),
            "BV-CONFLICT-002"
        );
        // Off a recordings path the guard fails and the D-M1c-19 `409` arm answers
        // instead — the rule is scoped, not global.
        assert_eq!(
            status_to_code(409, Some("sha256 mismatch"), false, "rustion/blobs/a").code(),
            error_codes::CONFLICT
        );
    }

    #[test]
    fn a_prefix_rule_keeps_the_appendix_trailing_space_and_a_compound_rule_needs_both_parts() {
        // `prefix "key "` + contains `already exists with type`. The trailing space in
        // Appendix B's literal is load-bearing: it stops the rule swallowing `key_name`
        // (D-M1c-14 item 2).
        assert_eq!(
            map(400, Some("Key app already exists with type aes256-gcm96")).code(),
            "BV-TRANSIT-002"
        );
        assert_eq!(
            map(400, Some("key_name already exists with type aes256-gcm96")).code(),
            "BV-INPUT-100"
        );
        assert_eq!(map(400, Some("Key app is fine")).code(), "BV-INPUT-100");
    }

    #[test]
    fn every_d_m1c_4_capture_shape_extracts_what_the_decision_pins_err_035() {
        use crate::error::DetailValue;

        let policy = map(403, Some("Cannot assign policy app-admin: not granted."));
        assert_eq!(policy.code(), "BV-AUTHZ-001");
        assert_eq!(
            policy.details().get("policy"),
            Some(&DetailValue::Str("app-admin".to_owned()))
        );

        let locked = map(400, Some("Account temporarily locked (retry after 300s)."));
        assert_eq!(
            locked.details().get("retry_after_secs"),
            Some(&DetailValue::Int(300))
        );

        let source = map(403, Some("Source address 203.0.113.17 is unauthorized."));
        assert_eq!(
            source.details().get("source_ip"),
            Some(&DetailValue::Str("203.0.113.17".to_owned()))
        );

        let batch = map(400, Some("Batch has 200 operations, exceeds max 128."));
        assert_eq!(batch.details().get("count"), Some(&DetailValue::Int(200)));
        assert_eq!(batch.details().get("max"), Some(&DetailValue::Int(128)));

        let meta = map(400, Some("Meta key(s) username, spiffe_id are reserved."));
        assert_eq!(
            meta.details().get("keys"),
            Some(&DetailValue::List(vec![
                "username".to_owned(),
                "spiffe_id".to_owned()
            ]))
        );

        let namespace = map(404, Some("No such namespace dti/esi."));
        assert_eq!(
            namespace.details().get("namespace"),
            Some(&DetailValue::Str("dti/esi".to_owned()))
        );

        let named = map(404, Some("No policy named app-read."));
        assert_eq!(
            named.details().get("policy"),
            Some(&DetailValue::Str("app-read".to_owned()))
        );
    }

    #[test]
    fn a_capture_that_cannot_match_is_not_an_error_the_code_still_lands_d_m1c_4() {
        for (status, message, code, key) in [
            (400_u16, "Account temporarily locked", error_codes::AUTH_ACCOUNT_LOCKED, "retry_after_secs"),
            (400, "Batch has many operations, exceeds max quota", error_codes::INPUT_BATCH_TOO_LARGE, "count"),
            (400, "Meta key(s)  are reserved", error_codes::INPUT_RESERVED_TOKEN_META_KEY, "keys"),
            (404, "No policy named ", error_codes::NOT_FOUND_POLICY_NOT_FOUND, "policy"),
        ] {
            let error = map(status, Some(message));
            assert_eq!(error.code(), code, "{message}");
            assert!(!error.details().contains_key(key), "{message}");
        }
    }

    #[test]
    fn retryable_follows_err_006_independent_of_code_populated_here() {
        assert!(map(503, Some("cluster node is unhealthy")).retryable()); // SERVER-002
        // Recognised at step 4 (`contains (5xx) is sealed`), not by the status arm.
        assert!(!map(503, Some("The node is sealed")).retryable()); // SERVER-001
        assert!(map(429, Some("unrecognised")).retryable()); // RATE-002
        assert!(!status_to_code(429, None, true, PATH).retryable()); // RATE-001
    }

    #[test]
    fn the_conflict_codes_d_m1b_4_populated_are_reached_by_recognition_at_m1c() {
        // M1b shipped these catalogue entries with no way to reach them; D-M1c-3 wires
        // them to Appendix B §2 rows, so the M1b `#[allow(dead_code)]` constructors are
        // gone rather than left as dead code (D-M1c-14 item 9).
        //
        // These also prove step 4 still answers first after D-M1c-19: the status table's
        // new `409` arm produces BV-CONFLICT-001, so a BV-CONFLICT-002/003 result can
        // only have come from recognition.
        assert!(ErrorCatalog::get("BV-CONFLICT-002").is_some());
        assert_eq!(
            status_to_code(409, Some("digest mismatch"), false, "rustion/recordings/a").code(),
            "BV-CONFLICT-002"
        );
        assert_eq!(
            map(400, Some("brokered_resource_no_static_credential")).code(),
            "BV-CONFLICT-003"
        );
    }

    #[test]
    fn transport_failure_kinds_map_to_the_fixed_d_m1b_4a_codes() {
        for (kind, code) in [
            (TransportFailureKind::ConnectionRefused, "BV-TRANSPORT-001"),
            (TransportFailureKind::Dns, "BV-TRANSPORT-001"),
            (TransportFailureKind::Reset, "BV-TRANSPORT-001"),
            (TransportFailureKind::Timeout, "BV-TRANSPORT-002"),
            (TransportFailureKind::TlsVerify, "BV-TRANSPORT-003"),
            (TransportFailureKind::TlsHandshake, "BV-TRANSPORT-003"),
            (TransportFailureKind::Cancelled, "BV-TRANSPORT-005"),
        ] {
            assert_eq!(transport_failure_to_error(kind).code(), code);
        }
    }
}
