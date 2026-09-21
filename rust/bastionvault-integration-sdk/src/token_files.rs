//! The token-helper **write** path (`CFG-031`, `CFG-032`).
//!
//! The read path is [`crate::config`]'s (`CFG-030`, and `BV-CONFIG-010` for an encrypted
//! file); both live behind one module per direction so the permission rule and the
//! `BVTOK1:` rule each have exactly one call site.

use std::fs;
use std::io;
use std::path::Path;

use crate::error::Error;

/// `CFG-031`: writes `token` to `path` with owner-only permissions (`0600`, or the
/// platform equivalent).
///
/// The mode is applied at **creation** rather than by a `chmod` after the write, because
/// write-then-chmod leaves a window in which the token exists on disk with the process
/// umask's default mode. The permissions are then asserted on the still-empty file as
/// well, which closes the other half of the same hole: a pre-existing token file with a
/// looser mode is not made owner-only by `O_CREAT`'s mode, which applies only when the
/// file is created.
///
/// On a platform with no unix mode, `CFG-031`'s "platform equivalent" is the ACL the file
/// inherits from the user-profile directory `TokenFile` defaults into.
pub(crate) fn write(path: &Path, token: &str) -> Result<(), Error> {
    write_inner(path, token).map_err(|cause| not_writable(path, cause))
}

fn write_inner(path: &Path, token: &str) -> io::Result<()> {
    use std::io::Write;

    let mut options = fs::OpenOptions::new();
    options.write(true).create(true).truncate(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(OWNER_ONLY);
    }
    let mut file = options.open(path)?;
    #[cfg(unix)]
    {
        // The file is open and empty at this point, so tightening the mode here cannot
        // expose any token material — it only covers the pre-existing-file case that the
        // creation mode above does not reach.
        fs::set_permissions(path, std::os::unix::fs::PermissionsExt::from_mode(OWNER_ONLY))?;
    }
    file.write_all(token.as_bytes())?;
    file.flush()
}

#[cfg(unix)]
const OWNER_ONLY: u32 = 0o600;

/// `CFG-032`: deletes `path` if it is present, and does not fail if it is absent. A
/// missing *directory* is the same "absent" from the caller's point of view as a missing
/// file, so both are success.
pub(crate) fn delete(path: &Path) -> Result<(), Error> {
    match fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(cause) if cause.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(cause) => Err(not_writable(path, cause)),
    }
}

/// `BV-CONFIG-011 TokenFileNotWritable` (D-M2-16, amending D-M2-13).
///
/// The .NET pathfinder first used `BV-CONFIG-005 FileNotReadable`, whose message says the
/// file cannot be *read* — the wrong sentence for a failed `Auth.PersistToken`, and one a
/// caller matching on the code could not tell apart from a genuine read failure. The
/// catalogue entry is generated from Appendix B, never transcribed here.
fn not_writable(path: &Path, cause: io::Error) -> Error {
    crate::error::catalog_errors::config_token_file_not_writable()
        .with_detail("path", path.display().to_string())
        .with_cause(cause)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn write_then_read_round_trips_the_token_cfg_031() {
        let directory = tempfile::tempdir().expect("a temporary directory");
        let path = directory.path().join(".vault-token");
        write(&path, crate::fake_tokens::PERSISTED).expect("the write must succeed");
        assert_eq!(
            fs::read_to_string(&path).expect("readable"),
            crate::fake_tokens::PERSISTED
        );
    }

    #[cfg(unix)]
    #[test]
    fn the_written_file_is_owner_only_cfg_031() {
        use std::os::unix::fs::PermissionsExt;
        let directory = tempfile::tempdir().expect("a temporary directory");
        let path = directory.path().join(".vault-token");
        write(&path, crate::fake_tokens::PERSISTED).expect("the write must succeed");
        let mode = fs::metadata(&path).expect("metadata").permissions().mode() & 0o777;
        assert_eq!(mode, 0o600, "CFG-031 requires 0600, got {mode:o}");
    }

    #[cfg(unix)]
    #[test]
    fn an_existing_world_readable_file_is_tightened_before_the_token_lands_cfg_031() {
        use std::os::unix::fs::PermissionsExt;
        let directory = tempfile::tempdir().expect("a temporary directory");
        let path = directory.path().join(".vault-token");
        fs::write(&path, "stale").expect("seed the file");
        fs::set_permissions(&path, fs::Permissions::from_mode(0o644)).expect("loosen it");
        write(&path, crate::fake_tokens::PERSISTED).expect("the write must succeed");
        let mode = fs::metadata(&path).expect("metadata").permissions().mode() & 0o777;
        assert_eq!(mode, 0o600, "a pre-existing loose mode must be tightened, got {mode:o}");
    }

    #[test]
    fn an_unwritable_path_is_config_011_with_the_path_in_details_d_m2_16() {
        let directory = tempfile::tempdir().expect("a temporary directory");
        // A directory that does not exist: the token file cannot be created under it.
        let path = directory.path().join("absent").join(".vault-token");
        let error = write(&path, crate::fake_tokens::PERSISTED).expect_err("the write must fail");
        assert_eq!(error.code(), "BV-CONFIG-011");
        assert_eq!(error.attempts(), 0);
        assert!(!error.retryable());
        assert_eq!(
            error.details().get("path"),
            Some(&crate::error::DetailValue::Str(path.display().to_string()))
        );
        // The write failure never quotes the token it failed to write.
        assert!(!format!("{error}").contains("FAKEpersisted"));
    }

    #[test]
    fn delete_removes_a_present_file_and_succeeds_on_an_absent_one_cfg_032() {
        let directory = tempfile::tempdir().expect("a temporary directory");
        let path = directory.path().join(".vault-token");
        write(&path, crate::fake_tokens::PERSISTED).expect("the write must succeed");
        delete(&path).expect("present is success");
        assert!(!path.exists());
        // "MUST NOT fail if it is absent" — twice over: missing file, and missing directory.
        delete(&path).expect("absent is success");
        delete(&directory.path().join("absent").join(".vault-token")).expect("absent directory is success");
    }

    #[cfg(unix)]
    #[test]
    fn a_delete_that_is_refused_is_config_011_not_silent_cfg_032() {
        use std::os::unix::fs::PermissionsExt;
        let directory = tempfile::tempdir().expect("a temporary directory");
        let nested = directory.path().join("locked");
        fs::create_dir(&nested).expect("create");
        let path = nested.join(".vault-token");
        fs::write(&path, crate::fake_tokens::PERSISTED).expect("seed");
        // A read-only *directory* refuses the unlink, which is not "absent" and must not be
        // reported as success.
        fs::set_permissions(&nested, fs::Permissions::from_mode(0o500)).expect("lock");
        let result = delete(&path);
        fs::set_permissions(&nested, fs::Permissions::from_mode(0o700)).expect("unlock");
        let error = result.expect_err("a refused unlink must be reported");
        assert_eq!(error.code(), "BV-CONFIG-011");
    }
}
