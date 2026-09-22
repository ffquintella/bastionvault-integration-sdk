using System.Net;
using System.Net.Sockets;
using System.Text;
using BastionVault.IntegrationSdk.Internal;
using BastionVault.IntegrationSdk.Testing;
using BastionVault.IntegrationSdk.Tests.Harness;

namespace BastionVault.IntegrationSdk.Tests;

/// <summary>
/// DSC-050's built-in default <see cref="ISrvResolver"/>: wire-level coverage of
/// <see cref="DnsSrvResolver"/> against an in-process fake DNS server, below the level the shared
/// fixture corpus can reach (D-R16-6's own argument for why this needs dedicated tests — the
/// fixture corpus starts below the resolver).
/// </summary>
public sealed class DnsSrvResolverUnitTests
{
    private const string OwnerName = "_bvault._tcp.vault.corp.example";
    private static readonly string[] OwnerLabels = ["_bvault", "_tcp", "vault", "corp", "example"];

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_normal_udp_answer_is_returned()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            return BuildAnswer(id, OwnerLabels, [SrvRecordBytes(OwnerLabels, 10, 50, 8200, ["bv-1", "corp", "example"])]);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        IReadOnlyList<SrvRecord> records = await resolver.ResolveAsync(OwnerName);

        SrvRecord record = Assert.Single(records);
        Assert.Equal("bv-1.corp.example", record.Target);
        Assert.Equal(8200, record.Port);
        Assert.Equal(10, record.Priority);
        Assert.Equal(50, record.Weight);
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_truncated_udp_answer_triggers_the_identical_query_retried_over_tcp()
    {
        using FakeDnsServer server = new();
        ushort? udpId = null;
        ushort? tcpId = null;
        server.OnUdpQuery = query =>
        {
            udpId = ReadId(query);
            // TC=1 and no answers: DSC-050 requires the TCP retry regardless of what (if anything)
            // the truncated UDP payload carries.
            return Header(udpId.Value, response: true, truncated: true, qdCount: 1, anCount: 0)
                .Concat(Question(OwnerLabels, DnsTypeSrv, DnsClassIn))
                .ToArray();
        };
        server.OnTcpQuery = query =>
        {
            tcpId = ReadId(query);
            return BuildAnswer(tcpId.Value, OwnerLabels, [SrvRecordBytes(OwnerLabels, 10, 50, 8200, ["bv-tcp", "corp", "example"])]);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        IReadOnlyList<SrvRecord> records = await resolver.ResolveAsync(OwnerName);

        Assert.Equal("bv-tcp.corp.example", Assert.Single(records).Target);
        _ = Assert.NotNull(udpId);
        _ = Assert.NotNull(tcpId);
        // "Retry the identical query": the same query ID travels over both transports.
        Assert.Equal(udpId, tcpId);
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_single_label_name_is_rejected_without_a_query_being_sent()
    {
        using FakeDnsServer server = new();
        int queries = 0;
        server.OnUdpQuery = query =>
        {
            _ = query;
            _ = Interlocked.Increment(ref queries);
            return null;
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync("_singlelabel"));
        Assert.Equal(0, queries);
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task No_answer_is_ever_cached()
    {
        using FakeDnsServer server = new();
        string target = "bv-1";
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            return BuildAnswer(id, OwnerLabels, [SrvRecordBytes(OwnerLabels, 10, 50, 8200, [target, "corp", "example"])]);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);

        SrvRecord first = Assert.Single(await resolver.ResolveAsync(OwnerName));
        Assert.Equal("bv-1.corp.example", first.Target);

        target = "bv-2";
        SrvRecord second = Assert.Single(await resolver.ResolveAsync(OwnerName));
        Assert.Equal("bv-2.corp.example", second.Target);
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task An_off_topic_answer_record_is_skipped_and_yields_no_records()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            // A record for a different owner name, of a different type: DSC-050 requires it to be
            // ignored, not to crash the walk.
            byte[] offTopic = ResourceRecordBytes(["other", "example"], type: 1, cls: DnsClassIn, rdata: [1, 2, 3, 4]);
            return BuildAnswer(id, OwnerLabels, [offTopic]);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        IReadOnlyList<SrvRecord> records = await resolver.ResolveAsync(OwnerName);

        Assert.Empty(records);
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task Authority_and_additional_records_are_skipped_without_being_parsed()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            byte[] answer = SrvRecordBytes(OwnerLabels, 10, 50, 8200, ["bv-1", "corp", "example"]);
            // An authority record whose RDATA is deliberately unparseable garbage: DSC-050 requires
            // it to be skipped by RDLENGTH alone, never parsed for content.
            byte[] authority = ResourceRecordBytes(["ns", "example"], type: 2, cls: DnsClassIn, rdata: [0xFF, 0xFF, 0xFF]);
            byte[] additional = ResourceRecordBytes(["extra", "example"], type: 41, cls: DnsClassIn, rdata: []);
            byte[] header = Header(id, response: true, truncated: false, qdCount: 1, anCount: 1, nsCount: 1, arCount: 1);
            byte[] question = Question(OwnerLabels, DnsTypeSrv, DnsClassIn);
            return Concat(header, question, answer, authority, additional);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        IReadOnlyList<SrvRecord> records = await resolver.ResolveAsync(OwnerName);

        Assert.Equal("bv-1.corp.example", Assert.Single(records).Target);
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_mismatched_query_id_is_a_resolve_failure()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            unchecked
            {
                id += 1; // Deliberately wrong.
            }

            return BuildAnswer(id, OwnerLabels, [SrvRecordBytes(OwnerLabels, 10, 50, 8200, ["bv-1", "corp", "example"])]);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_response_with_the_qr_bit_unset_is_a_resolve_failure()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            // response: false — echoes the query back instead of answering it.
            return Header(id, response: false, truncated: false, qdCount: 1, anCount: 0)
                .Concat(Question(OwnerLabels, DnsTypeSrv, DnsClassIn))
                .ToArray();
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_response_echoing_a_different_question_is_a_resolve_failure()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            return BuildAnswer(id, ["not", "the", "question"], []);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_reserved_label_length_encoding_is_a_resolve_failure()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            byte[] header = Header(id, response: true, truncated: false, qdCount: 1, anCount: 1);
            byte[] question = Question(OwnerLabels, DnsTypeSrv, DnsClassIn);
            // A length octet with the reserved top-bit pattern `01` (0x40): not a normal label (`00`)
            // and not a compression pointer (`11`). DSC-050 requires this to be rejected outright
            // rather than mis-parsed as a 64-byte label by an implementation that masks the length
            // without checking the top bits first.
            byte[] answerName = [0x40, 0x00, 0x00];
            byte[] rrFixed = Concat(U16(DnsTypeSrv), U16(DnsClassIn), U32(0), U16(0));
            return Concat(header, question, answerName, rrFixed);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_decoded_name_over_255_bytes_is_a_resolve_failure()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            byte[] header = Header(id, response: true, truncated: false, qdCount: 1, anCount: 1);
            byte[] question = Question(OwnerLabels, DnsTypeSrv, DnsClassIn);
            // 130 two-byte labels decode to 130 * 3 = 390 bytes, over the 255-byte cap, using only
            // ordinary (legal) labels — no compression pointer is involved in this bound.
            string[] longLabels = Enumerable.Range(0, 130).Select(i => "ab").ToArray();
            byte[] answerName = Name(longLabels);
            byte[] rrFixed = Concat(U16(DnsTypeSrv), U16(DnsClassIn), U32(0), U16(0));
            return Concat(header, question, answerName, rrFixed);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_compression_pointer_that_does_not_point_backwards_is_a_resolve_failure()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            byte[] header = Header(id, response: true, truncated: false, qdCount: 1, anCount: 1);
            byte[] question = Question(OwnerLabels, DnsTypeSrv, DnsClassIn);
            int answerNameOffset = header.Length + question.Length;
            // Points at itself: not strictly backwards under any reading.
            ushort selfPointer = (ushort)(0xC000 | answerNameOffset);
            byte[] answerName = [(byte)(selfPointer >> 8), (byte)selfPointer];
            byte[] rrFixed = Concat(U16(DnsTypeSrv), U16(DnsClassIn), U32(0), U16(0));
            return Concat(header, question, answerName, rrFixed);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task Compression_pointer_hops_are_capped()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            byte[] header = Header(id, response: true, truncated: false, qdCount: 1, anCount: 1);
            byte[] question = Question(OwnerLabels, DnsTypeSrv, DnsClassIn);
            return BuildHopChainAnswer(header, question, hopCount: 200);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task A_lying_rdlength_that_overruns_the_message_is_a_resolve_failure()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            byte[] header = Header(id, response: true, truncated: false, qdCount: 1, anCount: 1);
            byte[] question = Question(OwnerLabels, DnsTypeSrv, DnsClassIn);
            byte[] answerName = Name(OwnerLabels);
            // RDLENGTH claims 100 bytes of RDATA but the message supplies none: the walk must not
            // silently clamp or resynchronise, it must fail closed.
            byte[] rrFixed = Concat(U16(DnsTypeSrv), U16(DnsClassIn), U32(0), U16(100));
            return Concat(header, question, answerName, rrFixed);
        };
        server.Start();

        DnsSrvResolver resolver = new([server.Endpoint]);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(OwnerName));
    }

