namespace BastionVault.IntegrationSdk.Internal;

/// <summary>The kinds of transport-level failure a transport (production or fake) can raise (D-M1b-1, TRN-100).</summary>
public enum TransportFailureKind
{
    /// <summary>The connection was refused.</summary>
    ConnectionRefused,

    /// <summary>The request timed out.</summary>
    Timeout,

    /// <summary>Certificate verification failed.</summary>
    TlsVerify,

    /// <summary>The TLS handshake itself failed.</summary>
    TlsHandshake,

    /// <summary>The connection was reset.</summary>
    Reset,

    /// <summary>DNS resolution failed.</summary>
    Dns,
}

/// <summary>
/// Maps a <see cref="TransportFailureKind"/> to its fixed error code (D-M1b-4a):
/// <c>connection_refused</c>/<c>dns</c>/<c>reset</c> → <c>BV-TRANSPORT-001</c>; <c>timeout</c> →
/// <c>BV-TRANSPORT-002</c>; <c>tls_verify</c>/<c>tls_handshake</c> → <c>BV-TRANSPORT-003</c>.
/// </summary>
internal static class TransportFailureMapper
{
    public static BastionVaultException Map(TransportFailureKind kind, Exception? cause = null)
    {
        string code = kind switch
        {
            TransportFailureKind.ConnectionRefused => ErrorCodes.TransportConnectionFailed,
            TransportFailureKind.Dns => ErrorCodes.TransportConnectionFailed,
            TransportFailureKind.Reset => ErrorCodes.TransportConnectionFailed,
            TransportFailureKind.Timeout => ErrorCodes.TransportTimeout,
            TransportFailureKind.TlsVerify => ErrorCodes.TransportTlsError,
            TransportFailureKind.TlsHandshake => ErrorCodes.TransportTlsError,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognised transport failure kind."),
        };

        ErrorCatalogEntry entry = ErrorCatalog.Require(code);
        return BastionVaultException.Request(
            code,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: entry.Retryable,
            attempts: 1,
            cause: cause);
    }

    /// <summary>Builds the fixed <c>BV-TRANSPORT-004</c> error for a response over <c>MaxResponseBytes</c> (TRN-033).</summary>
    public static BastionVaultException MapResponseTooLarge(string method, string path, string address, int attempts)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.TransportResponseTooLarge);
        return BastionVaultException.Request(
            ErrorCodes.TransportResponseTooLarge,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: attempts,
            method: method,
            path: path,
            address: address);
    }

    /// <summary>Builds the fixed <c>BV-TRANSPORT-005</c> error for a cancelled operation (OVR-006).</summary>
    public static BastionVaultException MapCancelled(string method, string path, string address, int attempts)
    {
        ErrorCatalogEntry entry = ErrorCatalog.Require(ErrorCodes.TransportCancelled);
        return BastionVaultException.Request(
            ErrorCodes.TransportCancelled,
            entry.Category,
            entry.Message,
            entry.Hint,
            retryable: false,
            attempts: attempts,
            method: method,
            path: path,
            address: address);
    }
}
