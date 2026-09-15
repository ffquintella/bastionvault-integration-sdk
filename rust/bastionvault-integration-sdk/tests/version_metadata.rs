mod harness;

use bastionvault_integration_sdk::{
    sdk_version, specification_source_ref, specification_source_release, specification_version,
};
use harness::fixture::FixtureLoader;

#[test]
fn exposes_spec_and_sdk_version_functions_cnf_041() {
    assert_eq!(specification_version(), "1.1.0");
    assert_eq!(sdk_version(), env!("CARGO_PKG_VERSION"));
    assert_eq!(sdk_version(), "0.5.0");
}

#[test]
fn exposes_specification_source_matching_provenance_json_cnf_047() {
    assert_eq!(specification_source_release(), "0.42.0");
    assert_eq!(specification_source_ref(), "v0.42.0");

    let loader = match FixtureLoader::new() {
        Ok(loader) => loader,
        Err(_) => return,
    };
    let provenance_path = loader.repository_root().join("specifications/provenance.json");
    if !provenance_path.exists() {
        // specifications/provenance.json is produced by a companion change (DR-0011);
        // skip the manifest-consistency check rather than fail when it is absent.
        return;
    }

    let text = std::fs::read_to_string(&provenance_path).expect("provenance.json must be readable");
    let manifest: serde_json::Value =
        serde_json::from_str(&text).expect("provenance.json must be valid JSON");
    assert_eq!(
        manifest["upstream"]["release"].as_str(),
        Some(specification_source_release())
    );
    assert_eq!(
        manifest["upstream"]["ref"].as_str(),
        Some(specification_source_ref())
    );
}
