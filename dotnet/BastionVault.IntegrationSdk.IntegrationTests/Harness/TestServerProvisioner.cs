namespace BastionVault.IntegrationSdk.IntegrationTests.Harness;

/// <summary>Turns the <see cref="ServerAvailability"/> verdict into a live <see cref="TestServer"/>.</summary>
internal static class TestServerProvisioner
{
    public static async Task<(TestServer Server, ManagedServer? Owned, IReadOnlyList<string> Warnings)> ProvisionAsync(
        TestMatrix matrix,
        string runId,
        CancellationToken cancellationToken)
    {
        ServerAvailability.Probe probe = ServerAvailability.Current;
        return probe.Mode switch
        {
            TestServerMode.External => (await ExternalAsync(runId, cancellationToken).ConfigureAwait(false), null, []),
            TestServerMode.Managed => await ManagedAsync(matrix, probe, runId, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(ServerAvailability.UnavailableReason),
        };
    }

    private static async Task<TestServer> ExternalAsync(string runId, CancellationToken cancellationToken)
    {
        string address = TestEnvironment.Get(TestEnvironment.Addr)!;
        string rootToken = TestEnvironment.Get(TestEnvironment.RootToken)
            ?? throw new InvalidOperationException(
                $"{TestEnvironment.Addr} is set but {TestEnvironment.RootToken} is not. The mode " +
                "table requires a root token for external mode; the harness will not guess one.");

        SecretString[] unsealKeys = (TestEnvironment.Get(TestEnvironment.UnsealKeys) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => new SecretString(k))
            .ToArray();

        string? caCertPath = TestEnvironment.Get(TestEnvironment.CaCert);
        string? caCertPem = caCertPath is null ? null : await File.ReadAllTextAsync(caCertPath, cancellationToken).ConfigureAwait(false);

        TestServer server = new TestServer(
            TestServerMode.External,
            address.TrimEnd('/'),
            new SecretString(rootToken),
            caCertPem,
            version: "unknown",
            TestEnvironment.Get(TestEnvironment.Namespace),
            TestEnvironment.Flag(TestEnvironment.TlsSkipVerify),
            unsealKeys,
            runId);

        // An external server may be sealed; unseal it when (and only when) the operator supplied
        // keys for it, which is what the mode table's "or BASTIONVAULT_TEST_UNSEAL_KEYS" is for.
        using (BastionVaultClient client = server.CreateClient())
        {
            SealStatus seal = await client.Sys.SealStatusAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (seal.Sealed)
            {
                if (unsealKeys.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"the external server at {address} is sealed and {TestEnvironment.UnsealKeys} is not set.");
                }

                foreach (SecretString? key in unsealKeys)
                {
                    seal = await client.Sys.UnsealAsync(key.Value("unseal key"), cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (!seal.Sealed)
                    {
                        break;
                    }
                }
            }
        }

        return await WithResolvedVersionAsync(server, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(TestServer, ManagedServer?, IReadOnlyList<string>)> ManagedAsync(
        TestMatrix matrix,
        ServerAvailability.Probe probe,
        string runId,
        CancellationToken cancellationToken)
    {
        List<string> warnings = [];
        ManagedServer managed = probe.BinaryPath is not null
            ? await ManagedBinaryServer.StartAsync(probe.BinaryPath, matrix.ManagedServer, runId, cancellationToken).ConfigureAwait(false)
            : await ManagedContainerServer.StartAsync(
                probe.ContainerRuntime!,
                TestEnvironment.Get(TestEnvironment.Image) ?? matrix.MinimumImage,
                matrix.ManagedServer,
                runId,
                cancellationToken).ConfigureAwait(false);

        try
        {
            BastionVaultClientOptions bootstrapOptions = new BastionVaultClientOptions
            {
                Address = managed.Address,
                ClusterDiscovery = false,
                Timeout = TimeSpan.FromSeconds(30),
            };

            SecretString rootToken;
            IReadOnlyList<SecretString> keys;
            using (BastionVaultClient bootstrap = new BastionVaultClient(bootstrapOptions))
            {
                using InitResult init = await bootstrap.Sys.InitAsync(
                    matrix.ManagedServer.InitShares,
                    matrix.ManagedServer.InitThreshold,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                // InitResult is disposable and zeroes its material; copy out before it goes.
                rootToken = new SecretString(init.RootToken.Reveal());
                keys = [.. init.Keys.Select(k => new SecretString(k.Reveal()))];
            }

            using (BastionVaultClient unsealer = new BastionVaultClient(bootstrapOptions))
            {
                foreach (SecretString key in keys)
                {
                    SealStatus status = await unsealer.Sys.UnsealAsync(key.Value("unseal key"), cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (!status.Sealed)
                    {
                        break;
                    }
                }
            }

            TestServer server = new TestServer(
                TestServerMode.Managed,
                managed.Address,
                rootToken,
                caCertPem: null,
                version: "unknown",
                @namespace: TestEnvironment.Get(TestEnvironment.Namespace),
                tlsSkipVerify: false,
                keys,
                runId);

            server = await WithResolvedVersionAsync(server, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(await ApplyDosDefaultsAsync(server, matrix.ManagedServer.DosDefaults, cancellationToken).ConfigureAwait(false));
            return (server, managed, warnings);
        }
        catch
        {
            await managed.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<TestServer> WithResolvedVersionAsync(TestServer server, CancellationToken cancellationToken)
    {
        using BastionVaultClient client = server.CreateClient();
        ServerInfo info = await client.Sys.ServerInfoAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        string version = string.IsNullOrWhiteSpace(info.Version) ? "unknown" : info.Version;

        return new TestServer(
            server.Mode,
            server.Address,
            server.RootToken,
            server.CaCertPem,
            version,
            server.Namespace,
            server.TlsSkipVerify,
            server.UnsealKeys,
            server.RunId);
    }

    /// <summary>
    /// test-matrix.json's <c>managedServer.dosConfigDefaults</c>. Best effort by design: a server
    /// that does not expose <c>sys/dos/config</c> is a version fact, not a harness failure, and
    /// ITG-S 27/28 gate on it themselves. The warning is carried into the run banner so a silently
    /// unconfigured gate cannot be mistaken for a configured one.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ApplyDosDefaultsAsync(
        TestServer server,
        DosDefaults defaults,
        CancellationToken cancellationToken)
    {
        try
        {
            using BastionVaultClient client = server.CreateClient();
            _ = await client.Sys.Dos.WriteConfigAsync(
                new DosConfig
                {
                    WindowSecs = defaults.WindowSecs,
                    MaxRequests = defaults.MaxRequests,
                    AuthMaxRequests = defaults.AuthMaxRequests,
                    BanSecs = defaults.BanSecs,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return [];
        }
        catch (BastionVaultException ex)
        {
            return [$"managedServer.dosConfigDefaults not applied: {ex.Code} ({ex.StatusCode})"];
        }
    }
}
