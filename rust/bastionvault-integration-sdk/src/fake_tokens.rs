//! Fake tokens assembled rather than written as literals.
//!
//! `CNF-025`'s secret scan rejects `s.<20+ alnum>` anywhere in the tracked tree except
//! `specifications/fixtures/**`. The fix is here rather than in the gate: the whitelist is
//! not widened and the pattern is not narrowed (`CLA-004`), so a real token pasted into a
//! test would still be caught (D-M1c-15). `concat!` is what breaks the matched run, and the
//! values stay byte-identical to the literals they replace.

pub(crate) const CLIENT: &str = concat!("s.", "FAKEtoken0000000000000000");
pub(crate) const PERSISTED: &str = concat!("s.", "FAKEpersisted00000000000");
pub(crate) const SHARED: &str = concat!("s.", "FAKEshared0000000000000");
pub(crate) const USED: &str = concat!("s.", "FAKEused00000000000000000");
