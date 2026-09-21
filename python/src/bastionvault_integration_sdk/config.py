"""Client configuration: resolution and validation (specifications/02-client-configuration.md).

`ClientOptions` is the mutable input; `ClientConfig` is the immutable, fully-resolved
result (D-M1a-2). Resolution happens exactly once, in `ClientConfig.resolve` (CFG-002):
first every setting in the table is resolved and parsed, in table order, raising
`BV-CONFIG-003` naming the setting on the first malformed value (D-M1a-4/5); then the
nine validation checks run in the fixed order of D-M1a-5, stopping at the first failure.
"""

from __future__ import annotations

import re
import unicodedata
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import timedelta
from pathlib import Path
from types import MappingProxyType
from typing import Any
from urllib.parse import urlsplit

from cryptography import x509
from cryptography.hazmat.primitives import serialization

from ._metadata import SDK_VERSION
from .environment import EnvironmentSource, ProcessEnvironmentSource
from .errors import ErrorCodes, make_config_error
from .logger import ClientLogger, NoOpClientLogger
from .secrets import SecretString
from .settings import (
    RESERVED_HEADERS_CASEFOLD,
    AutoRenew,
    InvalidSettingValueError,
    RateGate,
    parse_bool,
    parse_duration,
    parse_int,
)
from .token_source import TokenSource
from .transport import RequestObserver, RetryPolicy, Transport, _NoOpRequestObserver

_LOOPBACK_HOSTS = frozenset({"127.0.0.1", "::1", "localhost"})
_DEFAULT_ADDRESS = "https://127.0.0.1:8200"
_DEFAULT_TIMEOUT = timedelta(seconds=30)
_DEFAULT_CONNECT_TIMEOUT = timedelta(seconds=10)
_DEFAULT_DISCOVERY_PROBE_TIMEOUT = timedelta(milliseconds=1500)
_MIN_TLS_VERSION = "TLSv1.2"
_DEFAULT_MAX_RESPONSE_BYTES = 134217728  # 128 MiB (D-M1b-13)
_CERTIFICATE_BLOCK_RE = re.compile(
    rb"-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----", re.DOTALL
)


@dataclass
class ClientOptions:
    """The mutable input an application fills in before constructing a `Client`."""

    address: str | None = None
    token: str | None = None
    token_file: str | None = None
    use_token_helper: bool | None = None
    namespace: str | None = None
    ca_cert_path: str | None = None
    ca_cert_pem: str | None = None
    ca_cert_replaces_system_roots: bool | None = None
    client_cert_path: str | None = None
    client_key_path: str | None = None
    tls_skip_verify: bool | None = None
    tls_server_name: str | None = None
    allow_insecure_http: bool | None = None
    timeout: timedelta | None = None
    connect_timeout: timedelta | None = None
    retry_policy: RetryPolicy | None = None
    rate_gate: RateGate | None = None
    cluster_discovery: bool | None = None
    discovery_probe_timeout: timedelta | None = None
    headers: Mapping[str, str] | None = None
    user_agent: str | None = None
    api_prefix: str | None = None
    auto_renew: AutoRenew | None = None
    logger: ClientLogger | None = None
    transport: Transport | None = None
    max_response_bytes: int | None = None
    use_system_proxy: bool | None = None
    observer: RequestObserver | None = None
    token_source: TokenSource | None = None
    """AUT-001's source, supplied explicitly (D-M2-12).

    Without an injection point `TokenSource.callback` is decorative: AUT-004 makes
    `Auth.token_source` read-only, and `set_token`/`Auth.token.use` install only a `Static`
    source. D-M2-6 pinned the type and forgot its way in.
    """


