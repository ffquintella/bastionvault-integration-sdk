"""Unit tests for client configuration resolution and validation.

Covers CFG-001..005, CFG-010..018, CFG-030, CFG-040..042, CFG-050, CFG-060/061,
CFG-072, OVR-001, OVR-004, CNF-030, CNF-033, CNF-035 (decisions/0003-m1a-configuration.md).
"""

from __future__ import annotations

import dataclasses
import datetime
from pathlib import Path
from typing import Any

import pytest

from bastionvault_integration_sdk import (
    Client,
    ClientConfig,
    ClientOptions,
    MapEnvironmentSource,
    NoneEnvironmentSource,
    RequestOptions,
    RetryPolicy,
    Transport,
)
from bastionvault_integration_sdk.errors import BastionVaultError, ErrorCodes


class _RecordingLogger:
    def __init__(self) -> None:
        self.warnings: list[str] = []

    def warning(self, message: str) -> None:
        self.warnings.append(message)


class _FakeTransport:
    def request(self, method: str, url: str, headers: Any, body: Any) -> Any:
        raise AssertionError("no request should ever be sent at M1a")


def _generate_cert_and_key(tmp_path: Path) -> tuple[Path, Path]:
    """Build a throwaway self-signed certificate/key pair for CFG-012/013/014 tests."""

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "bastionvault-test")])
    now = datetime.datetime.now(datetime.timezone.utc)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - datetime.timedelta(days=1))
        .not_valid_after(now + datetime.timedelta(days=1))
        .sign(key, hashes.SHA256())
    )
    cert_path = tmp_path / "cert.pem"
    key_path = tmp_path / "key.pem"
    cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    key_path.write_bytes(
        key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
    )
    return cert_path, key_path


# --------------------------------------------------------------------------------
# CFG-001 / CFG-002 — resolution precedence and single-read-at-construction
# --------------------------------------------------------------------------------


def test_cfg_001_resolution_order_explicit_then_bastionvault_then_vault_then_default() -> None:
    """@req CFG-001"""
    default_config = ClientConfig.resolve(ClientOptions(), NoneEnvironmentSource())
    assert default_config.address == "https://127.0.0.1:8200"

    vault_only = ClientConfig.resolve(
        ClientOptions(), MapEnvironmentSource({"VAULT_ADDR": "https://from-vault:8200"})
    )
    assert vault_only.address == "https://from-vault:8200"

    both_set = ClientConfig.resolve(
        ClientOptions(),
        MapEnvironmentSource(
            {"BASTIONVAULT_ADDR": "https://from-bastionvault:8200", "VAULT_ADDR": "https://from-vault:8200"}
        ),
    )
    assert both_set.address == "https://from-bastionvault:8200"

    explicit_wins = ClientConfig.resolve(
        ClientOptions(address="https://explicit:8200"),
        MapEnvironmentSource({"BASTIONVAULT_ADDR": "https://from-bastionvault:8200"}),
    )
    assert explicit_wins.address == "https://explicit:8200"


def test_cfg_001_full_settings_table_resolves_rate_gate_and_auto_renew() -> None:
    """@req CFG-001"""
    config = ClientConfig.resolve(
        ClientOptions(),
        MapEnvironmentSource({"BASTIONVAULT_RATE_PER_SEC": "20", "BASTIONVAULT_RATE_BURST": "40"}),
    )

    assert config.rate_gate.rate_per_second == 20
    assert config.rate_gate.burst == 40
    assert config.auto_renew.enabled is False


@pytest.mark.parametrize("setting", ["BASTIONVAULT_RATE_PER_SEC", "BASTIONVAULT_RATE_BURST"])
def test_cfg_001_rate_gate_rejects_negative_values(setting: str) -> None:
    """@req CFG-001"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(), MapEnvironmentSource({setting: "-1"}))
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE


def test_cfg_001_rate_gate_rejects_unparsable_values() -> None:
    """@req CFG-001"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_RATE_PER_SEC": "lots"}))
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert excinfo.value.details["setting"] == "RateGate.RatePerSecond"


