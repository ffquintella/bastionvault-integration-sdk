namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// The token-helper <b>write</b> path (CFG-031, CFG-032). The read path is
/// <see cref="ConfigurationResolver"/>'s (CFG-030, and BV-CONFIG-010 for an encrypted file);
/// both live behind one type per direction so the permission rule and the <c>BVTOK1:</c> rule
/// each have exactly one call site.
/// </summary>
internal static class TokenFiles
{
    /// <summary>
    /// The CLI's encrypted token-file marker. A file carrying it is <b>not</b> a token and must
    /// not be treated as one (BV-CONFIG-010).
    /// </summary>
    internal const string EncryptedPrefix = "BVTOK1:";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// CFG-031: writes <paramref name="token"/> to <paramref name="path"/> with owner-only
    /// permissions (<c>0600</c> or the platform equivalent).
    /// </summary>
    /// <remarks>
    /// The mode is applied at <i>creation</i> rather than by a <c>chmod</c> after the write,
    /// because write-then-chmod leaves a window in which the token exists on disk with the
    /// process umask's default mode. The Windows path is reached through
    /// <see cref="PlatformNotSupportedException"/> rather than through an
    /// <c>OperatingSystem.IsWindows()</c> guard, so the restrictive mode is always attempted and
    /// is never skipped by a wrong guess about which platforms support it; there, CFG-031's
    /// "platform equivalent" is the ACL the file inherits from the user-profile directory
    /// <c>TokenFile</c> defaults into.
    /// </remarks>
    public static void Write(string path, string token)
    {
        try
        {
            FileStreamOptions options = new()
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = OwnerOnly,
            };
            using FileStream stream = new(path, options);
            using StreamWriter writer = new(stream);
            writer.Write(token);
        }
        catch (PlatformNotSupportedException)
        {
            File.WriteAllText(path, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw NotWritable(path, exception);
        }
    }

    /// <summary>
    /// CFG-032: deletes <paramref name="path"/> if present, and does not fail if it is absent.
    /// <see cref="File.Delete"/> is already silent on a missing <i>file</i> but not on a missing
    /// <i>directory</i>, which is the same "absent" from the caller's point of view.
    /// </summary>
    /// <summary>
    /// <c>BV-CONFIG-011 TokenFileNotWritable</c> (D-M2-16, amending D-M2-13). Previously this was
    /// <c>BV-CONFIG-005 FileNotReadable</c>, whose message says the file cannot be <i>read</i> —
    /// the wrong sentence for a failed <c>Auth.PersistToken</c>, and one a caller matching on the
    /// code could not distinguish from a genuine read failure. The catalogue entry is generated
    /// from Appendix B, not transcribed here.
    /// </summary>
    private static BastionVaultException NotWritable(string path, Exception cause)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ConfigTokenFileNotWritable);
        return BastionVaultException.Config(
            ErrorCodes.ConfigTokenFileNotWritable,
            entry.Message,
            entry.Hint,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = path },
            cause);
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is DirectoryNotFoundException or FileNotFoundException)
        {
            // Absent is success (CFG-032).
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw NotWritable(path, exception);
        }
    }
}