@dataclass(frozen=True)
class ClientConfig:
    """The immutable, fully-resolved configuration the rest of the SDK reads (D-M1a-2)."""

    address: str
    token: SecretString | None
    token_file: str
    use_token_helper: bool
    namespace: str
    ca_cert_path: str | None
    ca_cert_pem: str | None
    ca_cert_replaces_system_roots: bool
    ca_certificates: tuple[Any, ...]
    client_cert_path: str | None
    client_key_path: str | None
    client_certificate: Any | None
    client_private_key: Any | None
    tls_skip_verify: bool
    tls_server_name: str | None
    min_tls_version: str
    offers_tls_1_3: bool
    allow_insecure_http: bool
    timeout: timedelta
    connect_timeout: timedelta
    retry_policy: RetryPolicy
    rate_gate: RateGate
    cluster_discovery: bool
    discovery_probe_timeout: timedelta
    headers: Mapping[str, str]
    user_agent: str
    api_prefix: str
    auto_renew: AutoRenew
    logger: ClientLogger
    transport: Transport | None
    max_response_bytes: int
    use_system_proxy: bool
    observer: RequestObserver
    token_source: TokenSource | None

    @property
    def is_insecure(self) -> bool:
        """CFG-018: true exactly when `TlsSkipVerify` is true."""
        return self.tls_skip_verify

    @classmethod
    def resolve(
        cls,
        options: ClientOptions | None = None,
        environment: EnvironmentSource | None = None,
    ) -> ClientConfig:
        """Resolve and validate a `ClientConfig` (CFG-001/002; D-M1a-5 order)."""

        opts = options if options is not None else ClientOptions()
        env = environment if environment is not None else ProcessEnvironmentSource()

        # --- Phase 0: validate the *type* of every explicit, code-supplied value
        # (D-M1a-19). `mypy --strict` cannot catch a wrong type at an untyped call
        # site, so a wrong-typed value is checked here and raises `BV-CONFIG-003`
        # naming the setting, in table order, before anything tries to use it.
        _validate_explicit_types(opts)

        # --- Phase A: resolve every setting, in settings-table declaration order. ---
        address = _resolve_str(opts.address, env, ("BASTIONVAULT_ADDR", "VAULT_ADDR"), _DEFAULT_ADDRESS)
        token_raw = _resolve_optional_str(opts.token, env, ("BASTIONVAULT_TOKEN", "VAULT_TOKEN"))
        token_file = _resolve_str(
            opts.token_file,
            env,
            ("BASTIONVAULT_TOKEN_FILE",),
            str(Path.home() / ".vault-token"),
        )
        use_token_helper = _resolve_bool(
            opts.use_token_helper, env, ("BASTIONVAULT_USE_TOKEN_HELPER",), False, "UseTokenHelper"
        )
        if token_raw is None and use_token_helper:
            token_raw = _read_token_helper_file(token_file)
        token = SecretString(token_raw) if token_raw else None

        namespace_raw = _resolve_str(
            opts.namespace, env, ("BASTIONVAULT_NAMESPACE", "VAULT_NAMESPACE"), ""
        )
        ca_cert_path = _resolve_optional_str(opts.ca_cert_path, env, ("BASTIONVAULT_CACERT", "VAULT_CACERT"))
        ca_cert_pem = _resolve_optional_str(opts.ca_cert_pem, env, ())
        ca_cert_replaces_system_roots = _resolve_bool(
            opts.ca_cert_replaces_system_roots, env, (), False, "CaCertReplacesSystemRoots"
        )
        client_cert_path = _resolve_optional_str(
            opts.client_cert_path, env, ("BASTIONVAULT_CLIENT_CERT", "VAULT_CLIENT_CERT")
        )
        client_key_path = _resolve_optional_str(
            opts.client_key_path, env, ("BASTIONVAULT_CLIENT_KEY", "VAULT_CLIENT_KEY")
        )
        tls_skip_verify = _resolve_bool(
            opts.tls_skip_verify,
            env,
            ("BASTIONVAULT_SKIP_VERIFY", "VAULT_SKIP_VERIFY"),
            False,
            "TlsSkipVerify",
        )
        tls_server_name = _resolve_optional_str(
            opts.tls_server_name, env, ("BASTIONVAULT_TLS_SERVER_NAME", "VAULT_TLS_SERVER_NAME")
        )
        allow_insecure_http = _resolve_bool(
            opts.allow_insecure_http, env, ("BASTIONVAULT_ALLOW_INSECURE_HTTP",), False, "AllowInsecureHttp"
        )
        timeout = _resolve_duration(
            opts.timeout, env, ("BASTIONVAULT_TIMEOUT", "VAULT_CLIENT_TIMEOUT"), _DEFAULT_TIMEOUT, "Timeout"
        )
        connect_timeout = _resolve_duration(
            opts.connect_timeout,
            env,
            ("BASTIONVAULT_CONNECT_TIMEOUT",),
            _DEFAULT_CONNECT_TIMEOUT,
            "ConnectTimeout",
        )

        if opts.retry_policy is not None:
            retry_policy = opts.retry_policy
        else:
            retries = _resolve_int(
                env, ("BASTIONVAULT_MAX_RETRIES", "VAULT_MAX_RETRIES"), "RetryPolicy.MaxAttempts"
            )
            retry_policy = RetryPolicy() if retries is None else RetryPolicy(max_attempts=retries + 1)

        if opts.rate_gate is not None:
            rate_gate = opts.rate_gate
        else:
            rate_per_second = _resolve_int(env, ("BASTIONVAULT_RATE_PER_SEC",), "RateGate.RatePerSecond")
            burst = _resolve_int(env, ("BASTIONVAULT_RATE_BURST",), "RateGate.Burst")
            if rate_per_second is not None and rate_per_second < 0:
                raise make_config_error(
                    ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": "RateGate.RatePerSecond"}
                )
            if burst is not None and burst < 0:
                raise make_config_error(
                    ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": "RateGate.Burst"}
                )
            rate_gate = RateGate(
                rate_per_second=8 if rate_per_second is None else rate_per_second,
                burst=16 if burst is None else burst,
            )

        if opts.cluster_discovery is not None:
            cluster_discovery = opts.cluster_discovery
        else:
            no_discovery_raw = _first_present(
                env, ("BASTIONVAULT_NO_CLUSTER_DISCOVERY", "VAULT_NO_CLUSTER_DISCOVERY")
            )
            if no_discovery_raw is None:
                cluster_discovery = True
            else:
                try:
                    cluster_discovery = not parse_bool(no_discovery_raw)
                except InvalidSettingValueError as error:
                    raise make_config_error(
                        ErrorCodes.CONFIG_INVALID_SETTING_VALUE,
                        details={"setting": "ClusterDiscovery"},
                        cause=error,
                    ) from error

        discovery_probe_timeout = _resolve_duration(
            opts.discovery_probe_timeout,
            env,
            ("BASTIONVAULT_DISCOVERY_PROBE_TIMEOUT",),
            _DEFAULT_DISCOVERY_PROBE_TIMEOUT,
            "DiscoveryProbeTimeout",
        )
        headers_input = dict(opts.headers or {})
        default_user_agent = f"bastionvault-sdk-python/{SDK_VERSION}"
        user_agent = opts.user_agent if opts.user_agent is not None else default_user_agent
        api_prefix = opts.api_prefix if opts.api_prefix is not None else "v1"
        auto_renew = opts.auto_renew if opts.auto_renew is not None else AutoRenew()
        logger = opts.logger if opts.logger is not None else NoOpClientLogger()
        transport = opts.transport
        max_response_bytes = (
            opts.max_response_bytes if opts.max_response_bytes is not None else _DEFAULT_MAX_RESPONSE_BYTES
        )
        use_system_proxy = opts.use_system_proxy if opts.use_system_proxy is not None else False
        observer = opts.observer if opts.observer is not None else _NoOpRequestObserver()

        # --- Phase B: validation, in the fixed D-M1a-5 order. Stop at first failure. ---

        # 1 & 2: Address validity, then insecure-http (CFG-010/011, CNF-035).
        _validate_address(address, allow_insecure_http)

        # 3: Namespace (CFG-015).
        namespace = _validate_namespace(namespace_raw)

        # 4: Reserved headers (CFG-017).
        headers = _validate_headers(headers_input)

        # 5: Timeouts > 0 (CFG-016).
        _validate_positive_duration(timeout, "Timeout")
        _validate_positive_duration(connect_timeout, "ConnectTimeout")

        # 6: Client cert pair (CFG-012).
        if bool(client_cert_path) != bool(client_key_path):
            raise make_config_error(ErrorCodes.CONFIG_CLIENT_CERT_INCOMPLETE)

        # 7 & 8: Referenced files readable, then PEM parses, both in the same order:
        # CaCertPath (skipped when CaCertPem is set), ClientCertPath, ClientKeyPath.
        if ca_cert_pem is not None:
            ca_certificates = _parse_certificates(ca_cert_pem.encode("utf-8"), "CaCertPem")
        elif ca_cert_path is not None:
            ca_certificates = _parse_certificates(_read_configured_file(ca_cert_path), ca_cert_path)
        else:
            ca_certificates = ()

        client_certificate: Any | None = None
        client_private_key: Any | None = None
        if client_cert_path is not None and client_key_path is not None:
            cert_bytes = _read_configured_file(client_cert_path)
            key_bytes = _read_configured_file(client_key_path)
            client_certificate = _parse_certificates(cert_bytes, client_cert_path)[0]
            client_private_key = _parse_private_key(key_bytes, client_key_path)

        # 9: TlsSkipVerify warning, not a failure (CFG-018, CNF-030).
        if tls_skip_verify:
            logger.warning(
                "TLS certificate verification is disabled (TlsSkipVerify=true); "
                "this MUST NOT be used in production."
            )

        return cls(
            address=address,
            token=token,
            token_source=opts.token_source,
            token_file=token_file,
            use_token_helper=use_token_helper,
            namespace=namespace,
            ca_cert_path=ca_cert_path,
            ca_cert_pem=ca_cert_pem,
            ca_cert_replaces_system_roots=ca_cert_replaces_system_roots,
            ca_certificates=ca_certificates,
            client_cert_path=client_cert_path,
            client_key_path=client_key_path,
            client_certificate=client_certificate,
            client_private_key=client_private_key,
            tls_skip_verify=tls_skip_verify,
            tls_server_name=tls_server_name,
            min_tls_version=_MIN_TLS_VERSION,
            offers_tls_1_3=True,
            allow_insecure_http=allow_insecure_http,
            timeout=timeout,
            connect_timeout=connect_timeout,
            retry_policy=retry_policy,
            rate_gate=rate_gate,
            cluster_discovery=cluster_discovery,
            discovery_probe_timeout=discovery_probe_timeout,
            headers=headers,
            user_agent=user_agent,
            api_prefix=api_prefix,
            auto_renew=auto_renew,
            logger=logger,
            transport=transport,
            max_response_bytes=max_response_bytes,
            use_system_proxy=use_system_proxy,
            observer=observer,
        )


