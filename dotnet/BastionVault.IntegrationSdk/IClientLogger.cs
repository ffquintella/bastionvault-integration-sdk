namespace BastionVault.IntegrationSdk;

/// <summary>
/// The runtime-idiomatic logging hook named as the <c>Logger</c> setting in
/// <c>specifications/02-client-configuration.md</c>. At milestone M1a only the warning-level line
/// CNF-030 requires (when <c>TlsSkipVerify</c> is true) is emitted; the full request/response
/// observability hook (CFG-080/081) is M1b.
/// </summary>
public interface IClientLogger
{
    /// <summary>Emits a warning-level line. Never called with secret material (CNF-031).</summary>
    void Warn(string message);

    /// <summary>
    /// Emits an info-level line. Never called with secret material (CNF-031). A default
    /// interface method so every existing implementer keeps compiling unchanged (D-M2-28
    /// item 4); an implementer that wants to observe AUT-095's line overrides it.
    /// </summary>
    void Info(string message)
    {
    }
}

/// <summary>The default <see cref="IClientLogger"/>: discards every message.</summary>
public sealed class NoOpClientLogger : IClientLogger
{
    /// <summary>The shared no-op instance.</summary>
    public static NoOpClientLogger Instance { get; } = new();

    private NoOpClientLogger()
    {
    }

    /// <summary>Discards <paramref name="message"/>.</summary>
    public void Warn(string message)
    {
    }
}
