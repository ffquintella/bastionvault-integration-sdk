namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// The attribute every integration scenario uses instead of <c>[Fact]</c>.
/// <para>
/// ITG-003 is enforced here, at xUnit <b>discovery</b> time: when neither provisioning mode is
/// possible, <see cref="Skip"/> returns the mandated reason and the runner reports the test as
/// skipped - never passed, never failed - without anything having tried to start a server. That
/// is also why <see cref="ServerAvailability"/> is a pure probe: discovery must stay free of
/// side effects.
/// </para>
/// <para>
/// It derives from <see cref="SkippableFactAttribute"/> so a scenario can <i>also</i> skip at run
/// time, which is what ITG-031's version gate needs (the server version is not knowable until a
/// server exists).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationFactAttribute : SkippableFactAttribute
{
    private string? explicitSkip;

    public override string? Skip
    {
        get => explicitSkip ?? (ServerAvailability.Current.IsAvailable ? null : ServerAvailability.UnavailableReason);
        set => explicitSkip = value;
    }
}

/// <summary>The <c>[Theory]</c> counterpart of <see cref="IntegrationFactAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationTheoryAttribute : SkippableTheoryAttribute
{
    private string? explicitSkip;

    public override string? Skip
    {
        get => explicitSkip ?? (ServerAvailability.Current.IsAvailable ? null : ServerAvailability.UnavailableReason);
        set => explicitSkip = value;
    }
}