# --------------------------------------------------------------------------------
# Phase 0: explicit-value type validation (D-M1a-19)
# --------------------------------------------------------------------------------

# (attribute on ClientOptions, canonical setting name, expected type), in settings-table
# declaration order. `None` always means "unset"; a present value of the wrong type is
# `BV-CONFIG-003`, never a generic `TypeError`/`AttributeError` surfacing at first use.
_EXPLICIT_TYPE_CHECKS: tuple[tuple[str, str, type], ...] = (
    ("address", "Address", str),
    ("token", "Token", str),
    ("token_file", "TokenFile", str),
    ("use_token_helper", "UseTokenHelper", bool),
    ("namespace", "Namespace", str),
    ("ca_cert_path", "CaCertPath", str),
    ("ca_cert_pem", "CaCertPem", str),
    ("ca_cert_replaces_system_roots", "CaCertReplacesSystemRoots", bool),
    ("client_cert_path", "ClientCertPath", str),
    ("client_key_path", "ClientKeyPath", str),
    ("tls_skip_verify", "TlsSkipVerify", bool),
    ("tls_server_name", "TlsServerName", str),
    ("allow_insecure_http", "AllowInsecureHttp", bool),
    ("timeout", "Timeout", timedelta),
    ("connect_timeout", "ConnectTimeout", timedelta),
    ("retry_policy", "RetryPolicy", RetryPolicy),
    ("rate_gate", "RateGate", RateGate),
    ("cluster_discovery", "ClusterDiscovery", bool),
    ("discovery_probe_timeout", "DiscoveryProbeTimeout", timedelta),
    ("headers", "Headers", Mapping),
    ("user_agent", "UserAgent", str),
    ("api_prefix", "ApiPrefix", str),
    ("auto_renew", "AutoRenew", AutoRenew),
    ("logger", "Logger", ClientLogger),
    ("transport", "Transport", Transport),
    ("max_response_bytes", "MaxResponseBytes", int),
    ("use_system_proxy", "UseSystemProxy", bool),
    ("observer", "Observer", RequestObserver),
)


