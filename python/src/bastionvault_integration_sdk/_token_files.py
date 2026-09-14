"""The token-helper **write** path (CFG-031, CFG-032).

The read path is `config._read_token_helper_file`'s (CFG-030); both live behind one
function per direction so CFG-031's permission rule has exactly one call site.

Python does **not** yet implement `BV-CONFIG-010` (the CLI's encrypted `BVTOK1:` token
file), which .NET's read path does. That is an M1a-owned CFG-030 parity gap, not M2a's to
close, and no constant for the marker is defined here -- one would imply a rule this
package enforces and does not (D-M1c-25's spirit: no plausible-looking stub).
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Final

from .errors import BastionVaultError, ErrorCodes, make_config_error

#: `0600`: owner read/write only, as CFG-031 requires.
_OWNER_ONLY: Final = 0o600


def write(path: str, token: str) -> None:
    """CFG-031: write `token` to `path` with owner-only permissions (`0600`).

    The mode is applied at *creation* rather than by a `chmod` after the write, because
    write-then-chmod leaves a window in which the token exists on disk with the process
    umask's default mode.
    """

    try:
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, _OWNER_ONLY)
        with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
            # `O_CREAT`'s mode applies only to a file this call creates, and it is further
            # masked by the process umask; an existing file keeps whatever mode it had. So
            # the mode is re-applied unconditionally, and through the **descriptor** rather
            # than the path, which closes the window in which another process could swap
            # the path between the open and the chmod.
            os.fchmod(handle.fileno(), _OWNER_ONLY)
            handle.write(token)
    except (OSError, ValueError) as error:
        raise _not_writable(path, error) from error


def delete(path: str) -> None:
    """CFG-032: delete `path` if present, and do not fail if it is absent.

    `Path.unlink(missing_ok=True)` is already silent on a missing *file* but not on a
    missing *directory*, which is the same "absent" from the caller's point of view.
    """

    try:
        Path(path).unlink(missing_ok=True)
    except (FileNotFoundError, NotADirectoryError):
        return  # Absent is success (CFG-032).
    except (OSError, ValueError) as error:
        raise _not_writable(path, error) from error


def _not_writable(path: str, cause: BaseException) -> BastionVaultError:
    """`BV-CONFIG-011 TokenFileNotWritable` (D-M2-16, amending D-M2-13).

    Previously this would have been `BV-CONFIG-005 FileNotReadable`, whose message says the
    file cannot be *read* -- the wrong sentence for a failed `Auth.persist_token`, and one
    a caller matching on the code could not distinguish from a genuine read failure. The
    catalogue entry is generated from Appendix B, not transcribed here.
    """

    return make_config_error(
        ErrorCodes.CONFIG_TOKEN_FILE_NOT_WRITABLE, details={"path": path}, cause=cause
    )


__all__ = ["delete", "write"]
