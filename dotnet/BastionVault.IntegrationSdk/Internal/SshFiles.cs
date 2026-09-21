namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// SSH-002's <c>Ssh.WriteCertificateFile</c> write path. D-M9-12: follows
/// <see cref="TokenFiles.Write"/>'s creation shape exactly (<c>TokenFiles.cs:17,37-42,48</c>),
/// differing only in the mode: <c>0644</c> here, not <c>0600</c>, because an SSH certificate is
/// public material (unlike <see cref="TokenFiles"/>'s token). Also follows
/// <c>TokenFiles.cs:52-55</c>: a write failure other than the platform fallback surfaces as a
/// coded <see cref="BastionVaultException"/>, never a raw framework exception, out of a public
/// SDK method.
/// </summary>
internal static class SshFiles
{
    /// <summary><c>0644</c>: owner read/write, group and other read — world-readable by design (D-M9-12).</summary>
    private const UnixFileMode CertificateMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>
    /// The file-creation seam. Production always runs <see cref="CreateWithMode"/>; a test
    /// substitutes it to force the <see cref="PlatformNotSupportedException"/> fallback
    /// deterministically, since that branch is otherwise reachable only on Windows and D-M9-12
    /// forbids an <see cref="OperatingSystem.IsWindows"/> guard that would make the platform
    /// itself a second, competing way to reach it. A second test substitutes it to throw
    /// <see cref="UnauthorizedAccessException"/>, covering the coded-failure catch clause the same
    /// way.
    /// </summary>
    internal static Action<string, string> CreateWithMode { get; set; } = CreateFileWithMode;

    /// <summary>
    /// Writes <paramref name="signedKey"/> to <paramref name="path"/> with <c>0644</c>, applied at
    /// file <i>creation</i> via <see cref="FileStreamOptions.UnixCreateMode"/> rather than a
    /// <c>chmod</c> after the write, which would leave a window in which the file exists on disk
    /// with the process umask's default mode (D-M9-12). The Windows path is reached through
    /// <see cref="PlatformNotSupportedException"/> rather than an
    /// <see cref="OperatingSystem.IsWindows"/> guard, so the mode is always attempted first.
    /// </summary>
    public static void WriteCertificate(string path, string signedKey)
    {
        try
        {
            CreateWithMode(path, signedKey);
        }
        catch (PlatformNotSupportedException)
        {
            File.WriteAllText(path, signedKey);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw NotWritable(path, exception);
        }
    }

    private static void CreateFileWithMode(string path, string signedKey)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = CertificateMode,
        };
        using FileStream stream = new(path, options);
        using StreamWriter writer = new(stream);
        writer.Write(signedKey);
    }

    /// <summary>
    /// <c>BV-CONFIG-011</c> (ERR-036): the message comes from <see cref="ErrorCatalog"/>, the one
    /// table Appendix B and the generated Error Reference page are both built from, exactly as
    /// <see cref="TokenFiles"/>'s own <c>NotWritable</c> does (<c>TokenFiles.cs:70-79</c>). Minting a
    /// second message for this code would be an Appendix B prose change and therefore R3, out of
    /// scope here — but the in-scope answer is not a hard-coded competing sentence either; it is the
    /// catalogue's message plus a call-specific <b>hint</b>, which ERR-035 already permits varying
    /// per call site (the same pattern <c>AddressClassifier.cs:213-225</c> uses).
    /// </summary>
    private static BastionVaultException NotWritable(string path, Exception cause)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.ConfigTokenFileNotWritable);
        return BastionVaultException.Config(
            ErrorCodes.ConfigTokenFileNotWritable,
            entry.Message,
            "Check that the directory for the SSH certificate file in `Details.path` exists and the process user can write it.",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = path },
            cause);
    }
}
