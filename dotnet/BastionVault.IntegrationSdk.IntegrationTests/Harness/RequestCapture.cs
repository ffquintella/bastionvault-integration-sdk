using System.Collections.Concurrent;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>ITG-022's observability hook, scoped to one <see cref="BastionVaultClient"/>.</summary>
/// <remarks>
/// Every event feeds both the ITG-020/022 checks in <see cref="RunAssertions"/> and the ITG-023
/// tally in the shared <see cref="SectionTally"/> passed at construction.
/// </remarks>
public sealed class RequestCapture : IRequestObserver
{
    private readonly SectionTally sections;
    private readonly ConcurrentQueue<RequestEvent> events = new();

    internal RequestCapture(SectionTally sections)
    {
        this.sections = sections;
    }

    /// <inheritdoc/>
    public void OnRequestCompleted(RequestEvent requestEvent)
    {
        events.Enqueue(requestEvent);
        sections.Record(requestEvent.Method, requestEvent.Path);
    }

    /// <summary>Every event observed so far.</summary>
    public IReadOnlyList<RequestEvent> Events => [.. events];
}