def test_cfg_001_rate_gate_zero_disables_it() -> None:
    """@req CFG-001"""
    config = ClientConfig.resolve(
        ClientOptions(),
        MapEnvironmentSource({"BASTIONVAULT_RATE_PER_SEC": "0", "BASTIONVAULT_RATE_BURST": "0"}),
    )
    assert config.rate_gate.rate_per_second == 0
    assert config.rate_gate.burst == 0


def test_cfg_001_explicit_rate_gate_bypasses_environment() -> None:
    """@req CFG-001"""
    from bastionvault_integration_sdk import RateGate

    explicit = RateGate(rate_per_second=1, burst=2)
    config = ClientConfig.resolve(
        ClientOptions(rate_gate=explicit), MapEnvironmentSource({"BASTIONVAULT_RATE_PER_SEC": "99"})
    )
    assert config.rate_gate is explicit


def test_cfg_001_no_cluster_discovery_env_var_is_inverted() -> None:
    """@req CFG-001"""
    disabled = ClientConfig.resolve(
        ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_NO_CLUSTER_DISCOVERY": "1"})
    )
    assert disabled.cluster_discovery is False

    left_enabled = ClientConfig.resolve(
        ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_NO_CLUSTER_DISCOVERY": "0"})
    )
    assert left_enabled.cluster_discovery is True

    default_enabled = ClientConfig.resolve(ClientOptions(), NoneEnvironmentSource())
    assert default_enabled.cluster_discovery is True


def test_cfg_001_no_cluster_discovery_malformed_value_raises() -> None:
    """@req CFG-001"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(
            ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_NO_CLUSTER_DISCOVERY": "sure"})
        )
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert excinfo.value.details["setting"] == "ClusterDiscovery"


def test_cfg_001_explicit_cluster_discovery_bypasses_environment() -> None:
    """@req CFG-001"""
    config = ClientConfig.resolve(
        ClientOptions(cluster_discovery=False),
        MapEnvironmentSource({"BASTIONVAULT_NO_CLUSTER_DISCOVERY": "0"}),
    )
    assert config.cluster_discovery is False


def test_cfg_002_environment_is_read_once_at_construction_not_lazily() -> None:
    """@req CFG-002"""
    values = {"BASTIONVAULT_ADDR": "https://first:8200"}
    config = ClientConfig.resolve(ClientOptions(), MapEnvironmentSource(values))
    assert config.address == "https://first:8200"

    values["BASTIONVAULT_ADDR"] = "https://second:8200"

    # ClientConfig is frozen and was fully computed already; mutating the backing map
    # afterwards must not change anything already resolved.
    assert config.address == "https://first:8200"


# --------------------------------------------------------------------------------
# CFG-003 / CFG-004 — shared boolean and duration parsers
# --------------------------------------------------------------------------------


@pytest.mark.parametrize("raw,expected", [("1", True), ("true", True), ("YES", True), ("On", True)])
def test_cfg_003_boolean_parser_accepts_true_values(raw: str, expected: bool) -> None:
    """@req CFG-003"""
    config = ClientConfig.resolve(ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_SKIP_VERIFY": raw}))
    assert config.tls_skip_verify is expected


@pytest.mark.parametrize("raw", ["0", "false", "NO", "Off", ""])
def test_cfg_003_boolean_parser_accepts_false_values(raw: str) -> None:
    """@req CFG-003"""
    config = ClientConfig.resolve(ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_SKIP_VERIFY": raw}))
    assert config.tls_skip_verify is False


def test_cfg_003_malformed_boolean_raises_invalid_setting_value() -> None:
    """@req CFG-003"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_SKIP_VERIFY": "maybe"}))

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert excinfo.value.details["setting"] == "TlsSkipVerify"
    assert excinfo.value.retryable is False
    assert excinfo.value.attempts == 0


