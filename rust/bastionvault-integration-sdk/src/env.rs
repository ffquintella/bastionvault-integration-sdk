//! The environment access seam (D-M1a-3).
//!
//! Resolution never calls the process environment directly; it reads an
//! [`EnvironmentSource`]. This is what makes `EnvironmentSource::None` the CFG-005
//! env-free constructor, what lets fixtures inject an `environment` map (Appendix C),
//! and what keeps every test in this crate free of `std::env::set_var` (OVR-004): a
//! test that wants a controlled environment uses `EnvironmentSource::Map` instead of
//! mutating the real process environment.

use std::collections::BTreeMap;

/// Where resolution reads environment variables from.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub enum EnvironmentSource {
    /// Reads the real process environment. This is the default for
    /// [`crate::ClientConfigBuilder::new`] (CFG-005).
    #[default]
    Process,
    /// Reads nothing; only explicit values and built-in defaults apply. This is
    /// CFG-005's env-ignoring constructor.
    None,
    /// Reads a caller-supplied map — used by tests and by embedders that already have
    /// their own environment abstraction.
    Map(BTreeMap<String, String>),
}

impl EnvironmentSource {
    pub(crate) fn get(&self, name: &str) -> Option<String> {
        match self {
            Self::Process => std::env::var(name).ok(),
            Self::None => None,
            Self::Map(map) => map.get(name).cloned(),
        }
    }

    /// Looks up `names` in order, first match wins (part of CFG-001's precedence:
    /// `BASTIONVAULT_*` before `VAULT_*`).
    pub(crate) fn get_first(&self, names: &[&str]) -> Option<String> {
        names.iter().find_map(|name| self.get(name))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_source_is_process() {
        assert_eq!(EnvironmentSource::default(), EnvironmentSource::Process);
    }

    #[test]
    fn none_source_reads_nothing() {
        let source = EnvironmentSource::None;
        assert_eq!(source.get("PATH"), None);
        assert_eq!(source.get_first(&["BASTIONVAULT_ADDR", "VAULT_ADDR"]), None);
    }

    #[test]
    fn map_source_honours_declared_precedence_order() {
        let mut map = BTreeMap::new();
        map.insert("VAULT_ADDR".to_owned(), "https://compat:8200".to_owned());
        let source = EnvironmentSource::Map(map.clone());
        assert_eq!(
            source.get_first(&["BASTIONVAULT_ADDR", "VAULT_ADDR"]),
            Some("https://compat:8200".to_owned())
        );
        map.insert(
            "BASTIONVAULT_ADDR".to_owned(),
            "https://primary:8200".to_owned(),
        );
        let source = EnvironmentSource::Map(map);
        assert_eq!(
            source.get_first(&["BASTIONVAULT_ADDR", "VAULT_ADDR"]),
            Some("https://primary:8200".to_owned())
        );
    }
}
