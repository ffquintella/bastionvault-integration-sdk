//! The logging seam (D-M1a-17): a public logger trait with a no-op default, so CNF-030's
//! warning is observable in tests without capturing global stdout.

use std::sync::Arc;

/// A runtime-idiomatic logging hook (the `Logger` setting).
pub trait ClientLogger: std::fmt::Debug + Send + Sync {
    /// A warning-level log line, e.g. CNF-030's "TLS verification disabled" warning.
    fn warn(&self, message: &str);
}

/// The default `Logger`: discards every message.
#[derive(Debug, Clone, Copy, Default)]
pub struct NoopLogger;

impl ClientLogger for NoopLogger {
    fn warn(&self, _message: &str) {}
}

pub(crate) fn default_logger() -> Arc<dyn ClientLogger> {
    Arc::new(NoopLogger)
}
