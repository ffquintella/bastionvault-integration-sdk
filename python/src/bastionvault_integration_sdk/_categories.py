"""`ErrorCategory`, defined apart from `errors` so the generated catalogue can import it.

`_generated/error_catalog_data.py` imports `ErrorCategory` from `errors`, and `errors`
imports the generated rows. Holding the enum here makes that cycle resolvable in one
direction and only one: `errors` binds `ErrorCategory` on its first line, so by the time
the generated module asks `errors` for it, it is already there. The public home of the
enum is still `bastionvault_integration_sdk.errors` (and the package root); nothing
outside the package imports this module.
"""

from __future__ import annotations

from enum import Enum


class ErrorCategory(Enum):
    """One of the categories from specifications/04-error-model.md."""

    CONFIGURATION = "configuration"
    INPUT = "input"
    TRANSPORT = "transport"
    PROTOCOL = "protocol"
    AUTHENTICATION = "authentication"
    AUTHORIZATION = "authorization"
    NOT_FOUND = "not_found"
    CONFLICT = "conflict"
    RATE_LIMIT = "rate_limit"
    QUOTA = "quota"
    SERVER_STATE = "server_state"
    DISCOVERY = "discovery"
    ENGINE = "engine"


__all__ = ["ErrorCategory"]
