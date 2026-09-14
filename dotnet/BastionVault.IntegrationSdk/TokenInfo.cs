namespace BastionVault.IntegrationSdk;

/// <summary>
/// A token-store lookup's <c>data</c> object (05 §Token store operations), as returned by
/// <see cref="TokenOperations.LookupAsync"/> and <see cref="TokenOperations.LookupSelfAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The wire <c>ttl</c> field is <b>not</b> exposed: it is always <c>0</c> on the wire, and the
/// specification requires the SDK to compute <see cref="RemainingTtl"/> instead and to expose
/// neither the wire value nor a guess at it (AUT-014). There is deliberately no <c>Ttl</c> member;
/// a test asserts its absence, because "MUST NOT expose" is only verifiable negatively.
/// </para>
/// <para>
/// <see cref="Id"/> is a <see cref="SecretString"/>. The specification names the field <c>id</c>
/// and does not name its type, but a lookup's <c>id</c> <i>is</i> token material, and
/// <see cref="AuthOperations.TokenInfo"/> is public — a plain string would make
/// <c>Auth.TokenInfo.Id</c> a second, non-redacting way to read the very token AUT-004 requires
/// <see cref="AuthOperations.CurrentToken"/> to redact (CNF-031, CNF-032).
/// </para>
/// </remarks>
public sealed class TokenInfo
{
    /// <summary>The token itself, held redacting (CNF-031); see the type remarks.</summary>
    public SecretString? Id { get; init; }

    /// <summary>Policies attached to the token.</summary>
    public IReadOnlyList<string> Policies { get; init; } = Array.Empty<string>();

    /// <summary>The auth path the token was issued from, e.g. <c>auth/userpass/login/alice</c>.</summary>
    public string? Path { get; init; }

    /// <summary>The token's metadata map.</summary>
    public IReadOnlyDictionary<string, string>? Meta { get; init; }

    /// <summary>The token's display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Remaining uses, <c>0</c> for unlimited.</summary>
    public int NumUses { get; init; }

    /// <summary>Wire <c>creation_time</c>, a unix timestamp, as an instant.</summary>
    public DateTimeOffset? CreationTime { get; init; }

    /// <summary>Wire <c>creation_ttl</c> in seconds; <see cref="TimeSpan.Zero"/> means "no TTL".</summary>
    public TimeSpan CreationTtl { get; init; }

    /// <summary>Wire <c>explicit_max_ttl</c> in seconds.</summary>
    public TimeSpan ExplicitMaxTtl { get; init; }

    /// <summary>Wire <c>period</c> in seconds, when the token is periodic.</summary>
    public TimeSpan? Period { get; init; }

    /// <summary>
    /// AUT-014: <c>CreationTime + CreationTtl − Clock.NowUtc()</c>, and <see langword="null"/>
    /// when <see cref="CreationTtl"/> is zero. Computed from the injected clock at parse time, so
    /// a fixture that declares <c>clock.start</c> pins it exactly (D-M2-7).
    /// </summary>
    public TimeSpan? RemainingTtl { get; init; }
}

/// <summary>
/// The request body of <see cref="TokenOperations.CreateAsync"/> (05 §Token store operations,
/// AUT-081, AUT-082). Every property maps to the wire field of the same <c>snake_case</c> name and
/// is omitted from the body when left unset (OVR-007).
/// </summary>
public sealed record CreateTokenRequest
{
    /// <summary>Wire <c>policies</c>.</summary>
    public IReadOnlyList<string>? Policies { get; init; }

    /// <summary>Wire <c>ttl</c>, sent in seconds.</summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>Wire <c>period</c>, sent in seconds.</summary>
    public TimeSpan? Period { get; init; }

    /// <summary>Wire <c>num_uses</c>.</summary>
    public int? NumUses { get; init; }

    /// <summary>Wire <c>renewable</c>. Default <see langword="true"/>, as the server's is.</summary>
    public bool Renewable { get; init; } = true;

    /// <summary>Wire <c>meta</c>. Reserved keys are refused client-side (AUT-081).</summary>
    public IReadOnlyDictionary<string, string>? Meta { get; init; }

    /// <summary>Wire <c>display_name</c>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Wire <c>explicit_max_ttl</c>, sent in seconds.</summary>
    public TimeSpan? ExplicitMaxTtl { get; init; }

    /// <summary>Wire <c>no_default_policy</c>.</summary>
    public bool? NoDefaultPolicy { get; init; }

    /// <summary>Wire <c>no_parent</c> (root only).</summary>
    public bool? NoParent { get; init; }

    /// <summary>Wire <c>id</c> (root only).</summary>
    public string? Id { get; init; }

    /// <summary>Wire <c>type</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Wire <c>child_visible</c>.</summary>
    public bool? ChildVisible { get; init; }

    /// <summary>
    /// AUT-082's opt-in: when <see langword="true"/> the created token replaces the client's own
    /// (which AUT-001 makes a <see cref="TokenSourceKind.Static"/> source). Default
    /// <see langword="false"/> — <c>Create</c> never switches the client's token unless asked.
    /// </summary>
    public bool UseResult { get; init; }
}
