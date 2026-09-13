//! TLS parameter materialisation (D-M1a-7/D-M1a-14).
//!
//! M1a reads and PEM-parses trust material and records the values as data on
//! `ClientConfig`. It builds no `rustls::ClientConfig` and no live TLS stack — that is
//! M1b.

/// Minimum negotiated TLS version (CFG-041). BastionVault's floor is 1.2 with 1.3
/// offered; nothing lower is ever recorded.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TlsVersion {
    Tls12,
    Tls13,
}

/// The materialised TLS parameters (CFG-040/041/042). Nothing here is wired into a live
/// TLS stack yet (D-M1a-14).
#[derive(Debug, Clone, PartialEq)]
pub struct TlsParameters {
    pub minimum_version: TlsVersion,
    pub offers_tls13: bool,
    pub server_name: Option<String>,
    pub ca_certificates: Vec<Vec<u8>>,
    pub ca_replaces_system_roots: bool,
    pub client_certificate: Option<ClientCertificate>,
}

impl Default for TlsParameters {
    fn default() -> Self {
        Self {
            minimum_version: TlsVersion::Tls12,
            offers_tls13: true,
            server_name: None,
            ca_certificates: Vec::new(),
            ca_replaces_system_roots: false,
            client_certificate: None,
        }
    }
}

/// A parsed mTLS client certificate + private key pair (CFG-044's data; the "presented
/// on every connection" behaviour is M1b).
#[derive(Debug, Clone, PartialEq)]
pub struct ClientCertificate {
    pub certificate_der: Vec<Vec<u8>>,
    pub private_key_der: Vec<u8>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_tls_parameters_are_the_secure_baseline() {
        let params = TlsParameters::default();
        assert_eq!(params.minimum_version, TlsVersion::Tls12);
        assert!(params.offers_tls13);
        assert!(params.server_name.is_none());
        assert!(params.ca_certificates.is_empty());
        assert!(!params.ca_replaces_system_roots);
        assert!(params.client_certificate.is_none());
    }
}