def _validate_explicit_types(options: ClientOptions) -> None:
    """D-M1a-19: a wrong-typed explicit value is `BV-CONFIG-003`, not a generic exception."""

    for attribute, setting, expected_type in _EXPLICIT_TYPE_CHECKS:
        value = getattr(options, attribute)
        if value is None:
            continue
        if not isinstance(value, expected_type):
            raise make_config_error(
                ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": setting}
            )
    if options.headers is not None:
        for key, header_value in options.headers.items():
            if not isinstance(key, str) or not isinstance(header_value, str):
                raise make_config_error(
                    ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": "Headers"}
                )


# --------------------------------------------------------------------------------
# Resolution helpers
# --------------------------------------------------------------------------------


def _first_present(environment: EnvironmentSource, names: Sequence[str]) -> str | None:
    for name in names:
        value = environment.get(name)
        if value is not None:
            return value
    return None


def _resolve_str(
    explicit: str | None, environment: EnvironmentSource, names: Sequence[str], default: str
) -> str:
    if explicit is not None:
        return explicit
    value = _first_present(environment, names)
    return value if value is not None else default


def _resolve_optional_str(
    explicit: str | None, environment: EnvironmentSource, names: Sequence[str]
) -> str | None:
    if explicit is not None:
        return explicit
    return _first_present(environment, names)


