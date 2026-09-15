using BastionVault.IntegrationSdk.Internal;

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
/// The <see cref="TokenSourceKind.Login"/> variant's public factory (<see cref="Login"/>) lands in
/// M2b with AUT-002's login call. It is <b>declarative</b>: it records the method, the credentials
/// and the options, and a <see cref="BastionVaultClient"/> binds the performer that actually logs
/// in when the source is installed on it. That is why the factory needs no client and why an
/// application can build one before the client exists (<c>BastionVaultClientOptions.TokenSource</c>).
/// </para>
/// </remarks>
public sealed class TokenSource
{
    private readonly SecretString staticToken;
    private readonly Func<CancellationToken, Task<SecretString>>? callback;
    private readonly Func<CancellationToken, Task<SecretString>>? login;

    /// <summary>
    /// AUT-001's <c>Login(method, credentials, options)</c>, when this source came from
    /// <see cref="Login"/>. Absent for the other two variants and for the internal
    /// <see cref="LoginWith"/> seam, whose performer is supplied directly.
    /// </summary>
    private readonly LoginDescriptor? descriptor;

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
        Func<CancellationToken, Task<SecretString>>? login,
        LoginDescriptor? descriptor = null)
    {
        Kind = kind;
        this.staticToken = staticToken;
        this.callback = callback;
        this.login = login;
        this.descriptor = descriptor;
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
    /// AUT-001's <see cref="TokenSourceKind.Login"/> variant: the SDK logs in with
    /// <paramref name="credentials"/> lazily, on the first authenticated request, and again after
    /// an AUT-003 re-login (D-M2-9's ruling — <c>Client.Auth.AuthenticateAsync</c> forces it
    /// eagerly).
    /// </summary>
    /// <remarks>
    /// The returned source is not yet bound to a client, which is what lets it be handed to
    /// <c>BastionVaultClientOptions.TokenSource</c> before one exists. The client binds a performer
    /// at construction; the unbound instance itself never logs in, and
    /// <see cref="ResolveAsync"/> on it reports <c>BV-AUTH-001</c> rather than silently answering
    /// "no token".
    /// </remarks>
    /// <param name="method">Which flow to perform. Must agree with <paramref name="credentials"/>.</param>
    /// <param name="credentials">The credentials, retained in redacting types (AUT-100).</param>
    /// <param name="options">AUT-003's two settings; defaults when omitted.</param>
    /// <exception cref="ArgumentException"><paramref name="method"/> disagrees with <paramref name="credentials"/>.</exception>
    public static TokenSource Login(AuthMethod method, LoginCredentials credentials, LoginOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (method != credentials.Method)
        {
            // Checked rather than ignored: D-M2-6 pins both parameters, so the pair can disagree,
            // and silently preferring one of them would make `method` decorative on one call and
            // load-bearing on the next.
            throw new ArgumentException(
                $"The login method '{method}' does not match the credentials, which are for '{credentials.Method}'.",
                nameof(method));
        }

        return new TokenSource(
            TokenSourceKind.Login,
            SecretString.Empty,
            null,
            null,
            new LoginDescriptor(method, credentials, options ?? new LoginOptions()));
    }

    /// <summary>
    /// The <see cref="TokenSourceKind.Login"/> variant with its login performed by
    /// <paramref name="login"/> directly. The seam M2a shipped (D-M2-11(a)) and the one
    /// <see cref="BindTo"/> uses, so there is exactly one place the single-flight cell is armed.
    /// </summary>
    internal static TokenSource LoginWith(Func<CancellationToken, Task<SecretString>> login)
    {
        ArgumentNullException.ThrowIfNull(login);
        return new TokenSource(TokenSourceKind.Login, SecretString.Empty, null, login);
    }

    /// <summary>
    /// AUT-001's <c>Login</c> descriptor, when this source has one. Read by the client to bind a
    /// performer, and by AUT-003's replay predicate for <see cref="IntegrationSdk.LoginOptions"/>.
    /// </summary>
    internal LoginDescriptor? Descriptor => descriptor;

    /// <summary>
    /// The bound twin of this declarative <see cref="Login"/> source: same descriptor, plus the
    /// performer <paramref name="login"/> that actually issues the request.
    /// </summary>
    /// <remarks>
    /// A new instance rather than a mutation of this one. The application may hold the object it
    /// passed to <c>BastionVaultClientOptions</c>, and filling in a performer behind its back would
    /// make a public object's behaviour change at a moment the application did not choose. The
    /// client's <c>Auth.TokenSource</c> is the bound twin, whose <see cref="Kind"/> and
    /// <see cref="Descriptor"/> are identical, so AUT-001 still sees exactly one source.
    /// </remarks>
    internal TokenSource BindTo(Func<CancellationToken, Task<SecretString>> login)
    {
        ArgumentNullException.ThrowIfNull(login);
        return new TokenSource(TokenSourceKind.Login, SecretString.Empty, null, login, descriptor);
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

        if (login is null && descriptor is not null)
        {
            // A `Login` source built by the public factory but never installed on a client. It has
            // no performer, so it cannot log in. Reported as BV-AUTH-001 — "no token is
            // configured" is exactly what this source can offer — rather than returned as an empty
            // token, which would look like CFG-020's "no token" case while actually being a
            // misuse the caller can fix.
            ErrorCatalogEntry unbound = ErrorCatalog.Require(ErrorCodes.AuthNoToken);
            throw BastionVaultException.Request(
                ErrorCodes.AuthNoToken,
                unbound.Category,
                unbound.Message,
                unbound.Hint,
                retryable: unbound.Retryable,
                attempts: 0,
                details: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["reason"] = "This Login token source is not installed on a client, so the SDK has nothing to log in through.",
                });
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
                _ = Interlocked.CompareExchange(ref loginFlight, NewFlight(), flight);
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

    private void Arm()
    {
        Volatile.Write(ref loginFlight, NewFlight());
    }

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
    {
        return new(InvokeLoginAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private async Task<SecretString> InvokeLoginAsync()
    {
        return await login!(CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>
/// What <see cref="TokenSource.Login"/> records: AUT-001's <c>Login(method, credentials, options)</c>
/// triple, so the client can perform the login and AUT-003 can read its settings.
/// </summary>
/// <param name="Method">Which flow to perform.</param>
/// <param name="Credentials">The retained credentials (AUT-100).</param>
/// <param name="Options">AUT-003's <c>ReloginOnPermissionDenied</c> and <c>MinReloginInterval</c>.</param>
internal sealed record LoginDescriptor(AuthMethod Method, LoginCredentials Credentials, LoginOptions Options);
