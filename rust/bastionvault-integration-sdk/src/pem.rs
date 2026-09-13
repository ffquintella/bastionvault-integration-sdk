//! Minimal PEM block extraction (CFG-014, D-M1a-7).
//!
//! M1a parses TLS material to *prove it is well-formed PEM* and to materialise a parsed
//! collection onto `ClientConfig` (D-M1a-14). It does not build a live TLS stack, so a
//! full X.509 parser is over-building for this milestone; base64-decoding each
//! `BEGIN`/`END` block is enough to catch the failure this ID cares about (not PEM at
//! all, or an empty body — D-M1a-17).

use base64::engine::general_purpose::STANDARD as BASE64;
use base64::Engine as _;

pub(crate) struct PemBlock {
    pub(crate) label: String,
    pub(crate) der: Vec<u8>,
}

pub(crate) fn parse_blocks(pem_text: &str) -> Result<Vec<PemBlock>, String> {
    let mut blocks = Vec::new();
    let mut rest = pem_text;
    while let Some(begin_at) = rest.find("-----BEGIN ") {
        let after_begin = &rest[begin_at + "-----BEGIN ".len()..];
        let label_end = after_begin
            .find("-----")
            .ok_or_else(|| "unterminated BEGIN header".to_owned())?;
        let label = after_begin[..label_end].to_owned();
        let body_start = begin_at + "-----BEGIN ".len() + label_end + "-----".len();
        let after_header = &rest[body_start..];
        let end_marker = format!("-----END {label}-----");
        let end_at = after_header
            .find(&end_marker)
            .ok_or_else(|| format!("missing END marker for {label}"))?;
        let body: String = after_header[..end_at]
            .chars()
            .filter(|c| !c.is_whitespace())
            .collect();
        let der = BASE64
            .decode(body.as_bytes())
            .map_err(|error| format!("invalid base64 in {label} block: {error}"))?;
        blocks.push(PemBlock { label, der });
        rest = &after_header[end_at + end_marker.len()..];
    }
    Ok(blocks)
}

/// Parses `pem_text` and returns the DER bodies of every block whose label contains
/// `"CERTIFICATE"`. An empty result (no blocks, or none of them certificates) is the
/// caller's cue to raise `BV-CONFIG-006` (D-M1a-17: a PEM body that parses to zero
/// certificates is not success).
pub(crate) fn parse_certificates(pem_text: &str) -> Result<Vec<Vec<u8>>, String> {
    Ok(parse_blocks(pem_text)?
        .into_iter()
        .filter(|block| block.label.contains("CERTIFICATE"))
        .map(|block| block.der)
        .collect())
}

/// Parses `pem_text` and returns the DER bodies of every block whose label contains
/// `"PRIVATE KEY"`.
pub(crate) fn parse_private_keys(pem_text: &str) -> Result<Vec<Vec<u8>>, String> {
    Ok(parse_blocks(pem_text)?
        .into_iter()
        .filter(|block| block.label.contains("PRIVATE KEY"))
        .map(|block| block.der)
        .collect())
}

#[cfg(test)]
mod tests {
    use super::*;

    const SAMPLE_CERT: &str = "-----BEGIN CERTIFICATE-----\nSGVsbG8sIHdvcmxkIQ==\n-----END CERTIFICATE-----\n";
    const SAMPLE_KEY: &str =
        "-----BEGIN PRIVATE KEY-----\nSGVsbG8sIHdvcmxkIQ==\n-----END PRIVATE KEY-----\n";

    #[test]
    fn parses_a_well_formed_certificate_block() {
        let certs = parse_certificates(SAMPLE_CERT).expect("must parse");
        assert_eq!(certs.len(), 1);
        assert_eq!(certs[0], b"Hello, world!");
    }

    #[test]
    fn parses_a_well_formed_key_block() {
        let keys = parse_private_keys(SAMPLE_KEY).expect("must parse");
        assert_eq!(keys.len(), 1);
    }

    #[test]
    fn empty_body_yields_zero_certificates_not_an_error() {
        let certs = parse_certificates("not pem at all").expect("no BEGIN marker is not itself a parse error");
        assert!(certs.is_empty());
    }

    #[test]
    fn malformed_base64_is_a_parse_error() {
        let malformed = "-----BEGIN CERTIFICATE-----\nnot-base64!!!\n-----END CERTIFICATE-----\n";
        assert!(parse_blocks(malformed).is_err());
    }
}