    [Fact]
    [Requirement("DSC-050")]
    [Trait("Requirement", "DSC-050")]
    public async Task The_nameserver_override_is_used_in_preference_to_platform_discovery()
    {
        using FakeDnsServer server = new();
        server.OnUdpQuery = query =>
        {
            ushort id = ReadId(query);
            return BuildAnswer(id, OwnerLabels, [SrvRecordBytes(OwnerLabels, 10, 50, 8200, ["bv-1", "corp", "example"])]);
        };
        server.Start();

        FakeTransport transport = new();
        transport.EnqueueResponse(
            200,
            body: Encoding.UTF8.GetBytes("""{"initialized":true,"sealed":false,"standby":false,"cluster_healthy":true}"""));

        using BastionVaultClient client = new(
            new BastionVaultClientOptions
            {
                Address = "vault.corp.example",
                Token = "s.FAKE-token-0000000000000000",
                Transport = transport,
                RateGate = new RateGate { RatePerSecond = 0 },
                Discovery = new DiscoveryConfig { Nameservers = [server.Endpoint] },
            },
            EnvironmentSource.None);

        NodeSelection? pinned = await client.ConnectAsync();

        // A real platform lookup for "vault.corp.example" could never resolve to this fake server's
        // fixed answer: reaching it proves the override took effect, not platform discovery.
        Assert.Equal("https://bv-1.corp.example:8200", pinned!.Url);
    }