@pytest.mark.parametrize(
    "raw,expected_seconds",
    [("30s", 30.0), ("1m30s", 90.0), ("500ms", 0.5), ("2h", 7200.0), ("45", 45.0)],
)
def test_cfg_004_duration_parser_accepts_go_vault_style_and_bare_seconds(
    raw: str, expected_seconds: float
) -> None:
    """@req CFG-004"""
    config = ClientConfig.resolve(ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_TIMEOUT": raw}))
    assert config.timeout == datetime.timedelta(seconds=expected_seconds)


def test_cfg_004_malformed_duration_raises_invalid_setting_value() -> None:
    """@req CFG-004"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_TIMEOUT": "soon"}))

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert excinfo.value.details["setting"] == "Timeout"


# --------------------------------------------------------------------------------
# CFG-005 — the environment-ignoring constructor
# --------------------------------------------------------------------------------


def test_cfg_005_none_environment_source_ignores_environment_entirely() -> None:
    """@req CFG-005"""
    env = MapEnvironmentSource({"BASTIONVAULT_ADDR": "https://from-env:8200"})
    with_env = ClientConfig.resolve(ClientOptions(), env)
    assert with_env.address == "https://from-env:8200"

    without_env = ClientConfig.resolve(ClientOptions(), NoneEnvironmentSource())
    assert without_env.address == "https://127.0.0.1:8200"


def test_cfg_005_default_client_constructor_reads_process_environment() -> None:
    """@req CFG-005"""
    from bastionvault_integration_sdk.environment import ProcessEnvironmentSource

    default_client = Client()
    explicit_process_env = Client(environment=ProcessEnvironmentSource())

    assert default_client.config.address == explicit_process_env.config.address


# --------------------------------------------------------------------------------
# CFG-010 / CFG-011 / CNF-035 — address validity and insecure-http exemption
# --------------------------------------------------------------------------------


@pytest.mark.parametrize(
    "address", ["", "   ", "http://", "ftp://host:1", "http:// bad host", "bare name with spaces"]
)
def test_cfg_010_invalid_address_raises_invalid_address(address: str) -> None:
    """@req CFG-010"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(address=address), NoneEnvironmentSource())

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_ADDRESS


def test_cfg_010_bare_cluster_name_is_accepted_without_scheme() -> None:
    """@req CFG-010"""
    config = ClientConfig.resolve(ClientOptions(address="vault.internal"), NoneEnvironmentSource())
    assert config.address == "vault.internal"


@pytest.mark.parametrize("host", ["127.0.0.1", "localhost", "LOCALHOST", "[::1]"])
def test_cfg_011_and_cnf_035_loopback_http_is_allowed_without_flag(host: str) -> None:
    """@req CFG-011 @req CNF-035"""
    config = ClientConfig.resolve(
        ClientOptions(address=f"http://{host}:8200"), NoneEnvironmentSource()
    )
    assert config.address == f"http://{host}:8200"


def test_cfg_011_and_cnf_035_non_loopback_http_requires_allow_insecure_http() -> None:
    """@req CFG-011 @req CNF-035"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(address="http://vault.example.com:8200"), NoneEnvironmentSource())
    assert excinfo.value.code == ErrorCodes.CONFIG_INSECURE_HTTP_NOT_ALLOWED

    allowed = ClientConfig.resolve(
        ClientOptions(address="http://vault.example.com:8200", allow_insecure_http=True),
        NoneEnvironmentSource(),
    )
    assert allowed.allow_insecure_http is True


def test_cfg_011_and_cnf_035_narrow_loopback_exemption_rejects_127_0_0_2() -> None:
    """@req CFG-011 @req CNF-035"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(address="http://127.0.0.2:8200"), NoneEnvironmentSource())
    assert excinfo.value.code == ErrorCodes.CONFIG_INSECURE_HTTP_NOT_ALLOWED


# --------------------------------------------------------------------------------
# CFG-012 / CFG-013 / CFG-014 — client cert pairing, file readability, PEM parsing
# --------------------------------------------------------------------------------


def test_cfg_012_client_cert_and_key_must_be_both_or_neither(tmp_path: Path) -> None:
    """@req CFG-012"""
    cert_path, key_path = _generate_cert_and_key(tmp_path)

    with pytest.raises(BastionVaultError) as only_cert:
        ClientConfig.resolve(ClientOptions(client_cert_path=str(cert_path)), NoneEnvironmentSource())
    assert only_cert.value.code == ErrorCodes.CONFIG_CLIENT_CERT_INCOMPLETE

    with pytest.raises(BastionVaultError) as only_key:
        ClientConfig.resolve(ClientOptions(client_key_path=str(key_path)), NoneEnvironmentSource())
    assert only_key.value.code == ErrorCodes.CONFIG_CLIENT_CERT_INCOMPLETE

    neither = ClientConfig.resolve(ClientOptions(), NoneEnvironmentSource())
    assert neither.client_certificate is None
    assert neither.client_private_key is None

    both = ClientConfig.resolve(
        ClientOptions(client_cert_path=str(cert_path), client_key_path=str(key_path)),
        NoneEnvironmentSource(),
    )
    assert both.client_certificate is not None
    assert both.client_private_key is not None


def test_cfg_013_unreadable_referenced_file_raises_file_not_readable(tmp_path: Path) -> None:
    """@req CFG-013"""
    missing_path = tmp_path / "does-not-exist.pem"
    with pytest.raises(BastionVaultError) as missing:
        ClientConfig.resolve(ClientOptions(ca_cert_path=str(missing_path)), NoneEnvironmentSource())
    assert missing.value.code == ErrorCodes.CONFIG_FILE_NOT_READABLE
    assert missing.value.details["path"] == str(missing_path)

    with pytest.raises(BastionVaultError) as is_a_directory:
        ClientConfig.resolve(ClientOptions(ca_cert_path=str(tmp_path)), NoneEnvironmentSource())
    assert is_a_directory.value.code == ErrorCodes.CONFIG_FILE_NOT_READABLE


def test_cfg_013_regression_unusable_path_is_never_a_generic_exception() -> None:
    """@req CFG-013"""
    unusable_path = "bad\x00path.pem"

    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(ca_cert_path=unusable_path), NoneEnvironmentSource())

    assert excinfo.value.code == ErrorCodes.CONFIG_FILE_NOT_READABLE
    assert isinstance(excinfo.value.cause, ValueError)


