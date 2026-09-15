namespace BastionVault.IntegrationSdk;

/// <summary>
/// <c>Sys.Init</c>'s result (SYS-010, SYS-011): the unseal key shares and the initial root token.
/// This is the single most sensitive payload the API produces — it is returned exactly once, by a
/// server that keeps no copy — so the type is built around not retaining it any longer than the
/// caller asks for.
/// </summary>
/// <remarks>
/// <para>
/// SYS-011 requires the two members to use a redacting type, to be zeroed on dispose <i>where
/// possible</i>, and never to be logged. All three are honoured as follows.
/// </para>
/// <para>
/// <b>Redacting.</b> <see cref="Keys"/> and <see cref="RootToken"/> are
/// <see cref="SecretString"/>, whose <see cref="SecretString.ToString"/> is <c>[REDACTED]</c>
/// (CNF-031, DR-0003 D-M1a-9). <see cref="ToString"/> here is redacting for the same reason: an
/// interpolated <c>InitResult</c> is the likeliest accidental log line.
/// </para>
/// <para>
/// <b>Zeroed on dispose.</b> The material this instance <i>owns</i> is held in
/// <see cref="char"/> arrays, which <see cref="Dispose"/> overwrites with <c>'\0'</c>. That is the
/// whole of what "where possible" can mean on .NET: <see cref="string"/> is immutable and may be
/// interned, so a <see cref="string"/> field could not be zeroed at all. Two residues are
/// unavoidable and are stated rather than papered over — the transient strings
/// <c>System.Text.Json</c> materialises while parsing the response body, and any string a caller
/// obtains from <see cref="SecretString.Reveal"/>. Both are ordinary garbage; neither is reachable
/// from this instance after construction and dispose respectively.
/// </para>
/// <para>
/// Because the buffers are what is owned, <see cref="Keys"/> and <see cref="RootToken"/> build a
/// fresh <see cref="SecretString"/> on each read rather than caching one: a cached
/// <see cref="SecretString"/> would hold a <see cref="string"/> this type could never clear, which
/// would make <see cref="Dispose"/> a claim rather than an act. After <see cref="Dispose"/> both
/// throw <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public sealed class InitResult : IDisposable
{
    private readonly char[][] keyBuffers;
    private readonly char[] rootTokenBuffer;
    private bool disposed;

    internal InitResult(IReadOnlyList<string> keys, string rootToken)
    {
        keyBuffers = keys.Select(key => key.ToCharArray()).ToArray();
        rootTokenBuffer = rootToken.ToCharArray();
    }

    /// <summary>
    /// The unseal key shares, in the order the server returned them. A fresh list of fresh
    /// <see cref="SecretString"/> instances on each read; see the type's remarks.
    /// </summary>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    public IReadOnlyList<SecretString> Keys
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return keyBuffers.Select(buffer => new SecretString(new string(buffer))).ToArray();
        }
    }

    /// <summary>The initial root token. A fresh <see cref="SecretString"/> on each read; see the type's remarks.</summary>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    public SecretString RootToken
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new SecretString(new string(rootTokenBuffer));
        }
    }

    /// <summary>How many key shares the server returned. Readable after <see cref="Dispose"/>; a count is not secret material.</summary>
    public int KeyCount => keyBuffers.Length;

    /// <summary>Whether <see cref="Dispose"/> has run, and therefore whether the owned buffers have been zeroed.</summary>
    public bool IsDisposed => disposed;

    /// <summary>SYS-011: overwrites every owned buffer with <c>'\0'</c>. Idempotent.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (char[] buffer in keyBuffers)
        {
            Array.Clear(buffer);
        }

        Array.Clear(rootTokenBuffer);
        disposed = true;
    }

    /// <summary>SYS-011: always redacted, and never carries a key share or the root token.</summary>
    public override string ToString()
    {
        return $"InitResult {{ Keys = {keyBuffers.Length} x [REDACTED], RootToken = [REDACTED] }}";
    }
}