def _resolve_bool(
    explicit: bool | None,
    environment: EnvironmentSource,
    names: Sequence[str],
    default: bool,
    setting: str,
) -> bool:
    if explicit is not None:
        return explicit
    raw = _first_present(environment, names)
    if raw is None:
        return default
    try:
        return parse_bool(raw)
    except InvalidSettingValueError as error:
        raise make_config_error(
            ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": setting}, cause=error
        ) from error


def _resolve_duration(
    explicit: timedelta | None,
    environment: EnvironmentSource,
    names: Sequence[str],
    default: timedelta,
    setting: str,
) -> timedelta:
    if explicit is not None:
        return explicit
    raw = _first_present(environment, names)
    if raw is None:
        return default
    try:
        return parse_duration(raw)
    except InvalidSettingValueError as error:
        raise make_config_error(
            ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": setting}, cause=error
        ) from error


def _resolve_int(environment: EnvironmentSource, names: Sequence[str], setting: str) -> int | None:
    raw = _first_present(environment, names)
    if raw is None:
        return None
    try:
        return parse_int(raw)
    except InvalidSettingValueError as error:
        raise make_config_error(
            ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": setting}, cause=error
        ) from error


def _read_token_helper_file(path: str) -> str | None:
    """CFG-030: read the token file, trimmed. Any failure means "no token" (D-M1a-16)."""
    try:
        content = Path(path).read_text(encoding="utf-8")
    except (OSError, ValueError, UnicodeDecodeError):
        return None
    trimmed = content.strip()
    return trimmed or None


# --------------------------------------------------------------------------------
# Validation helpers (D-M1a-5)
# --------------------------------------------------------------------------------


