namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// D-M2-25 item 2's marker: distinguishes a failure the SDK's own login-response contract produced
/// <i>inside</i> a <see cref="TokenSource"/> from one a source delegate merely <b>leaked</b>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequestExecutor"/>'s <c>BV-AUTH-017</c> guard used to pass every
/// <see cref="BastionVaultException"/> through unwrapped, because M2b's <see cref="TokenSource"/>
/// login raises coded errors (<c>BV-AUTH-003</c>…<c>BV-AUTH-014</c>, <c>BV-AUTH-010</c>,
/// <c>BV-AUTH-011</c>, <c>BV-RATE-001</c>, and <c>BV-AUTHZ-001</c> for AUT-041's gated login) that
/// must reach the caller with their own code. The residue that ruling named: a
/// <see cref="TokenSourceKind.Callback"/> delegate that leaks an unrelated coded exception — one
/// raised by code inside the delegate that is not the login-response contract — surfaced with the
/// source's internal <c>Path</c> and <c>Method</c> masquerading as the outer request's.
/// </para>
/// <para>
/// <b>Marked by origin, not by a code whitelist.</b> D-M2-25 listed the codes, which was a
/// description of the set and not a mechanism: the same codes can also arrive from an application's
/// own call, and AUT-041's <c>BV-AUTHZ-001</c> is the source's own failure while the outer
/// request's <c>BV-AUTHZ-001</c> is the key AUT-003's replay turns on. A whitelist cannot tell
/// those two apart; where the error was produced can. So the login runner marks every error it
/// produces and nothing else does.
/// </para>
/// <para>
/// <b>Why a flag rather than a derived exception type.</b> D-M2-25's sketch had the marker
/// implemented "only by the exception types the recognizer raises", which needs a subtype —
/// and <see cref="BastionVaultException"/> is <c>sealed</c> because ERR-001 makes it the single
/// error type every SDK failure is an instance of. Unsealing it is a public API change, which that
/// same ruling states this is not; and Rust's single error struct cannot be subclassed at all, so a
/// subtype could not be transcribed in the parity pass. An internal interface implemented by the
/// one error type, carrying a per-instance flag, is checkable with the <c>is</c> test the ruling
/// names and transcribes to all three languages as one boolean field.
/// </para>
/// </remarks>
internal interface IRecognizedAtSource
{
    /// <summary>
    /// <see langword="true"/> when this failure is the login-response contract's own verdict, so
    /// the <c>BV-AUTH-017</c> guard must let it through and AUT-003's replay must not key on it.
    /// </summary>
    bool RecognizedAtSource { get; }
}
