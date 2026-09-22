namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-012's mechanism. xUnit runs test <i>collections</i> in parallel, and a collection attribute
/// alone cannot express "this test must be the only one touching the server", because two
/// different collections still overlap. This is a writer-preferring async reader/writer lock over
/// the single shared server: an ordinary scenario takes the shared side, a scenario derived from
/// <see cref="SerialIntegrationTest"/> takes the exclusive side.
/// <para>
/// Rejected alternative: one xUnit collection named "serial" plus
/// <c>[CollectionDefinition(DisableParallelization = true)]</c>. That serialises the members of
/// that collection against each other but not against the rest of the assembly, which is the
/// half that matters for seal/unseal and <c>sys/dos/config</c>. See D-M12-10.
/// </para>
/// </summary>
internal sealed class SerialGate
{
    private readonly Lock sync = new();
    private readonly Queue<TaskCompletionSource> waitingWriters = new();
    private readonly List<TaskCompletionSource> waitingReaders = [];
    private int activeReaders;
    private bool writerActive;

    public Task<IAsyncDisposable> AcquireSharedAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource source;
        lock (sync)
        {
            if (!writerActive && waitingWriters.Count == 0)
            {
                activeReaders++;
                return Task.FromResult<IAsyncDisposable>(new Releaser(this, exclusive: false));
            }

            source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waitingReaders.Add(source);
        }

        return AwaitAsync(source, exclusive: false, cancellationToken);
    }

    public Task<IAsyncDisposable> AcquireExclusiveAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource source;
        lock (sync)
        {
            if (!writerActive && activeReaders == 0)
            {
                writerActive = true;
                return Task.FromResult<IAsyncDisposable>(new Releaser(this, exclusive: true));
            }

            source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waitingWriters.Enqueue(source);
        }

        return AwaitAsync(source, exclusive: true, cancellationToken);
    }

    private async Task<IAsyncDisposable> AwaitAsync(TaskCompletionSource source, bool exclusive, CancellationToken cancellationToken)
    {
        await using (cancellationToken.Register(() => source.TrySetCanceled(cancellationToken)).ConfigureAwait(false))
        {
            await source.Task.ConfigureAwait(false);
        }

        return new Releaser(this, exclusive);
    }

    private void Release(bool exclusive)
    {
        lock (sync)
        {
            if (exclusive)
            {
                writerActive = false;
            }
            else
            {
                activeReaders--;
            }

            if (writerActive || activeReaders > 0)
            {
                return;
            }

            if (waitingWriters.Count > 0)
            {
                writerActive = true;
                _ = waitingWriters.Dequeue().TrySetResult();
                return;
            }

            foreach (TaskCompletionSource reader in waitingReaders)
            {
                activeReaders++;
                _ = reader.TrySetResult();
            }

            waitingReaders.Clear();
        }
    }

    private sealed class Releaser(SerialGate gate, bool exclusive) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                gate.Release(exclusive);
            }

            return ValueTask.CompletedTask;
        }
    }
}
