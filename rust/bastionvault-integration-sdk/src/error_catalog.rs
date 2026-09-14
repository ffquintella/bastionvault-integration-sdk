//! The public error catalogue (ERR-036, D-M1c-7).
//!
//! The rows are **generated** into [`crate::generated::error_catalog_data`] from
//! `specifications/appendix-b-error-catalogue.md` §1 by `tools/error-catalogue`
//! (D-M1c-1). Hand-transcription ended at M1c: a code is added by editing the appendix
//! and regenerating, never by editing Rust.
//!
//! The message-recognition and hint-enrichment rules that consume this table stay
//! **internal**: they implement ERR-020 and are not a supported extension point
//! (D-M1c-7).

use std::sync::LazyLock;

use crate::error::ErrorCategory;
use crate::generated::error_catalog_data::ENTRIES;

/// One row of `specifications/appendix-b-error-catalogue.md` §1 (ERR-036).
///
/// The shape is pinned by `decisions/0005-m1c-error-model.md` D-M1c-7: six read-only
/// fields, every string `&'static str`, because the whole table is compiled in.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ErrorCatalogEntry {
    code: &'static str,
    name: &'static str,
    category: ErrorCategory,
    message: &'static str,
    hint: &'static str,
    retryable: bool,
}

impl ErrorCatalogEntry {
    /// Stable identifier, `BV-<CATEGORY>-<NNN>` (ERR-004/ERR-010).
    pub fn code(&self) -> &'static str {
        self.code
    }

    /// The appendix's `Name` column, e.g. `PermissionDenied`.
    pub fn name(&self) -> &'static str {
        self.name
    }

    /// The category this code belongs to.
    pub fn category(&self) -> ErrorCategory {
        self.category
    }

    /// The default English message (ERR-030).
    pub fn message(&self) -> &'static str {
        self.message
    }

    /// The actionable hint (ERR-031); never empty.
    pub fn hint(&self) -> &'static str {
        self.hint
    }

    /// ERR-006's retryability, emitted from the appendix's `R` column and cross-checked
    /// against ERR-006's own list at generation time (D-M1c-8).
    pub fn retryable(&self) -> bool {
        self.retryable
    }
}

/// The compiled table, in Appendix B order, so generated documentation is stable
/// (D-M1c-7). Built once from the generated tuple rows rather than being emitted as
/// `ErrorCatalogEntry` literals, so the generator stays free of Rust visibility rules.
static TABLE: LazyLock<Vec<ErrorCatalogEntry>> = LazyLock::new(|| {
    ENTRIES
        .iter()
        .map(
            |&(code, name, category, message, hint, retryable)| ErrorCatalogEntry {
                code,
                name,
                category,
                message,
                hint,
                retryable,
            },
        )
        .collect()
});

/// The programmatically inspectable code → message → hint table ERR-036 requires.
///
/// Spelled `ErrorCatalog`, not `Catalogue`, because ERR-036 names
/// `ErrorCatalog.Get(code)` (D-M1c-7).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ErrorCatalog;

impl ErrorCatalog {
    /// The entry for `code`, or `None` when no such code exists. Never panics
    /// (D-M1c-7).
    pub fn get(code: &str) -> Option<&'static ErrorCatalogEntry> {
        Self::all().iter().find(|entry| entry.code == code)
    }

    /// Every catalogue row, in Appendix B order (D-M1c-7).
    pub fn all() -> &'static [ErrorCatalogEntry] {
        TABLE.as_slice()
    }

    /// The entry for a code this crate itself raises. Unlike [`ErrorCatalog::get`] this
    /// is an internal contract: a missing code is a generator or wiring defect, not a
    /// caller error, so it is a panic rather than an `Option` every call site would have
    /// to thread through.
    pub(crate) fn require(code: &str) -> &'static ErrorCatalogEntry {
        Self::get(code).expect("every code raised by this crate is in the generated catalogue")
    }
}
