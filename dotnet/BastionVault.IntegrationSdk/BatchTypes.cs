using System.Text.Json;

namespace BastionVault.IntegrationSdk;

/// <summary>
/// The four kinds of operation <c>POST /v2/sys/batch</c> accepts (14 §Batch endpoint). Spelled
/// lower-case on the wire.
/// </summary>
public enum BatchOperationKind
{
    /// <summary>A read of one logical path.</summary>
    Read,

    /// <summary>A write of one logical path; <see cref="BatchOperation.Data"/> is required (BAT-004).</summary>
    Write,

    /// <summary>A delete of one logical path.</summary>
    Delete,

    /// <summary>A list of one logical path.</summary>
    List,
}

/// <summary>
/// One operation in a <c>Sys.Batch</c> request (14 §Operation surface).
/// </summary>
/// <remarks>
/// <b>A batch is not a transaction.</b> BAT-008: the server runs the operations sequentially and
/// does not roll back, so a batch that fails half way through leaves the successful half applied.
/// Nothing in this API is named <c>Transaction</c>, and nothing in it should be read as implying
/// atomicity.
/// </remarks>
public sealed class BatchOperation
{
    /// <summary>Which of the four kinds this is.</summary>
    public required BatchOperationKind Operation { get; init; }

    /// <summary>
    /// BAT-003: the <b>full logical path including the mount</b> (<c>secret/data/x</c>), never
    /// prefixed with <c>/v1/</c>. A leading <c>/</c> is stripped by the SDK before sending.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// BAT-004: required for <see cref="BatchOperationKind.Write"/> and rejected
    /// (<c>BV-INPUT-001</c>) for the other three. Sent verbatim as the operation's <c>data</c>
    /// member, so a KV v2 write carries the engine's own <c>{"data": {…}}</c> envelope.
    /// </summary>
    public JsonElement? Data { get; init; }
}

/// <summary>
/// One operation's outcome in a <c>Sys.Batch</c> response (14 §Operation surface). The overall
/// call succeeds even when every operation failed (BAT-005), so a caller inspects these.
/// </summary>
public sealed class BatchResult
{
    /// <summary>The per-operation HTTP status the server reported.</summary>
    public required int Status { get; init; }

    /// <summary>The logical path the server echoed for this operation.</summary>
    public required string Path { get; init; }

    /// <summary>The operation's payload, or <see langword="null"/> when it carried none (a <c>204</c>, or a failure).</summary>
    public JsonElement? Data { get; init; }

    /// <summary>The server's <c>errors[]</c> for this operation; empty when it succeeded.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>The server's <c>warnings[]</c> for this operation; empty when it emitted none.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// BAT-005: the per-operation error, mapped through the same rules as a standalone response
    /// (section 04) from this operation's <see cref="Status"/> and <see cref="Errors"/>.
    /// <see langword="null"/> when <see cref="Status"/> is below 400.
    /// </summary>
    /// <remarks>
    /// It is a <see cref="BastionVaultException"/> carried as a value rather than thrown: BAT-005
    /// makes a failed operation a <i>result</i>, and a caller that wants to raise it can, while a
    /// caller reading a partially-successful batch is not forced into exception control flow for
    /// the normal case.
    /// </remarks>
    public BastionVaultException? Error { get; init; }
}
