"""BastionVault Integration SDK — Python package.

M1b adds the transport and logical layer (specifications/03-transport-and-protocol.md):
the async `Transport` seam, `Client.logical` (`Read`/`Write`/`Delete`/`List`/`Raw`), the
retry loop (CFG-050..055/RES-001..004), the status->code mapping (D-M1b-4), and the
runtime-mutation/observability surface (`SetToken`/`ClearToken`/`WithNamespace`,
`RequestObserver`). See `decisions/0004-m1b-transport.md`.
"""

from __future__ import annotations

from ._metadata import (
    SDK_VERSION,
    SPECIFICATION_SOURCE_REF,
    SPECIFICATION_SOURCE_RELEASE,
    SPECIFICATION_VERSION,
)
from .client import Client
from .config import ClientConfig, ClientOptions
from .environment import (
    EnvironmentSource,
    MapEnvironmentSource,
    NoneEnvironmentSource,
    ProcessEnvironmentSource,
)
from .errors import (
    BastionVaultError,
    ErrorCatalog,
    ErrorCatalogEntry,
    ErrorCategory,
    ErrorCodes,
)
from .logger import ClientLogger, NoOpClientLogger
from .logical import AuthInfo, RawResponse, Response
from .secrets import SecretString
from .settings import AutoRenew, RateGate
from .testing import FakeTransport
from .transport import (
    Clock,
    JitterSource,
    RateGateState,
    RequestEvent,
    RequestObserver,
    RequestOptions,
    RetryPolicy,
    Transport,
    TransportRequest,
    TransportResponse,
)


def specification_version() -> str:
    """Return the specification version this SDK implements (specifications/README.md)."""
    return SPECIFICATION_VERSION


def sdk_version() -> str:
    """Return this package's own release version (must match pyproject.toml)."""
    return SDK_VERSION


def specification_source_release() -> str:
    """Return the upstream BastionVault release specification_version() was derived from (CNF-047)."""
    return SPECIFICATION_SOURCE_RELEASE


def specification_source_ref() -> str:
    """Return the upstream git ref (tag) for specification_source_release() (CNF-047)."""
    return SPECIFICATION_SOURCE_REF


__all__ = [
    "AuthInfo",
    "AutoRenew",
    "BastionVaultError",
    "Client",
    "ClientConfig",
    "ClientLogger",
    "ClientOptions",
    "Clock",
    "EnvironmentSource",
    "ErrorCatalog",
    "ErrorCatalogEntry",
    "ErrorCategory",
    "ErrorCodes",
    "FakeTransport",
    "JitterSource",
    "MapEnvironmentSource",
    "NoOpClientLogger",
    "NoneEnvironmentSource",
    "ProcessEnvironmentSource",
    "RateGate",
    "RateGateState",
    "RawResponse",
    "RequestEvent",
    "RequestObserver",
    "RequestOptions",
    "Response",
    "RetryPolicy",
    "SecretString",
    "Transport",
    "TransportRequest",
    "TransportResponse",
    "sdk_version",
    "specification_source_ref",
    "specification_source_release",
    "specification_version",
]