def _validate_address(address: str, allow_insecure_http: bool) -> None:
    """CFG-010/011, CNF-035."""

    stripped = address.strip()
    if not stripped:
        raise make_config_error(ErrorCodes.CONFIG_INVALID_ADDRESS)
    if "://" not in stripped:
        if any(character.isspace() for character in stripped):
            raise make_config_error(ErrorCodes.CONFIG_INVALID_ADDRESS)
        return  # a bare cluster name; discovery is deferred to M5.

    # TRN-092: an unbracketed IPv6 literal with a port is ambiguous (`::1:8200` could be
    # the address `::1` on port `8200`, or the address `::1:8200` with no port at all)
    # and MUST be rejected rather than guessed at. Checked on the raw authority before
    # `urlsplit`, since `urlsplit` silently mis-splits some of these instead of failing.
    scheme_separator = stripped.find("://")
    authority = stripped[scheme_separator + 3 :].split("/", 1)[0].split("?", 1)[0]
    host_port = authority.rsplit("@", 1)[-1]  # tolerate `user:pass@host` authority
    if not host_port.startswith("[") and host_port.count(":") >= 2:
        raise make_config_error(ErrorCodes.CONFIG_INVALID_ADDRESS)

    parsed = urlsplit(stripped)
    if (
        parsed.scheme not in ("http", "https")
        or not parsed.hostname
        or any(character.isspace() for character in parsed.hostname)
    ):
        raise make_config_error(ErrorCodes.CONFIG_INVALID_ADDRESS)
    if parsed.scheme == "http" and not allow_insecure_http:
        if parsed.hostname.casefold() not in _LOOPBACK_HOSTS:
            raise make_config_error(ErrorCodes.CONFIG_INSECURE_HTTP_NOT_ALLOWED)


def _validate_namespace(raw: str) -> str:
    """CFG-015: trailing `/` stripped silently; leading `/`, `//`, whitespace, control chars fail."""

    namespace = raw[:-1] if raw.endswith("/") else raw
    if (
        namespace.startswith("/")
        or "//" in namespace
        or any(character.isspace() for character in namespace)
        or any(unicodedata.category(character) == "Cc" for character in namespace)
    ):
        raise make_config_error(ErrorCodes.CONFIG_INVALID_NAMESPACE)
    return namespace


def _validate_headers(headers: Mapping[str, str]) -> Mapping[str, str]:
    """CFG-017: reject reserved header names, case-insensitively. Copies defensively."""

    copied = dict(headers)
    for name in copied:
        if name.casefold() in RESERVED_HEADERS_CASEFOLD:
            raise make_config_error(ErrorCodes.CONFIG_RESERVED_HEADER)
    return MappingProxyType(copied)


def _validate_positive_duration(value: timedelta, setting: str) -> None:
    if value <= timedelta(0):
        raise make_config_error(ErrorCodes.CONFIG_INVALID_SETTING_VALUE, details={"setting": setting})


def _read_configured_file(path: str) -> bytes:
    """CFG-013: convert every failure mode of opening the file into `BV-CONFIG-005`."""

    try:
        file_path = Path(path)
        if file_path.is_dir():
            raise IsADirectoryError(path)
        return file_path.read_bytes()
    except (OSError, ValueError) as error:
        raise make_config_error(
            ErrorCodes.CONFIG_FILE_NOT_READABLE, details={"path": path}, cause=error
        ) from error


def _parse_certificates(data: bytes, path_label: str) -> tuple[Any, ...]:
    """CFG-014: parse PEM certificates; a zero-certificate parse is `BV-CONFIG-006` (D-M1a-17)."""

    blocks = _CERTIFICATE_BLOCK_RE.findall(data)
    if not blocks:
        raise make_config_error(ErrorCodes.CONFIG_INVALID_PEM, details={"path": path_label})
    certificates = []
    for block in blocks:
        try:
            certificates.append(x509.load_pem_x509_certificate(block))
        except ValueError as error:
            raise make_config_error(
                ErrorCodes.CONFIG_INVALID_PEM, details={"path": path_label}, cause=error
            ) from error
    return tuple(certificates)


def _parse_private_key(data: bytes, path_label: str) -> Any:
    try:
        return serialization.load_pem_private_key(data, password=None)
    except (ValueError, TypeError) as error:
        raise make_config_error(
            ErrorCodes.CONFIG_INVALID_PEM, details={"path": path_label}, cause=error
        ) from error


__all__ = ["ClientConfig", "ClientOptions"]