def test_cfg_014_invalid_pem_raises_invalid_pem(tmp_path: Path) -> None:
    """@req CFG-014"""
    garbage_path = tmp_path / "garbage.pem"
    garbage_path.write_text("this is not a certificate", encoding="utf-8")

    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(ca_cert_path=str(garbage_path)), NoneEnvironmentSource())

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_PEM


def test_cfg_014_malformed_certificate_block_raises_invalid_pem(tmp_path: Path) -> None:
    """@req CFG-014"""
    malformed_path = tmp_path / "malformed.pem"
    malformed_path.write_text(
        "-----BEGIN CERTIFICATE-----\nnot-valid-base64!!!\n-----END CERTIFICATE-----\n",
        encoding="utf-8",
    )

    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(ca_cert_path=str(malformed_path)), NoneEnvironmentSource())

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_PEM


def test_cfg_014_empty_pem_is_invalid_pem_not_success(tmp_path: Path) -> None:
    """@req CFG-014"""
    empty_path = tmp_path / "empty.pem"
    empty_path.write_text("", encoding="utf-8")

    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(ca_cert_path=str(empty_path)), NoneEnvironmentSource())

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_PEM


def test_cfg_014_invalid_client_private_key_raises_invalid_pem(tmp_path: Path) -> None:
    """@req CFG-014"""
    cert_path, _key_path = _generate_cert_and_key(tmp_path)
    bad_key_path = tmp_path / "bad-key.pem"
    bad_key_path.write_text("not a private key", encoding="utf-8")

    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(
            ClientOptions(client_cert_path=str(cert_path), client_key_path=str(bad_key_path)),
            NoneEnvironmentSource(),
        )

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_PEM