    // ---- helpers: a minimal in-process fake DNS server -------------------------------------

    private const ushort DnsTypeSrv = 33;
    private const ushort DnsClassIn = 1;

    private static ushort ReadId(byte[] message)
    {
        return (ushort)((message[0] << 8) | message[1]);
    }

    private static byte[] Header(ushort id, bool response, bool truncated, ushort qdCount, ushort anCount, ushort nsCount = 0, ushort arCount = 0)
    {
        ushort flags = 0;
        if (response)
        {
            flags |= 0x8000;
        }

        if (truncated)
        {
            flags |= 0x0200;
        }

        return Concat(U16(id), U16(flags), U16(qdCount), U16(anCount), U16(nsCount), U16(arCount));
    }

    private static byte[] U16(int value)
    {
        return [(byte)(value >> 8), (byte)value];
    }

    private static byte[] U32(uint value)
    {
        return [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    private static byte[] Name(IReadOnlyList<string> labels)
    {
        using MemoryStream stream = new();
        foreach (string label in labels)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
        return stream.ToArray();
    }

    private static byte[] Question(IReadOnlyList<string> labels, ushort type, ushort cls)
    {
        return Concat(Name(labels), U16(type), U16(cls));
    }

    private static byte[] ResourceRecordBytes(IReadOnlyList<string> ownerLabels, ushort type, ushort cls, byte[] rdata)
    {
        return Concat(Name(ownerLabels), U16(type), U16(cls), U32(300), U16((ushort)rdata.Length), rdata);
    }

    private static byte[] SrvRecordBytes(IReadOnlyList<string> ownerLabels, ushort priority, ushort weight, ushort port, IReadOnlyList<string> targetLabels)
    {
        byte[] rdata = Concat(U16(priority), U16(weight), U16(port), Name(targetLabels));
        return ResourceRecordBytes(ownerLabels, DnsTypeSrv, DnsClassIn, rdata);
    }

    private static byte[] BuildAnswer(ushort id, IReadOnlyList<string> questionLabels, IReadOnlyList<byte[]> answers)
    {
        byte[] header = Header(id, response: true, truncated: false, qdCount: 1, anCount: (ushort)answers.Count);
        byte[] question = Question(questionLabels, DnsTypeSrv, DnsClassIn);
        return Concat([header, question, .. answers]);
    }

    /// <summary>
    /// One answer record whose owner NAME is a strictly-backwards, strictly-decreasing chain of
    /// <paramref name="hopCount"/> compression pointers terminating in a root label — every hop
    /// individually legal, so only DSC-050's hop cap (not the backwards-pointer rule) can reject it.
    /// </summary>
    private static byte[] BuildHopChainAnswer(byte[] header, byte[] question, int hopCount)
    {
        int chainStart = header.Length + question.Length;
        byte[] chain = new byte[1 + (2 * hopCount)];
        chain[0] = 0x00; // The root label: "slot 0" of the chain, at chainStart.
        int previousOffset = chainStart;
        for (int k = 1; k <= hopCount; k++)
        {
            int slotOffset = chainStart + 1 + (2 * (k - 1));
            ushort pointer = (ushort)(0xC000 | previousOffset);
            chain[1 + (2 * (k - 1))] = (byte)(pointer >> 8);
            chain[2 + (2 * (k - 1))] = (byte)pointer;
            previousOffset = slotOffset;
        }

        int topSlotOffset = previousOffset;
        ushort nameHead = (ushort)(0xC000 | topSlotOffset);
        byte[] answerName = [(byte)(nameHead >> 8), (byte)nameHead];
        byte[] rrFixed = Concat(U16(DnsTypeSrv), U16(DnsClassIn), U32(0), U16(0));

        return Concat(header, question, chain, answerName, rrFixed);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream stream = new();
        foreach (byte[] part in parts)
        {
            stream.Write(part);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A loopback-only UDP+TCP DNS server a test fully controls, so DSC-050's bounds can be driven
    /// with deliberately malformed bytes without ever touching a real resolver or the network.
    /// </summary>
    private sealed class FakeDnsServer : IDisposable
    {
        private readonly Socket udpSocket;
        private readonly TcpListener tcpListener;
        private readonly CancellationTokenSource cts = new();

        public FakeDnsServer()
        {
            udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)udpSocket.LocalEndPoint!).Port;

            tcpListener = new TcpListener(IPAddress.Loopback, port);
            tcpListener.Start();

            Endpoint = new IPEndPoint(IPAddress.Loopback, port);
        }

        public IPEndPoint Endpoint { get; }

        /// <summary>Called once per received UDP datagram; a <see langword="null"/> return sends nothing back.</summary>
        public Func<byte[], byte[]?>? OnUdpQuery { get; set; }

        /// <summary>Called once per accepted TCP connection's query; a <see langword="null"/> return sends nothing back.</summary>
        public Func<byte[], byte[]?>? OnTcpQuery { get; set; }

        public void Start()
        {
            _ = Task.Run(() => RunUdpAsync(cts.Token));
            _ = Task.Run(() => AcceptTcpAsync(cts.Token));
        }

        private async Task RunUdpAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[ushort.MaxValue];
            while (!cancellationToken.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try
                {
                    result = await udpSocket
                        .ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }

                byte[] query = buffer[..result.ReceivedBytes];
                byte[]? response = OnUdpQuery?.Invoke(query);
                if (response is not null)
                {
                    try
                    {
                        _ = await udpSocket.SendToAsync(response, SocketFlags.None, result.RemoteEndPoint, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is OperationCanceledException or ObjectDisposedException or SocketException)
                    {
                        return;
                    }
                }
            }
        }

        private async Task AcceptTcpAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await tcpListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException)
                {
                    return;
                }

                _ = HandleTcpClientAsync(client, cancellationToken);
            }
        }

        private async Task HandleTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using TcpClient owned = client;
            try
            {
                NetworkStream stream = owned.GetStream();
                byte[] lengthBytes = new byte[2];
                await ReadExactAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
                int length = (lengthBytes[0] << 8) | lengthBytes[1];
                byte[] query = new byte[length];
                await ReadExactAsync(stream, query, cancellationToken).ConfigureAwait(false);

                byte[]? response = OnTcpQuery?.Invoke(query);
                if (response is not null)
                {
                    byte[] prefixed = new byte[2 + response.Length];
                    prefixed[0] = (byte)(response.Length >> 8);
                    prefixed[1] = (byte)response.Length;
                    Array.Copy(response, 0, prefixed, 2, response.Length);
                    await stream.WriteAsync(prefixed, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (failure is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The client (DnsSrvResolver) closed or never connected for this scenario; nothing
                // to report back to.
            }
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("The TCP connection closed before the expected bytes arrived.");
                }

                offset += read;
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            udpSocket.Dispose();
            tcpListener.Stop();
            cts.Dispose();
        }
    }
}
