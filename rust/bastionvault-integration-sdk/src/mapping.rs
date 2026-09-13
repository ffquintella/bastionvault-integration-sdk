//! The status -> code mapping function (ERR-020, D-M1b-4/4a/4b/21/23).
//!
//! This is the **one function** the logical layer calls for every HTTP error status; it
//! populates exactly the D-M1b-4 set and never grows a throwing default arm (D-M1b-21).
//! `Retryable` on the returned [`Error`] always follows ERR-006 (baked into the
//! `mapping_errors` constructors), independent of `RetryPolicy.RetryOn` (D-M1b-4b).
//!
//! M1c extends this function with full server-message recognition (Appendix B §2) and
//! the remaining code rows; it does not reshape the signature or the populated set
//! (DR-0004 Consequences).

use crate::error::mapping_errors;
use crate::error::Error;
use crate::transport::TransportFailureKind;

/// Maps an HTTP status (plus the two discriminators section 03 itself names) to the
/// single mapped `Error` for that response. Callers attach `StatusCode`, `Method`,
/// `Path`, `Attempts`, `RetryAfter`, `ServerMessage` and `ServerErrors` afterwards — this
/// function only decides the code, message and hint.
///
/// - `status` MUST NOT be 200/204/304 (those are success paths handled above this
///   function) nor be a status the caller has already special-cased (e.g. 404 empty
///   body on `Read`/`List`, handled as an absent result before this is called).
/// - `server_message` is the extracted TRN-052 message (joined `errors[]` or singular
///   `error`), already lower-cased comparison is done internally.
/// - `has_retry_after` is whether the response carried a `Retry-After` header
///   (D-M1b-22: the discriminator is the header's presence, not the code).
pub(crate) fn status_to_code(status: u16, server_message: Option<&str>, has_retry_after: bool) -> Error {
    let message_lower = server_message.unwrap_or_default().to_ascii_lowercase();

    match status {
        // TRN-060/CNF-034: any 3xx other than 304 (304 never reaches here).
        300..=399 => mapping_errors::protocol_unexpected_redirect(),

        401 => mapping_errors::auth_unauthenticated(),
        403 => mapping_errors::authz_permission_denied(),
        404 => mapping_errors::notfound_path_not_found(),
        405 => mapping_errors::protocol_method_not_allowed(),
        416 => mapping_errors::input_chunk_index_out_of_range(),

        // D-M1b-23: 429 discrimination is the `Retry-After` header's presence.
        429 if has_retry_after => mapping_errors::rate_limited_by_dos_guard(),
        429 => mapping_errors::rate_namespace_quota_exceeded(),

        // D-M1b-23: 503 discrimination is `sealed` in the (case-insensitive) message.
        503 if message_lower.contains("sealed") => mapping_errors::server_sealed(),
        502..=504 => mapping_errors::server_unavailable(),
        507 => mapping_errors::quota_namespace_quota_exceeded(),

        // D-M1b-21: no throwing default arm. Unmapped statuses fall back by class
        // (this also covers `400` and `500`, which have no dedicated branch at M1b —
        // `500`'s fallback happens to be the same code section 03's own table names).
        400..=499 => mapping_errors::input_invalid_argument(),
        500..=599 => mapping_errors::server_internal_error(),
        _ => mapping_errors::protocol_unexpected_response(),
    }
}

