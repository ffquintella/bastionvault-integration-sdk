"""BastionVault Integration SDK — Python package.

M0 ships exactly one public surface (decision D-M0-12, amended by D-M0-12a): the
specification version this SDK implements and the package's own release version,
exposed as functions so the M0 package has an executed, coverage-instrumented
statement rather than inert module-level data. No client, transport, or operation
type is defined at this milestone. The function names are the cross-language parity
contract (CLA-003): Rust ships `specification_version()` / `sdk_version()`, .NET ships
`SdkInfo.SpecificationVersion` / `SdkInfo.SdkVersion`.
"""


def specification_version() -> str:
    """Return the specification version this SDK implements (specifications/README.md)."""
    return "1.0.0"


def sdk_version() -> str:
    """Return this package's own release version (must match pyproject.toml)."""
    return "0.2.1"
