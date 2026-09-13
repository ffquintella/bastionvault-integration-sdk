#![allow(dead_code)]
// `ActualError` is a test-only, language-neutral comparison shape (M0); returning it by
// value from a handful of fixture-driver functions is not a hot path worth boxing.
#![allow(clippy::result_large_err)]

pub mod driver;
pub mod fixture;
pub mod mock_server;
pub mod raw_client;
pub mod transport;
