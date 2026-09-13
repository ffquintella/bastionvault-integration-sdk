"""Tests for the injected environment seam (D-M1a-3)."""

from bastionvault_integration_sdk import MapEnvironmentSource, NoneEnvironmentSource, ProcessEnvironmentSource


def test_map_environment_source_reads_only_the_supplied_map() -> None:
    """@req OVR-004"""
    source = MapEnvironmentSource({"BASTIONVAULT_ADDR": "https://mapped:8200"})

    assert source.get("BASTIONVAULT_ADDR") == "https://mapped:8200"
    assert source.get("BASTIONVAULT_MISSING") is None


def test_none_environment_source_reads_nothing() -> None:
    """@req CFG-005"""
    source = NoneEnvironmentSource()

    assert source.get("BASTIONVAULT_ADDR") is None
    assert source.get("PATH") is None


def test_process_environment_source_reads_os_environ_without_mutating_it() -> None:
    """@req OVR-004"""
    source = ProcessEnvironmentSource()

    # A name essentially guaranteed absent: reading it must not mutate the process
    # environment and must return the same thing os.environ.get would.
    import os

    sentinel_name = "BASTIONVAULT_SDK_TEST_UNSET_SENTINEL"
    assert sentinel_name not in os.environ
    assert source.get(sentinel_name) is None
    assert sentinel_name not in os.environ
