"""BastionVault Integration SDK — Python package.

M2a adds the `Auth` area (`specifications/05-authentication.md`): AUT-001's `TokenSource`
and D-M2-9's asynchronous resolution seam, `Client.auth` (OVR-008), the nine token-store
operations, AUT-014's computed `remaining_ttl`, and the CFG-031/032 token-helper write
path. See `decisions/0006-m2-authentication.md`.

M1b added the transport and logical layer (specifications/03-transport-and-protocol.md):
the async `Transport` seam, `Client.logical` (`Read`/`Write`/`Delete`/`List`/`Raw`), the
retry loop (CFG-050..055/RES-001..004), the status->code mapping (D-M1b-4), and the
runtime-mutation/observability surface (`SetToken`/`ClearToken`/`WithNamespace`,
`RequestObserver`). See `decisions/0004-m1b-transport.md`.
"""

from __future__ import annotations

from ._metadata import SDK_VERSION, SPECIFICATION_VERSION
from .auth import AuthOperations, CreateTokenRequest, TokenInfo, TokenOperations
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
from .token_source import TokenSource, TokenSourceKind
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


__all__ = [
    "AuthInfo",
    "AuthOperations",
    "AutoRenew",
    "BastionVaultError",
    "Client",
    "ClientConfig",
    "ClientLogger",
    "ClientOptions",
    "Clock",
    "CreateTokenRequest",
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
    "TokenInfo",
    "TokenOperations",
    "TokenSource",
    "TokenSourceKind",
    "Transport",
    "TransportRequest",
    "TransportResponse",
    "sdk_version",
    "specification_version",
]
