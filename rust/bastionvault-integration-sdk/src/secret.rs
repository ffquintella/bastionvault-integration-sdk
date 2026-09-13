//! `SecretString` — a redacting holder for secret-bearing settings (D-M1a-9, CNF-031/032).
//!
//! `Token` (and any future secret setting) is held in this type. Its `Debug` and
//! `Display` forms never contain the value; the only way to read the value back is the
//! explicitly named [`SecretString::reveal`], so every read of secret material is a
//! searchable call site (D-M1a-17).

use std::fmt;

/// A string that redacts itself in every default textual representation.
#[derive(Clone)]
pub struct SecretString {
    value: String,
}

impl SecretString {
    /// Wraps `value` as a secret. The original `value` is moved in and is not copied
    /// anywhere else by this type.
    pub fn new(value: impl Into<String>) -> Self {
        Self {
            value: value.into(),
        }
    }

    /// Explicitly reveals the wrapped secret. Every call site is a place secret
    /// material is read, by design (D-M1a-17).
    pub fn reveal(&self) -> &str {
        &self.value
    }

    /// True when the wrapped value is empty.
    pub fn is_empty(&self) -> bool {
        self.value.is_empty()
    }
}

impl fmt::Debug for SecretString {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("SecretString(\"[REDACTED]\")")
    }
}

impl fmt::Display for SecretString {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("[REDACTED]")
    }
}

impl PartialEq for SecretString {
    fn eq(&self, other: &Self) -> bool {
        self.value == other.value
    }
}

impl Eq for SecretString {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn debug_and_display_never_contain_the_value() {
        let secret = SecretString::new("s.super-secret-token");
        let debug = format!("{secret:?}");
        let display = format!("{secret}");
        assert!(!debug.contains("super-secret-token"));
        assert!(!display.contains("super-secret-token"));
        assert_eq!(secret.reveal(), "s.super-secret-token");
    }

    #[test]
    fn is_empty_and_equality_compare_the_revealed_value() {
        assert!(SecretString::new("").is_empty());
        assert!(!SecretString::new("s.x").is_empty());
        assert_eq!(SecretString::new("same"), SecretString::new("same"));
        assert_ne!(SecretString::new("a"), SecretString::new("b"));
    }
}
