use bastionvault_integration_sdk::{sdk_version, specification_version};

#[test]
fn exposes_spec_and_sdk_version_functions_cnf_041() {
    assert_eq!(specification_version(), "1.0.0");
    assert_eq!(sdk_version(), env!("CARGO_PKG_VERSION"));
    assert_eq!(sdk_version(), "0.5.0");
}