/// D-M1b-4a: the six fixture `fail` kinds (plus caller cancellation) map to fixed
/// codes. The production transport classifies its own stack's failures into the same
/// set (D-M1b-1).
pub(crate) fn transport_failure_to_error(kind: TransportFailureKind) -> Error {
    match kind {
        TransportFailureKind::ConnectionRefused
        | TransportFailureKind::Dns
        | TransportFailureKind::Reset => mapping_errors::transport_connection_failed(),
        TransportFailureKind::Timeout => mapping_errors::transport_timeout(),
        TransportFailureKind::TlsVerify | TransportFailureKind::TlsHandshake => {
            mapping_errors::transport_tls_error()
        }
        TransportFailureKind::Cancelled => mapping_errors::transport_cancelled(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn three_hundreds_other_than_304_map_to_protocol_003() {
        assert_eq!(status_to_code(307, None, false).code(), "BV-PROTOCOL-003");
        assert_eq!(status_to_code(301, None, false).code(), "BV-PROTOCOL-003");
    }

    #[test]
    fn four_two_nine_discriminates_on_retry_after_presence_not_message() {
        assert_eq!(status_to_code(429, None, true).code(), "BV-RATE-001");
        assert_eq!(status_to_code(429, Some("anything"), false).code(), "BV-RATE-002");
    }

    #[test]
    fn five_zero_three_discriminates_on_sealed_case_insensitively() {
        assert_eq!(
            status_to_code(503, Some("BastionVault is Sealed."), false).code(),
            "BV-SERVER-001"
        );
        assert_eq!(
            status_to_code(503, Some("cluster has no leader"), false).code(),
            "BV-SERVER-002"
        );
    }

    #[test]
    fn unmapped_4xx_falls_back_to_input_001_never_panics_d_m1b_21() {
        let error = status_to_code(400, None, false);
        assert_eq!(error.code(), "BV-INPUT-001");
        let error = status_to_code(409, None, false);
        assert_eq!(error.code(), "BV-INPUT-001");
    }

    #[test]
    fn unmapped_5xx_falls_back_to_server_005_d_m1b_21() {
        assert_eq!(status_to_code(599, None, false).code(), "BV-SERVER-005");
    }

    #[test]
    fn everything_else_falls_back_to_protocol_002_d_m1b_21() {
        assert_eq!(status_to_code(100, None, false).code(), "BV-PROTOCOL-002");
    }

    #[test]
    fn dedicated_branches_for_401_403_404_and_507_are_reachable() {
        assert_eq!(status_to_code(401, None, false).code(), "BV-AUTH-002");
        assert_eq!(status_to_code(403, None, false).code(), "BV-AUTHZ-001");
        assert_eq!(status_to_code(404, Some("some message"), false).code(), "BV-NOTFOUND-001");
        assert_eq!(status_to_code(507, None, false).code(), "BV-QUOTA-001");
        assert_eq!(status_to_code(416, None, false).code(), "BV-INPUT-008");
    }

    #[test]
    fn conflict_catalogue_entries_carry_the_d_m1b_4_populated_codes_even_though_m1b_never_reaches_them_by_status_alone()
    {
        // D-M1c wires these into message recognition; M1b only ships the catalogue
        // entries themselves (D-M1b-4's populated set names them).
        use crate::error::mapping_errors::{
            conflict_brokered_resource_static_credential, conflict_recording_digest_mismatch,
        };
        assert_eq!(conflict_recording_digest_mismatch().code(), "BV-CONFLICT-002");
        assert_eq!(
            conflict_brokered_resource_static_credential().code(),
            "BV-CONFLICT-003"
        );
    }

    #[test]
    fn retryable_follows_err_006_independent_of_code_populated_here() {
        assert!(status_to_code(503, None, false).retryable()); // SERVER-002
        assert!(!status_to_code(503, Some("sealed"), false).retryable()); // SERVER-001
        assert!(status_to_code(429, None, false).retryable()); // RATE-002
        assert!(!status_to_code(429, None, true).retryable()); // RATE-001
    }

    #[test]
    fn transport_failure_kinds_map_to_the_fixed_d_m1b_4a_codes() {
        assert_eq!(
            transport_failure_to_error(TransportFailureKind::ConnectionRefused).code(),
            "BV-TRANSPORT-001"
        );
        assert_eq!(
            transport_failure_to_error(TransportFailureKind::Dns).code(),
            "BV-TRANSPORT-001"
        );
        assert_eq!(
            transport_failure_to_error(TransportFailureKind::Reset).code(),
            "BV-TRANSPORT-001"
        );
        assert_eq!(
            transport_failure_to_error(TransportFailureKind::Timeout).code(),
            "BV-TRANSPORT-002"
        );
        assert_eq!(
            transport_failure_to_error(TransportFailureKind::TlsVerify).code(),
            "BV-TRANSPORT-003"
        );
        assert_eq!(
            transport_failure_to_error(TransportFailureKind::TlsHandshake).code(),
            "BV-TRANSPORT-003"
        );
        assert_eq!(
            transport_failure_to_error(TransportFailureKind::Cancelled).code(),
            "BV-TRANSPORT-005"
        );
    }
}