def test_cfg_014_ca_cert_pem_precedence_over_ca_cert_path(tmp_path: Path) -> None:
    """@req CFG-014"""
    cert_path, _key_path = _generate_cert_and_key(tmp_path)
    inline_pem = cert_path.read_text(encoding="utf-8")
    never_read_path = tmp_path / "does-not-exist-and-is-never-opened.pem"

    config = ClientConfig.resolve(
        ClientOptions(ca_cert_pem=inline_pem, ca_cert_path=str(never_read_path)),
        NoneEnvironmentSource(),
    )

    assert len(config.ca_certificates) == 1


# --------------------------------------------------------------------------------
# CFG-015 — namespace validation
# --------------------------------------------------------------------------------


@pytest.mark.parametrize("namespace", ["/leading", "a//b", "with space", "control\x01char"])
def test_cfg_015_malformed_namespace_raises_invalid_namespace(namespace: str) -> None:
    """@req CFG-015"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(namespace=namespace), NoneEnvironmentSource())
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_NAMESPACE


def test_cfg_015_trailing_slash_is_stripped_silently() -> None:
    """@req CFG-015"""
    config = ClientConfig.resolve(ClientOptions(namespace="team/child/"), NoneEnvironmentSource())
    assert config.namespace == "team/child"


# --------------------------------------------------------------------------------
# CFG-016 — timeouts must be positive
# --------------------------------------------------------------------------------


@pytest.mark.parametrize("field_name", ["timeout", "connect_timeout"])
@pytest.mark.parametrize("value", [datetime.timedelta(0), datetime.timedelta(seconds=-1)])
def test_cfg_016_non_positive_timeouts_raise_invalid_setting_value(
    field_name: str, value: datetime.timedelta
) -> None:
    """@req CFG-016"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(**{field_name: value}), NoneEnvironmentSource())
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE


@pytest.mark.parametrize("field_name", ["timeout", "connect_timeout", "discovery_probe_timeout"])
@pytest.mark.parametrize("bad_value", [0, -5, 0.0, "30s"])
def test_d_m1a_19_wrong_typed_duration_raises_invalid_setting_value_not_type_error(
    field_name: str, bad_value: object
) -> None:
    """@req CFG-016"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(**{field_name: bad_value}), NoneEnvironmentSource())  # type: ignore[arg-type]

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert isinstance(excinfo.value.details["setting"], str)


@pytest.mark.parametrize(
    "field_name",
    [
        "use_token_helper",
        "ca_cert_replaces_system_roots",
        "tls_skip_verify",
        "allow_insecure_http",
        "cluster_discovery",
    ],
)
@pytest.mark.parametrize("bad_value", [1, 0, "true", "yes"])
def test_d_m1a_19_wrong_typed_boolean_raises_invalid_setting_value_not_type_error(
    field_name: str, bad_value: object
) -> None:
    """@req CFG-003"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(**{field_name: bad_value}), NoneEnvironmentSource())  # type: ignore[arg-type]

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE


@pytest.mark.parametrize(
    "field_name",
    ["address", "token", "token_file", "namespace", "tls_server_name", "user_agent", "api_prefix"],
)
def test_d_m1a_19_wrong_typed_string_raises_invalid_setting_value_not_type_error(field_name: str) -> None:
    """@req CFG-016"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(ClientOptions(**{field_name: 12345}), NoneEnvironmentSource())  # type: ignore[arg-type]

    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE


def test_d_m1a_19_wrong_typed_headers_raises_invalid_setting_value() -> None:
    """@req CFG-017"""
    with pytest.raises(BastionVaultError) as list_headers:
        ClientConfig.resolve(ClientOptions(headers=["not", "a", "mapping"]), NoneEnvironmentSource())  # type: ignore[arg-type]
    assert list_headers.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert list_headers.value.details["setting"] == "Headers"

    with pytest.raises(BastionVaultError) as non_str_values:
        ClientConfig.resolve(ClientOptions(headers={"X-Count": 1}), NoneEnvironmentSource())  # type: ignore[dict-item]
    assert non_str_values.value.details["setting"] == "Headers"


def test_d_m1a_19_wrong_typed_retry_policy_and_rate_gate_raise_invalid_setting_value() -> None:
    """@req CFG-050"""
    with pytest.raises(BastionVaultError) as wrong_retry_policy:
        ClientConfig.resolve(ClientOptions(retry_policy={"max_attempts": 1}), NoneEnvironmentSource())  # type: ignore[arg-type]
    assert wrong_retry_policy.value.details["setting"] == "RetryPolicy"

    with pytest.raises(BastionVaultError) as wrong_rate_gate:
        ClientConfig.resolve(ClientOptions(rate_gate=(1, 2)), NoneEnvironmentSource())  # type: ignore[arg-type]
    assert wrong_rate_gate.value.details["setting"] == "RateGate"


def test_d_m1a_19_wrong_typed_logger_and_transport_raise_invalid_setting_value() -> None:
    """@req OVR-001"""
    with pytest.raises(BastionVaultError) as wrong_logger:
        ClientConfig.resolve(ClientOptions(logger="not-a-logger"), NoneEnvironmentSource())  # type: ignore[arg-type]
    assert wrong_logger.value.details["setting"] == "Logger"

    with pytest.raises(BastionVaultError) as wrong_transport:
        ClientConfig.resolve(ClientOptions(transport=object()), NoneEnvironmentSource())  # type: ignore[arg-type]
    assert wrong_transport.value.details["setting"] == "Transport"


# --------------------------------------------------------------------------------
# CFG-017 — reserved headers, and the headers-aliasing regression
# --------------------------------------------------------------------------------


@pytest.mark.parametrize(
    "header_name",
    [
        "X-BastionVault-Token",
        "x-vault-token",
        "AUTHORIZATION",
        "Cookie",
        "X-BastionVault-Namespace",
        "host",
        "Content-Length",
    ],
)
def test_cfg_017_reserved_headers_are_rejected_case_insensitively(header_name: str) -> None:
    """@req CFG-017"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(
            ClientOptions(headers={header_name: "value"}), NoneEnvironmentSource()
        )
    assert excinfo.value.code == ErrorCodes.CONFIG_RESERVED_HEADER
    assert "X-Vault-Token" in excinfo.value.hint


def test_cfg_017_non_reserved_header_is_accepted() -> None:
    """@req CFG-017"""
    config = ClientConfig.resolve(ClientOptions(headers={"X-My-Header": "ok"}), NoneEnvironmentSource())
    assert config.headers["X-My-Header"] == "ok"


def test_cfg_017_regression_headers_do_not_alias_the_callers_dict() -> None:
    """@req CFG-017"""
    caller_headers = {"X-My-Header": "original"}
    config = ClientConfig.resolve(ClientOptions(headers=caller_headers), NoneEnvironmentSource())

    caller_headers["X-My-Header"] = "mutated-after-construction"
    caller_headers["X-Vault-Token"] = "injected-after-construction"

    assert config.headers["X-My-Header"] == "original"
    assert "X-Vault-Token" not in config.headers
    with pytest.raises(TypeError):
        config.headers["X-New"] = "not-allowed"  # type: ignore[index]


# --------------------------------------------------------------------------------
# CFG-018 / CNF-030 — TlsSkipVerify warns once and marks the client insecure
# --------------------------------------------------------------------------------


def test_cfg_018_and_cnf_030_tls_skip_verify_warns_once_and_marks_insecure() -> None:
    """@req CFG-018 @req CNF-030"""
    logger = _RecordingLogger()
    client = Client(ClientOptions(tls_skip_verify=True, logger=logger))

    assert client.is_insecure is True
    assert len(logger.warnings) == 1


def test_cfg_018_default_is_not_insecure_and_does_not_warn() -> None:
    """@req CFG-018 @req CNF-030"""
    logger = _RecordingLogger()
    client = Client(ClientOptions(logger=logger))

    assert client.is_insecure is False
    assert logger.warnings == []


