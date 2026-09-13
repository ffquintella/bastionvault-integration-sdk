"""BastionVault Integration SDK — Python package.

M1a adds client configuration (specifications/02-client-configuration.md): the mutable
`ClientOptions` input, the immutable resolved `ClientConfig`, the `Client` seam it
constructs, the shared `EnvironmentSource`/`SecretString`/`Transport` types, and the
`BastionVaultError` skeleton (only `BV-CONFIG-*` codes are populated; see
`decisions/0003-m1a-configuration.md`).
"""

from __future__ import annotations

from ._metadata import SDK_VERSION, SPECIFICATION_VERSION
from .client import Client
from .config import ClientConfig, ClientOptions
from .environment import (
    EnvironmentSource,
    MapEnvironmentSource,
    NoneEnvironmentSource,
    ProcessEnvironmentSource,
)
from .errors import BastionVaultError, ErrorCategory, ErrorCodes
from .logger import ClientLogger, NoOpClientLogger
from .secrets import SecretString
from .settings import AutoRenew, RateGate
from .transport import RequestOptions, RetryPolicy, Transport


def specification_version() -> str:
    """Return the specification version this SDK implements (specifications/README.md)."""
    return SPECIFICATION_VERSION


def sdk_version() -> str:
    """Return this package's own release version (must match pyproject.toml)."""
    return SDK_VERSION


__all__ = [
    "AutoRenew",
    "BastionVaultError",
    "Client",
    "ClientConfig",
    "ClientLogger",
    "ClientOptions",
    "EnvironmentSource",
    "ErrorCategory",
    "ErrorCodes",
    "MapEnvironmentSource",
    "NoOpClientLogger",
    "NoneEnvironmentSource",
    "ProcessEnvironmentSource",
    "RateGate",
    "RequestOptions",
    "RetryPolicy",
    "SecretString",
    "Transport",
    "sdk_version",
    "specification_version",
]
