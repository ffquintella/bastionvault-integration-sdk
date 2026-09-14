namespace BastionVault.IntegrationSdk;

/// <summary>Which of AUT-001's three token-source variants a <see cref="TokenSource"/> is.</summary>
public enum TokenSourceKind
{
    /// <summary>A token held directly: from configuration, <c>SetToken</c>, or the token file.</summary>
    Static,

    /// <summary>A login performed on first use and cached (AUT-002). The login call itself is M2b.</summary>
    Login,

    /// <summary>An application-provided asynchronous function (e.g. secrets from a KMS).</summary>
    Callback,
}

/// <summary>
/// AUT-001's token source. A <see cref="BastionVaultClient"/> holds exactly one, reachable as
/// <see cref="AuthOperations.TokenSource"/>; <see cref="BastionVaultClient.SetToken"/> replaces it
/// with a <see cref="Static"/> one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ResolveAsync"/> is <b>asynchronous</b> in all three SDKs (D-M2-9), because two of the
/// three variants perform I/O. The executor resolves <i>through</i> this type rather than reading a
/// field, and it does so exactly once per pass, above the retry loop, which is CFG-070's
/// "in-flight requests keep the token they started with" (D-M1b-9 — the snapshot did not move,
/// only its source changed).
/// </para>
/// <para>
/// The <see cref="TokenSourceKind.Login"/> variant's public factory lands with its credential
/// types in M2b, together with AUT-002's login call. M2a ships the variant's <i>contract</i>: its
/// kind, and the single-flight guarantee of ruling D-M2-11(a), reachable internally so the
/// concurrency invariants are asserted in the slice that decides them rather than the slice that
/// first uses them. A factory added later is not a breaking change; a factory that throws today
/// would be a stub, which D-M1c-25 forbids.
/// </para>
/// </remarks>
public sealed class TokenSource
{
    private readonly SecretString staticToken;
    private readonly Func<CancellationToken, Task<SecretString>>? callback;
    private readonly Func<CancellationToken, Task<SecretString>>? login;

    /// <summary>
    /// D-M2-11(a)'s single-flight cell: <see cref="Lazy{T}"/> over the login <i>task</i>, with
    /// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>, so N concurrent first-use
    /// resolutions await <b>one</b> login rather than queueing N. Deliberately not a
    /// <c>SemaphoreSlim</c> around a null check, which admits the interleaving where two callers
    /// both observe "no cached token" before either takes the lock. Re-armed, not reset, on
    /// invalidation.
    /// </summary>
    private Lazy<Task<SecretString>>? loginFlight;

    private TokenSource(
        TokenSourceKind kind,
        SecretString staticToken,
        Func<CancellationToken, Task<SecretString>>? callback,
        Func<CancellationToken, Task<SecretString>>? login)
    {
        Kind = kind;
        this.staticToken = staticToken;
        this.callback = callback;
        this.login = login;
        if (login is not null)
        {
            Arm();
        }
    }

    /// <summary>
    /// Which variant this is. AUT-001 requires a client to hold exactly one source and
    /// <c>SetToken</c> to replace it with <see cref="TokenSourceKind.Static"/>; this is the
    /// observable that makes both assertable.
    /// </summary>
    public TokenSourceKind Kind { get; }

    /// <summary>A source holding <paramref name="token"/> directly (AUT-001).</summary>
    public static TokenSource Static(SecretString token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new TokenSource(TokenSourceKind.Static, token, null, null);
    }

    /// <summary>
    /// A source that calls <paramref name="callback"/> on every resolution (AUT-001). It is
    /// asynchronous because the specification's own example for this variant is a KMS lookup
    /// (D-M2-6), and it is deliberately <b>not</b> de-duplicated: a callback is the application's
    /// own function and the SDK does not get to coalesce its calls on its behalf (D-M2-11(a)).
    /// </summary>
    public static TokenSource Callback(Func<CancellationToken, Task<SecretString>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new TokenSource(TokenSourceKind.Callback, SecretString.Empty, callback, null);
    }

    /// <summary>
    /// The <see cref="TokenSourceKind.Login"/> variant with its login performed by
    /// <paramref name="login"/>. Internal at M2a: the performer is M2b's login call
    /// (<c>AUT-002</c>), and the single-flight machinery around it is M2a's (D-M2-11(a)).
    /// </summary>
    internal static TokenSource LoginWith(Func<CancellationToken, Task<SecretString>> login)
    {
        ArgumentNullException.ThrowIfNull(login);
        return new TokenSource(TokenSourceKind.Login, SecretString.Empty, null, login);
    }