# --------------------------------------------------------------------------------
# CFG-030 — token helper file
# --------------------------------------------------------------------------------


def test_cfg_030_token_helper_reads_and_trims_the_token_file(tmp_path: Path) -> None:
    """@req CFG-030"""
    token_file = tmp_path / "token"
    token_file.write_text("  s.from-file-token  \n", encoding="utf-8")

    config = ClientConfig.resolve(
        ClientOptions(use_token_helper=True, token_file=str(token_file)), NoneEnvironmentSource()
    )

    assert config.token is not None
    assert config.token.reveal() == "s.from-file-token"


def test_cfg_030_missing_token_file_is_no_token_not_an_error(tmp_path: Path) -> None:
    """@req CFG-030"""
    missing_token_file = tmp_path / "missing-token"

    config = ClientConfig.resolve(
        ClientOptions(use_token_helper=True, token_file=str(missing_token_file)), NoneEnvironmentSource()
    )

    assert config.token is None


def test_cfg_030_token_helper_not_consulted_unless_use_token_helper_is_true(tmp_path: Path) -> None:
    """@req CFG-030"""
    token_file = tmp_path / "token"
    token_file.write_text("s.should-not-be-read", encoding="utf-8")

    config = ClientConfig.resolve(ClientOptions(token_file=str(token_file)), NoneEnvironmentSource())

    assert config.token is None


# --------------------------------------------------------------------------------
# CFG-040 / CFG-041 / CFG-042 — TLS parameters materialised as data
# --------------------------------------------------------------------------------


def test_cfg_040_ca_cert_replaces_system_roots_is_recorded() -> None:
    """@req CFG-040"""
    default_config = ClientConfig.resolve(ClientOptions(), NoneEnvironmentSource())
    assert default_config.ca_cert_replaces_system_roots is False

    replacing = ClientConfig.resolve(
        ClientOptions(ca_cert_replaces_system_roots=True), NoneEnvironmentSource()
    )
    assert replacing.ca_cert_replaces_system_roots is True


def test_cfg_041_minimum_tls_version_is_recorded_as_1_2_offering_1_3() -> None:
    """@req CFG-041"""
    config = ClientConfig.resolve(ClientOptions(), NoneEnvironmentSource())

    assert config.min_tls_version == "TLSv1.2"
    assert config.offers_tls_1_3 is True


def test_cfg_042_tls_server_name_is_recorded_verbatim() -> None:
    """@req CFG-042"""
    explicit = ClientConfig.resolve(
        ClientOptions(tls_server_name="vault.internal"), NoneEnvironmentSource()
    )
    assert explicit.tls_server_name == "vault.internal"

    from_env = ClientConfig.resolve(
        ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_TLS_SERVER_NAME": "from-env.internal"})
    )
    assert from_env.tls_server_name == "from-env.internal"


# --------------------------------------------------------------------------------
# CFG-050 — retry policy defaults
# --------------------------------------------------------------------------------


def test_cfg_050_retry_policy_defaults_match_specification() -> None:
    """@req CFG-050"""
    config = ClientConfig.resolve(ClientOptions(), NoneEnvironmentSource())

    assert config.retry_policy == RetryPolicy(
        max_attempts=3,
        initial_backoff=datetime.timedelta(milliseconds=250),
        max_backoff=datetime.timedelta(seconds=5),
        backoff_multiplier=2.0,
        jitter=0.2,
        respect_retry_after=True,
        retry_idempotent_only=True,
    )


def test_cfg_050_max_retries_env_var_sets_max_attempts_plus_one() -> None:
    """@req CFG-050"""
    config = ClientConfig.resolve(
        ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_MAX_RETRIES": "5"})
    )
    assert config.retry_policy.max_attempts == 6


def test_cfg_050_explicit_retry_policy_bypasses_the_environment() -> None:
    """@req CFG-050"""
    explicit_policy = RetryPolicy(max_attempts=1)
    config = ClientConfig.resolve(
        ClientOptions(retry_policy=explicit_policy),
        MapEnvironmentSource({"BASTIONVAULT_MAX_RETRIES": "9"}),
    )
    assert config.retry_policy is explicit_policy
    assert config.retry_policy.max_attempts == 1


