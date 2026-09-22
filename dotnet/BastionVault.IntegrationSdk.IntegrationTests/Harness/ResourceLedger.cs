using System.Globalization;
using System.Text;

namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>
/// ITG-010 / ITG-011: everything a test creates is created <b>through</b> the ledger, so deleting
/// it in teardown is the default rather than something each scenario has to remember.
/// <para>
/// Teardown runs in <see cref="IAsyncDisposable.DisposeAsync"/>, which xUnit calls whether the
/// test passed, failed or threw - that is the "even on failure" half of ITG-010, and it is why
/// nothing here is done in a <c>finally</c> inside a test body.
/// </para>
/// <para>
/// Deletion failures are collected and reported, never thrown over a test failure: a leaked mount
/// must be visible, but it must not replace the assertion that explains why the test failed.
/// </para>
/// </summary>
public sealed class ResourceLedger : IAsyncDisposable
{
    private readonly BastionVaultClient client;
    private readonly List<Entry> entries = [];
    private readonly Lock sync = new();
    private readonly List<string> failures = [];

    internal ResourceLedger(BastionVaultClient client, string prefix)
    {
        this.client = client;
        Prefix = prefix;
    }

    /// <summary>
    /// The <c>it-&lt;run-id&gt;-&lt;test&gt;</c> prefix every name this ledger issues starts with.
    /// </summary>
    public string Prefix { get; }

    /// <summary>Deletion failures seen during teardown. Empty on a clean teardown.</summary>
    public IReadOnlyList<string> TeardownFailures
    {
        get
        {
            lock (sync)
            {
                return [.. failures];
            }
        }
    }

    /// <summary>A unique, prefixed name for anything the ledger has no typed helper for.</summary>
    public string Name(string label)
    {
        return $"{Prefix}-{Sanitise(label)}";
    }

    /// <summary>Registers an arbitrary cleanup. Later slices use this for resource kinds not listed here.</summary>
    public void Track(string kind, string name, Func<CancellationToken, Task> delete)
    {
        lock (sync)
        {
            entries.Add(new Entry(kind, name, delete));
        }
    }

    /// <summary>Mounts a secrets engine at a unique path and schedules its unmount. Returns the path with no trailing slash.</summary>
    public async Task<string> MountAsync(string type, string label, IReadOnlyDictionary<string, string>? options = null, CancellationToken cancellationToken = default)
    {
        string path = Name(label);
        Track("mount", path, ct => client.Sys.UnmountAsync(path, cancellationToken: ct));
        await client.Sys.MountAsync(
            path,
            new MountRequest { Type = type, Description = Description(), Options = options },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>Enables an auth method at a unique path and schedules its removal.</summary>
    public async Task<string> EnableAuthAsync(string type, string label, IReadOnlyDictionary<string, string>? options = null, CancellationToken cancellationToken = default)
    {
        string path = Name(label);
        Track("auth", path, ct => client.Sys.DisableAuthMethodAsync(path, cancellationToken: ct));
        await client.Sys.EnableAuthMethodAsync(
            path,
            new MountRequest { Type = type, Description = Description(), Options = options },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>Writes a uniquely named policy and schedules its deletion (ITG-011).</summary>
    public async Task<string> WritePolicyAsync(string label, string hcl, CancellationToken cancellationToken = default)
    {
        string name = Name(label);
        Track("policy", name, ct => client.Sys.DeletePolicyAsync(name, cancellationToken: ct));
        await client.Sys.WritePolicyAsync(name, hcl, cancellationToken: cancellationToken).ConfigureAwait(false);
        return name;
    }

    /// <summary>A uniquely named userpass user on <paramref name="mount"/>, deleted in teardown (ITG-011).</summary>
    public string TrackUser(string mount, string username)
    {
        Track("user", $"{mount}/{username}", ct => client.Auth.Userpass.Admin.DeleteUserAsync(username, mount, cancellationToken: ct));
        return username;
    }

    /// <summary>A namespace created by the test, deleted in teardown (ITG-011).</summary>
    public string TrackNamespace(string path)
    {
        Track("namespace", path, ct => client.Sys.DeleteNamespaceAsync(path, cancellationToken: ct));
        return path;
    }

    /// <summary>A token issued by the test, revoked in teardown (ITG-011).</summary>
    public void TrackToken(string label, SecretString token)
    {
        Track("token", Name(label), ct => client.Auth.Token.RevokeAsync(token.Value("token"), cancellationToken: ct));
    }

    /// <summary>
    /// The description stamped on every mount this ledger creates. It carries the creation instant
    /// so <see cref="OrphanCleaner"/> has a second, independent way to date a leftover mount when
    /// the run id in the path cannot be parsed.
    /// </summary>
    private static string Description()
    {
        return string.Create(CultureInfo.InvariantCulture, $"bastionvault-sdk integration test; created={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
    }

    internal static string Sanitise(string label)
    {
        StringBuilder builder = new StringBuilder(label.Length);
        foreach (char c in label.ToLowerInvariant())
        {
            _ = builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        string text = builder.ToString().Trim('-');
        return text.Length == 0 ? "x" : text[..Math.Min(text.Length, 40)];
    }

    public async ValueTask DisposeAsync()
    {
        List<Entry> pending;
        lock (sync)
        {
            pending = [.. entries];
            entries.Clear();
        }

        // Reverse order: a token issued against a mount is revoked before the mount goes.
        pending.Reverse();

        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        foreach (Entry entry in pending)
        {
            try
            {
                await entry.Delete(timeout.Token).ConfigureAwait(false);
            }
            catch (BastionVaultException ex)
            {
                // Already gone is a clean teardown, not a failure.
                if (ex.StatusCode is 404)
                {
                    continue;
                }

                lock (sync)
                {
                    failures.Add($"{entry.Kind} '{entry.Name}': {ex.Code}");
                }
            }
            catch (OperationCanceledException)
            {
                lock (sync)
                {
                    failures.Add($"{entry.Kind} '{entry.Name}': teardown timed out");
                }
            }
        }
    }

    private sealed record Entry(string Kind, string Name, Func<CancellationToken, Task> Delete);
}