    /// <summary>
    /// D-M2-9's seam: the token this source currently stands for, resolving it if that takes I/O.
    /// </summary>
    /// <remarks>
    /// A <see cref="TokenSourceKind.Login"/> resolution is single-flighted (D-M2-11(a)). An
    /// awaiting caller that cancels abandons only its own wait (<c>Task.WaitAsync</c>) and does
    /// <b>not</b> cancel the shared login, because one caller's cancellation must not fail the
    /// other callers awaiting the same flight. That is why the login runs under
    /// <see cref="CancellationToken.None"/> rather than under whichever caller happened to win
    /// the race to start it.
    /// <para>
    /// A flight that <i>fails</i> is not cached (D-M2-17): its awaiters all see its failure and
    /// none of them retries, and the next resolution after it attempts again.
    /// </para>
    /// </remarks>
    public async Task<SecretString?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (callback is not null)
        {
            return await callback(cancellationToken).ConfigureAwait(false);
        }

        if (login is null)
        {
            // Deliberately does not observe the cancellation token: a Static resolve performs no
            // I/O, so there is nothing to cancel, and throwing here would raise a bare
            // OperationCanceledException on a path where ERR-020/TRN-054 require a coded
            // BastionVaultException. Cancellation of a Static-sourced request is mapped where the
            // request is actually made (BV-TRANSPORT-005).
            return staticToken;
        }

        Lazy<Task<SecretString>> flight = Volatile.Read(ref loginFlight)!;
        try
        {
            return await flight.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // D-M2-17. Every awaiter of this flight observes this flight's failure and none of
            // them retries inside its own call — so there is no retry storm — but the *cell* is
            // re-armed so a later resolution attempts again. Without this, `Lazy` caches the
            // faulted task and one transient login failure at startup bricks the client for its
            // whole lifetime, which is emphatically not what D-M2-11(a) decided: that ruling made
            // concurrent callers share one login *attempt*, not one permanent verdict.
            //
            // The re-arm is conditional on *this flight* having actually finished badly. A caller
            // whose own WaitAsync was cancelled while the login is still running must leave the
            // flight alone — it is still the live attempt for everybody else. `IsCanceled` is
            // checked alongside `IsFaulted` because a performer that raises
            // OperationCanceledException itself lands the task in the cancelled state, and that is
            // just as poisonous to cache.
            Task<SecretString> attempted = flight.Value;
            if (attempted.IsFaulted || attempted.IsCanceled)
            {
                // CompareExchange, so the N awaiters of one failed flight re-arm it once between
                // them and a later resolution's fresh flight is never clobbered by a straggler.
                Interlocked.CompareExchange(ref loginFlight, NewFlight(), flight);
            }

            throw;
        }
    }

    /// <summary>
    /// Discards a cached <see cref="TokenSourceKind.Login"/> result so the next
    /// <see cref="ResolveAsync"/> logs in again, re-arming the single-flight cell rather than
    /// clearing it (D-M2-11(a)). A no-op on the other two variants: <see cref="TokenSourceKind.Static"/>
    /// has nothing to re-resolve and <see cref="TokenSourceKind.Callback"/> already resolves every time.
    /// </summary>
    internal void Invalidate()
    {
        if (login is not null)
        {
            Arm();
        }
    }

    private void Arm() => Volatile.Write(ref loginFlight, NewFlight());

    /// <summary>
    /// A fresh single-flight cell. The factory calls <see cref="InvokeLoginAsync"/> rather than the
    /// performer directly, because a performer that throws <i>synchronously</i> — before returning
    /// its task — would otherwise fault the <see cref="Lazy{T}"/> factory itself, and
    /// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> caches a factory exception and
    /// rethrows it forever, which not even a re-arm in <see cref="ResolveAsync"/> can reach. An
    /// <c>async</c> method never throws synchronously: it returns a faulted task, which is the case
    /// the re-arm does handle (D-M2-17).
    /// </summary>
    private Lazy<Task<SecretString>> NewFlight()
        => new(InvokeLoginAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private async Task<SecretString> InvokeLoginAsync()
        => await login!(CancellationToken.None).ConfigureAwait(false);
}