def test_cfg_050_malformed_max_retries_raises_invalid_setting_value() -> None:
    """@req CFG-050"""
    with pytest.raises(BastionVaultError) as excinfo:
        ClientConfig.resolve(
            ClientOptions(), MapEnvironmentSource({"BASTIONVAULT_MAX_RETRIES": "many"})
        )
    assert excinfo.value.code == ErrorCodes.CONFIG_INVALID_SETTING_VALUE
    assert excinfo.value.details["setting"] == "RetryPolicy.MaxAttempts"


# --------------------------------------------------------------------------------
# CFG-060 / CFG-061 — per-call options
# --------------------------------------------------------------------------------


def test_cfg_060_request_options_are_optional_with_sensible_defaults() -> None:
    """@req CFG-060"""
    options = RequestOptions()

    assert options.namespace is None
    assert dict(options.headers) == {}
    assert options.timeout is None
    assert options.idempotent is False
    assert options.wrap_ttl is None
    assert options.token is None


def test_cfg_061_request_options_are_frozen_and_do_not_alias_caller_headers() -> None:
    """@req CFG-061"""
    caller_headers = {"X-Extra": "value"}
    options = RequestOptions(headers=caller_headers)

    caller_headers["X-Extra"] = "mutated-after-construction"
    assert options.headers["X-Extra"] == "value"

    with pytest.raises(dataclasses.FrozenInstanceError):
        options.namespace = "other"  # type: ignore[misc]


def test_cfg_061_per_call_options_never_mutate_the_client() -> None:
    """@req CFG-061"""
    client = Client(ClientOptions(namespace="team"))
    before = client.config

    RequestOptions(namespace="different-namespace-for-this-call-only")

    assert client.config is before
    assert client.config.namespace == "team"


# --------------------------------------------------------------------------------
# OVR-001 — the transport injection seam
# --------------------------------------------------------------------------------


def test_ovr_001_transport_is_a_runtime_checkable_protocol_and_is_injectable() -> None:
    """@req OVR-001"""
    fake = _FakeTransport()
    assert isinstance(fake, Transport)

    client = Client(transport=fake)
    assert client.config.transport is fake


def test_ovr_001_fixture_driver_transport_is_not_a_real_socket() -> None:
    """@req OVR-001"""
    fake = _FakeTransport()
    with pytest.raises(AssertionError):
        fake.request("GET", "https://example.invalid", {}, None)


# --------------------------------------------------------------------------------
# OVR-004 — no global mutable state; two clients are independent
# --------------------------------------------------------------------------------


def test_ovr_004_two_clients_are_fully_independent() -> None:
    """@req OVR-004"""
    first_options = ClientOptions(address="https://first.example:8200", namespace="team-a")
    second_options = ClientOptions(address="https://second.example:8200", namespace="team-b")

    first = Client(first_options, environment=NoneEnvironmentSource())
    second = Client(second_options, environment=NoneEnvironmentSource())

    assert first.config.address != second.config.address
    assert first.config.namespace != second.config.namespace

    first_options.namespace = "mutated-after-construction"
    assert first.config.namespace == "team-a"
    assert second.config.namespace == "team-b"


# --------------------------------------------------------------------------------
# CNF-033 — the SDK ships no code that writes a token to disk
# --------------------------------------------------------------------------------


def test_cnf_033_no_files_are_written_during_construction(tmp_path: Path) -> None:
    """@req CNF-033"""
    token_file = tmp_path / "token"
    token_file.write_text("s.existing-token", encoding="utf-8")
    before = set(tmp_path.iterdir())

    Client(ClientOptions(use_token_helper=True, token_file=str(token_file), token="s.explicit"))

    after = set(tmp_path.iterdir())
    assert before == after
    assert not hasattr(Client, "persist_token")
    assert not hasattr(ClientConfig, "persist_token")
