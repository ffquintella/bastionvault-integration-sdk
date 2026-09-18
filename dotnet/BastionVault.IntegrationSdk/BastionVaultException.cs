using System.Diagnostics.CodeAnalysis;
using System.Text;
using BastionVault.IntegrationSdk.Internal;

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
public sealed class BastionVaultException : Exception, IRecognizedAtSource
{
    /// <summary>
    /// D-M2-25 item 2's marker, set once by the login runner on every error <i>it</i> produces.
    /// Not a public member: the distinction is internal to the executor's <c>BV-AUTH-017</c> guard
    /// and AUT-003's replay predicate, and a caller branches on <see cref="Code"/> instead.
    /// </summary>
    private bool recognizedAtSource;

    /// <summary>
    /// The transport-level failure kind behind this error, when one produced it (D-M5-5 limb (i)).
    /// Internal and set only by <see cref="TransportFailureMapper"/>.
    /// </summary>
    private TransportFailureKind? transportKind;

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
        // ERR-003 is applied once, here, so the one-line form, any verbose form and any hint that
        // interpolates the path are redacted by the same rule rather than by three that can drift.
        Path = ErrorPaths.Redact(path);
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

    /// <summary>
    /// Logical path, with namespace prefix for display, when a request was made. Any
    /// <c>lookup</c>/<c>renew</c>/<c>revoke</c>/<c>revoke-orphan</c> token segment is already
    /// replaced with <c>&lt;redacted&gt;</c> (ERR-003).
    /// </summary>
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

    /// <inheritdoc/>
    bool IRecognizedAtSource.RecognizedAtSource => recognizedAtSource;

    /// <summary>
    /// Marks this error as the login-response contract's own verdict (D-M2-25 item 2). Mutating
    /// the instance rather than rebuilding it keeps reference identity, which matters because a
    /// login failure is compared by identity in the tests that prove it reached the caller
    /// unwrapped; an exception is never shared between callers, so there is nothing to race.
    /// </summary>
    internal BastionVaultException MarkRecognizedAtSource()
    {
        recognizedAtSource = true;
        return this;
    }

    /// <summary>
    /// The transport-level failure kind behind this error, or <see langword="null"/> when it did not
    /// come from the transport.
    /// </summary>
    /// <remarks>
    /// Needed because <c>ConnectionRefused</c>, <c>Reset</c> and <c>Dns</c> all map to the one code
    /// <c>BV-TRANSPORT-001</c> (D-M1b-4a), while <c>DSC-041</c>'s node-failure list names the first
    /// two and <b>not</b> DNS — so the code cannot discriminate what D-M5-5 scopes limb (i) to.
    /// Internal, per-instance and set at the mapper, for the same reasons D-M2-25 chose a flag over
    /// a derived type: <see cref="BastionVaultException"/> is sealed because ERR-001 makes it the
    /// single error type, and Rust's single error struct cannot be subclassed in the parity pass
    /// either.
    /// </remarks>
    internal TransportFailureKind? TransportKind => transportKind;

    /// <summary>Records the transport failure kind that produced this error (D-M5-5 limb (i)).</summary>
    internal BastionVaultException MarkTransportKind(TransportFailureKind kind)
    {
        transportKind = kind;
        return this;
    }

    /// <summary>
    /// Returns a copy carrying <paramref name="hint"/> and every other field unchanged, for an
    /// ERR-040 note whose condition is only decidable at the operation rather than in
    /// <see cref="Internal.HintEnrichment"/>'s client-side context (M7c's KV-v2 row, DR-0012
    /// D-M7-32).
    /// </summary>
    /// <remarks>
    /// A copy rather than a mutable <c>Hint</c>: the type is otherwise immutable, an error is
    /// handed to a caller and to the observer hook, and a settable hint would let a second reader
    /// see a different message from the first.
    /// </remarks>
    internal BastionVaultException WithHint(string hint)
    {
        return new BastionVaultException(
            Code,
            Category,
            Message,
            hint,
            Retryable,
            Attempts,
            ServerMessage,
            ServerErrors,
            StatusCode,
            RetryAfter,
            Method,
            Path,
            Address,
            Details,
            InnerException)
        {
            transportKind = transportKind,
        };
    }

    /// <summary>
    /// ERR-002's one-line form: <c>"&lt;Code&gt;: &lt;Message&gt; — &lt;Hint&gt;"</c>, optionally followed
    /// by <c>" [HTTP &lt;status&gt; &lt;METHOD&gt; &lt;path&gt;]"</c> and <c>" (server: "&lt;ServerMessage&gt;")"</c>.
    /// Never contains a newline (ERR-002) and, per ERR-003, never contains secret material.
    /// </summary>
    public override string ToString()
    {
        StringBuilder builder = new();
        _ = builder.Append(Code).Append(": ").Append(Message).Append(" — ").Append(Hint);

        if (StatusCode is int status)
        {
            _ = builder.Append(" [HTTP ").Append(status);
            if (Method is not null)
            {
                _ = builder.Append(' ').Append(Method);
            }

            if (Path is not null)
            {
                _ = builder.Append(' ').Append(Path);
            }

            _ = builder.Append(']');
        }

        if (ServerMessage is not null)
        {
            _ = builder.Append(" (server: \"").Append(ServerMessage).Append("\")");
        }

        // ERR-002: newlines are not permitted in the one-line form. Nothing in the catalogue
        // carries one, but a server message is attacker-influenced input and must not be able to
        // forge a second log line.
        return ErrorPaths.OneLine(builder.ToString());
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
    {
        return new(code, ErrorCategory.Configuration, message, hint, retryable: false, attempts: 0, details: details, cause: cause);
    }

    /// <summary>
    /// Builds a request-scoped error from the generated <see cref="ErrorCatalog"/> entry for
    /// <paramref name="code"/>. <c>Retryable</c> comes from that entry, which the generator emits
    /// from Appendix B's <c>R</c> column and cross-checks against ERR-006's list at generation
    /// time (D-M1c-8) — independently of any
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
    {
        return new(
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
}
