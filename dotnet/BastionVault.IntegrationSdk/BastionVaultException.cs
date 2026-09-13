using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The single error type every failure surfaced by the SDK is an instance of (ERR-001..ERR-006).
/// At milestone M1a only <see cref="ErrorCategory.Configuration"/> errors are ever constructed
/// (<c>decisions/0003-m1a-configuration.md</c>, D-M1a-1); every canonical field is present so
/// later milestones never need a breaking shape change.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "ERR-001 requires Code/Category/Hint on every instance; a message-only constructor would let one be constructed without them.")]
public sealed class BastionVaultException : Exception
{
    /// <summary>Constructs an error with every ERR-001 field.</summary>
    public BastionVaultException(
        string code,
        ErrorCategory category,
        string message,
        string hint,
        bool retryable,
        int attempts = 0,
        string? serverMessage = null,
        IReadOnlyList<string>? serverErrors = null,
        int? statusCode = null,
        TimeSpan? retryAfter = null,
        string? method = null,
        string? path = null,
        string? address = null,
        IReadOnlyDictionary<string, object?>? details = null,
        Exception? cause = null)
        : base(message, cause)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(message);
        ArgumentException.ThrowIfNullOrEmpty(hint);

        Code = code;
        Category = category;
        Hint = hint;
        Retryable = retryable;
        Attempts = attempts;
        ServerMessage = serverMessage;
        ServerErrors = serverErrors ?? Array.Empty<string>();
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        Method = method;
        Path = path;
        Address = address;
        Details = details ?? new Dictionary<string, object?>();
        Cause = cause;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Stable identifier, <c>BV-&lt;CATEGORY&gt;-&lt;NNN&gt;</c>. Never localised, never changed.</summary>
    public string Code { get; }

    /// <summary>One of the categories in <see cref="ErrorCategory"/>.</summary>
    public ErrorCategory Category { get; }

    /// <summary>Actionable guidance. Never empty (enforced at construction).</summary>
    public string Hint { get; }

    /// <summary>The raw server <c>error</c> string or joined <c>errors[]</c>, when the error came from a server response.</summary>
    public string? ServerMessage { get; }

    /// <summary>The raw <c>errors[]</c> array, when present.</summary>
    public IReadOnlyList<string> ServerErrors { get; }

    /// <summary>HTTP status, when a request was made.</summary>
    public int? StatusCode { get; }

    /// <summary>Parsed <c>Retry-After</c>, when present.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Whether an identical retry may succeed without operator/developer action (ERR-006).</summary>
    public bool Retryable { get; }

    /// <summary><c>GET</c>, <c>POST</c>, <c>LIST</c>, etc., when a request was made.</summary>
    public string? Method { get; }

    /// <summary>Logical path, with namespace prefix for display, when a request was made.</summary>
    public string? Path { get; }

    /// <summary>Server host (no credentials, no query string), when a request was made.</summary>
    public string? Address { get; }

    /// <summary>Number of attempts made (0 for configuration errors, which never send a request).</summary>
    public int Attempts { get; }

    /// <summary>Structured extras: <c>setting</c>, <c>path</c>, etc.</summary>
    public IReadOnlyDictionary<string, object?> Details { get; }

    /// <summary>Underlying runtime exception (IO, TLS, JSON), when this error wraps one.</summary>
    public Exception? Cause { get; }

    /// <summary>When this error was created (UTC).</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// ERR-002's one-line form: <c>"&lt;Code&gt;: &lt;Message&gt; — &lt;Hint&gt;"</c>, optionally followed
    /// by <c>" [HTTP &lt;status&gt; &lt;METHOD&gt; &lt;path&gt;]"</c> and <c>" (server: "&lt;ServerMessage&gt;")"</c>.
    /// Never contains a newline (ERR-002) and, per ERR-003, never contains secret material.
    /// </summary>
    public override string ToString()
    {
        StringBuilder builder = new();
        builder.Append(Code).Append(": ").Append(Message).Append(" — ").Append(Hint);

        if (StatusCode is int status)
        {
            builder.Append(" [HTTP ").Append(status);
            if (Method is not null)
            {
                builder.Append(' ').Append(Method);
            }

            if (Path is not null)
            {
                builder.Append(' ').Append(Path);
            }

            builder.Append(']');
        }

        if (ServerMessage is not null)
        {
            builder.Append(" (server: \"").Append(ServerMessage).Append("\")");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds a <c>BV-CONFIG-*</c> error: <c>Retryable = false</c>, <c>Attempts = 0</c>, and none of the
    /// request-scoped fields set, per D-M1a-1.
    /// </summary>
    internal static BastionVaultException Config(
        string code,
        string message,
        string hint,
        IReadOnlyDictionary<string, object?>? details = null,
        Exception? cause = null)
        => new(code, ErrorCategory.Configuration, message, hint, retryable: false, attempts: 0, details: details, cause: cause);

    /// <summary>
    /// Builds a request-scoped error from the <c>Internal.ErrorCatalogue</c> entry for
    /// <paramref name="code"/>. <c>Retryable</c> is computed from ERR-006 by
    /// <c>Internal.ErrorCatalogue.IsRetryable</c>, independently of any
    /// <see cref="IntegrationSdk.RetryPolicy.RetryOn"/> configuration (D-M1b-4b).
    /// </summary>
    internal static BastionVaultException Request(
        string code,
        ErrorCategory category,
        string message,
        string hint,
        bool retryable,
        int attempts,
        string? serverMessage = null,
        IReadOnlyList<string>? serverErrors = null,
        int? statusCode = null,
        TimeSpan? retryAfter = null,
        string? method = null,
        string? path = null,
        string? address = null,
        IReadOnlyDictionary<string, object?>? details = null,
        Exception? cause = null)
        => new(
            code,
            category,
            message,
            hint,
            retryable,
            attempts,
            serverMessage,
            serverErrors,
            statusCode,
            retryAfter,
            method,
            path,
            address,
            details,
            cause);
}
