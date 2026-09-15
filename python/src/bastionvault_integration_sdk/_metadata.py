"""Single source of truth for the package's own version metadata."""

from typing import Final

SDK_VERSION: Final[str] = "0.5.0"
SPECIFICATION_VERSION: Final[str] = "1.1.0"

# CNF-047: the upstream BastionVault release specifications/ was derived from, pinned in
# specifications/provenance.json as upstream.release / upstream.ref.
SPECIFICATION_SOURCE_RELEASE: Final[str] = "0.42.0"
SPECIFICATION_SOURCE_REF: Final[str] = "v0.42.0"
