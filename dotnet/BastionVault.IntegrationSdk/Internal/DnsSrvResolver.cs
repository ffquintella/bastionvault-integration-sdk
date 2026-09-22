using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace BastionVault.IntegrationSdk.Internal;

/// <summary>
/// DSC-050's built-in default <see cref="ISrvResolver"/>: a hand-rolled, zero-dependency SRV
/// resolver over UDP with a mandatory TCP retry on truncation (D-R16-6, D-R16-9). One instance is
/// constructed per client, in <see cref="BastionVaultClient"/>'s constructor, and only when no
/// application resolver was supplied — an injected <see cref="ISrvResolver"/> always wins and this
/// type is never consulted (DSC-014, DSC-050).
/// </summary>
/// <remarks>
/// <para>
/// <b>Trust boundary:</b> DNS here is unauthenticated either way, and RES-010/CFG-043 verify TLS
/// against the SRV target, so a spoofed answer yields a certificate failure, not a credential
/// leak (D-R16-9). This parser is therefore hardened against malformed and adversarial input for
/// robustness, not because it is the trust boundary — it is not.
/// </para>
/// <para>
/// No answer is ever cached (DSC-050): every call re-queries the wire. <c>DSC-042</c> already
/// re-probes the cached <em>candidate</em> set without a fresh SRV lookup, so nothing needs this
/// resolver itself to remember an answer.
/// </para>
/// </remarks>
internal sealed class DnsSrvResolver : ISrvResolver
{
    private const int DnsPort = 53;
    private const int MaxLabelLength = 63;
    private const int MaxNameLength = 255;

    // The wire format cannot desync into a loop under the strictly-backwards rule below, but a
    // finite cap is what DSC-050 asks for explicitly rather than relying on that argument alone.
    private const int MaxPointerHops = 128;

    private const ushort DnsTypeSrv = 33;
    private const ushort DnsClassIn = 1;

    private readonly IReadOnlyList<IPEndPoint>? configuredNameservers;

    public DnsSrvResolver(IReadOnlyList<IPEndPoint>? configuredNameservers)
    {
        this.configuredNameservers = configuredNameservers is { Count: > 0 } ? configuredNameservers : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SrvRecord>> ResolveAsync(string ownerName, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> questionLabels = SplitAbsoluteLabels(ownerName);

        IReadOnlyList<IPEndPoint> nameservers = configuredNameservers ?? DiscoverPlatformNameservers();
        if (nameservers.Count == 0)
        {
            throw new InvalidOperationException(
                "No DSC-050 nameservers are configured, and none could be discovered from the platform.");
        }

        Exception? lastFailure = null;
        foreach (IPEndPoint nameserver in nameservers)
        {
            try
            {
                return await QueryAsync(nameserver, questionLabels, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                lastFailure = failure;
            }
        }

        throw new InvalidOperationException("The SRV query failed against every configured nameserver.", lastFailure);
    }

    /// <summary>
    /// DSC-050: absolute-only qualification. No search-list or <c>ndots</c> emulation — a
    /// single-label name is rejected outright rather than guessed at (D-R16-8).
    /// </summary>
    private static string[] SplitAbsoluteLabels(string ownerName)
    {
        string[] labels = ownerName.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2)
        {
            throw new InvalidOperationException(
                $"'{ownerName}' is a single-label name. The default SRV resolver qualifies absolute names only " +
                "(DSC-050) and does not emulate a search list; configure an absolute, multi-label cluster name.");
        }

        foreach (string label in labels)
        {
            if (Encoding.ASCII.GetByteCount(label) is 0 or > MaxLabelLength)
            {
                throw new InvalidOperationException($"'{ownerName}' has a label that is empty or exceeds 63 bytes.");
            }
        }

        return labels;
    }

    /// <summary>
    /// D-R16-6/D-R16-7: the platform's configured nameservers, best-effort. Known imperfect on a
    /// macOS split-horizon VPN (accepted residual, roadmap risk R-26) — not fixed here, and never
    /// consulted at all once <see cref="DiscoveryConfig.Nameservers"/> is set.
    /// </summary>
    private static List<IPEndPoint> DiscoverPlatformNameservers()
    {
        List<IPEndPoint> nameservers = [];
        try
        {
            HashSet<IPAddress> seen = [];
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (IPAddress address in nic.GetIPProperties().DnsAddresses)
                {
                    if (seen.Add(address))
                    {
                        nameservers.Add(new IPEndPoint(address, DnsPort));
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Surfaced as "no nameservers" at the point of use (a resolve failure), not as a
            // client-construction failure — consistent with DSC-011 treating every resolver
            // problem as "no records" except where DSC-017 requires it to be loud.
        }

        return nameservers;
    }

    private static async Task<IReadOnlyList<SrvRecord>> QueryAsync(
        IPEndPoint nameserver,
        IReadOnlyList<string> questionLabels,
        CancellationToken cancellationToken)
    {
        byte[] query = BuildQuery(questionLabels, out ushort queryId);

        byte[] udpResponse = await SendUdpAsync(nameserver, query, cancellationToken).ConfigureAwait(false);
        bool truncated = ValidateHeaderAndCheckTruncated(udpResponse, queryId);

        byte[] response = truncated
            ? await SendTcpAsync(nameserver, query, cancellationToken).ConfigureAwait(false)
            : udpResponse;

        return ParseSrvAnswer(response, queryId, questionLabels);
    }

    // ---- Wire I/O ---------------------------------------------------------------------------

    private static async Task<byte[]> SendUdpAsync(IPEndPoint nameserver, byte[] query, CancellationToken cancellationToken)
    {
        using Socket socket = new(nameserver.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        // Connecting the (unauthenticated) UDP socket, rather than SendTo/ReceiveFrom, both picks
        // an ephemeral local source port (DSC-050) and makes the OS itself drop a datagram from
        // any address but the queried nameserver, before this code ever sees it.
        await socket.ConnectAsync(nameserver, cancellationToken).ConfigureAwait(false);
        _ = await socket.SendAsync(query, SocketFlags.None, cancellationToken).ConfigureAwait(false);

        byte[] buffer = new byte[ushort.MaxValue];
        int received = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        return buffer[..received];
    }

    private static async Task<byte[]> SendTcpAsync(IPEndPoint nameserver, byte[] query, CancellationToken cancellationToken)
    {
        using TcpClient client = new(nameserver.AddressFamily);
        await client.ConnectAsync(nameserver.Address, nameserver.Port, cancellationToken).ConfigureAwait(false);
        NetworkStream stream = client.GetStream();

        byte[] lengthPrefix = [(byte)(query.Length >> 8), (byte)query.Length];
        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(query, cancellationToken).ConfigureAwait(false);

        byte[] responseLengthBytes = new byte[2];
        await ReadExactAsync(stream, responseLengthBytes, cancellationToken).ConfigureAwait(false);
        int responseLength = (responseLengthBytes[0] << 8) | responseLengthBytes[1];

        byte[] response = new byte[responseLength];
        await ReadExactAsync(stream, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new FormatException("The TCP DNS response was truncated before its declared length.");
            }

            offset += read;
        }
    }

    // ---- Message build ------------------------------------------------------------------------

    private static byte[] BuildQuery(IReadOnlyList<string> questionLabels, out ushort queryId)
    {
        Span<byte> idBytes = stackalloc byte[2];
        RandomNumberGenerator.Fill(idBytes);
        queryId = (ushort)((idBytes[0] << 8) | idBytes[1]);

        using MemoryStream message = new();
        WriteUInt16(message, queryId);
        WriteUInt16(message, 0x0100); // RD=1 (recursion desired), standard query, all other bits 0.
        WriteUInt16(message, 1); // QDCOUNT
        WriteUInt16(message, 0); // ANCOUNT
        WriteUInt16(message, 0); // NSCOUNT
        WriteUInt16(message, 0); // ARCOUNT

        foreach (string label in questionLabels)
        {
            byte[] labelBytes = Encoding.ASCII.GetBytes(label);
            message.WriteByte((byte)labelBytes.Length);
            message.Write(labelBytes);
        }

        message.WriteByte(0); // Root label.
        WriteUInt16(message, DnsTypeSrv);
        WriteUInt16(message, DnsClassIn);

        return message.ToArray();
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    // ---- Message parse -------------------------------------------------------------------------

    /// <summary>
    /// Verifies the response's ID and <c>QR</c> bit before anything else is trusted (DSC-050), and
    /// reports the <c>TC</c> bit so the caller can perform the mandatory TCP retry.
    /// </summary>
    private static bool ValidateHeaderAndCheckTruncated(byte[] buffer, ushort expectedId)
    {
        RequireBytes(buffer, 0, 12);
        ushort id = ReadUInt16(buffer, 0);
        ushort flags = ReadUInt16(buffer, 2);

        if (id != expectedId || (flags & 0x8000) == 0)
        {
            throw new FormatException("The DNS response's ID or QR bit does not match the query.");
        }

        return (flags & 0x0200) != 0;
    }

    private static List<SrvRecord> ParseSrvAnswer(byte[] buffer, ushort expectedId, IReadOnlyList<string> questionLabels)
    {
        RequireBytes(buffer, 0, 12);
        ushort id = ReadUInt16(buffer, 0);
        ushort flags = ReadUInt16(buffer, 2);
        ushort questionCount = ReadUInt16(buffer, 4);
        ushort answerCount = ReadUInt16(buffer, 6);
        ushort authorityCount = ReadUInt16(buffer, 8);
        ushort additionalCount = ReadUInt16(buffer, 10);

        if (id != expectedId || (flags & 0x8000) == 0)
        {
            throw new FormatException("The DNS response's ID or QR bit does not match the query.");
        }

        if (questionCount != 1)
        {
            throw new FormatException("The DNS response echoed an unexpected number of questions.");
        }

        string expectedName = string.Join('.', questionLabels);

        int offset = 12;
        string echoedName = ReadName(buffer, offset, out offset);
        RequireBytes(buffer, offset, 4);
        ushort echoedType = ReadUInt16(buffer, offset);
        ushort echoedClass = ReadUInt16(buffer, offset + 2);
        offset += 4;

        if (echoedType != DnsTypeSrv
            || echoedClass != DnsClassIn
            || !string.Equals(echoedName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("The DNS response echoed a different question than the one that was sent.");
        }

        List<SrvRecord> matches = [];
        for (int index = 0; index < answerCount; index++)
        {
            offset = ReadAnswerResourceRecord(buffer, offset, expectedName, matches);
        }

        // Authority and additional records are skipped, never parsed for content (DSC-050): the
        // walk still has to know where each one ends, which is exactly the bound this shares with
        // an answer record.
        int trailingRecords = authorityCount + additionalCount;
        for (int index = 0; index < trailingRecords; index++)
        {
            offset = SkipResourceRecord(buffer, offset);
        }

        return matches;
    }

    /// <summary>
    /// Reads one answer resource record, adding it to <paramref name="matches"/> only when its
    /// name, type and class match the question (DSC-050). Always returns
    /// <c>rdataStart + RDLENGTH</c> as the next offset, regardless of how many bytes the SRV
    /// target's own (possibly compressed) name consumed — a lying <c>RDLENGTH</c> must not be able
    /// to desynchronise the walk.
    /// </summary>
    private static int ReadAnswerResourceRecord(byte[] buffer, int offset, string expectedName, List<SrvRecord> matches)
    {
        string name = ReadName(buffer, offset, out int afterName);
        RequireBytes(buffer, afterName, 10);
        ushort type = ReadUInt16(buffer, afterName);
        ushort recordClass = ReadUInt16(buffer, afterName + 2);
        ushort rdLength = ReadUInt16(buffer, afterName + 8);
        int rdataStart = afterName + 10;
        RequireBytes(buffer, rdataStart, rdLength);
        int nextOffset = rdataStart + rdLength;

        if (type == DnsTypeSrv
            && recordClass == DnsClassIn
            && string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            if (rdLength < 6)
            {
                throw new FormatException("An SRV record's RDATA is shorter than its fixed fields.");
            }

            ushort priority = ReadUInt16(buffer, rdataStart);
            ushort weight = ReadUInt16(buffer, rdataStart + 2);
            ushort port = ReadUInt16(buffer, rdataStart + 4);
            // The resuming offset from this call is intentionally discarded: the walk resumes at
            // `nextOffset` (offset + RDLENGTH) below, not at wherever this name parse ended.
            string target = ReadName(buffer, rdataStart + 6, out _);
            matches.Add(new SrvRecord(target, port, priority, weight));
        }

        return nextOffset;
    }

    private static int SkipResourceRecord(byte[] buffer, int offset)
    {
        _ = ReadName(buffer, offset, out int afterName);
        RequireBytes(buffer, afterName, 10);
        ushort rdLength = ReadUInt16(buffer, afterName + 8);
        int rdataStart = afterName + 10;
        RequireBytes(buffer, rdataStart, rdLength);
        return rdataStart + rdLength;
    }

    /// <summary>
    /// Decodes a (possibly compressed) name starting at <paramref name="offset"/>, and reports in
    /// <paramref name="nextOffset"/> where the <em>main</em> record stream resumes: immediately
    /// after the first compression pointer encountered, or immediately after the terminating root
    /// label when there was none. DSC-050's bounds, all enforced as each label is read rather than
    /// only at the end: a pointer must point strictly backwards of both the pointer itself and
    /// every pointer already followed for this name (which also caps hops at the message size),
    /// and is additionally capped at <see cref="MaxPointerHops"/>; each label is at most 63 bytes;
    /// the decoded name is at most 255 bytes.
    /// </summary>
    private static string ReadName(byte[] buffer, int offset, out int nextOffset)
    {
        List<string> labels = [];
        int position = offset;
        int decodedLength = 0;
        int hops = 0;
        bool jumped = false;
        int resumeOffset = -1;
        int mustBeLessThan = int.MaxValue;

        while (true)
        {
            RequireBytes(buffer, position, 1);
            byte lengthByte = buffer[position];

            if ((lengthByte & 0xC0) == 0xC0)
            {
                RequireBytes(buffer, position, 2);
                int target = ((lengthByte & 0x3F) << 8) | buffer[position + 1];

                if (target >= position || target >= mustBeLessThan)
                {
                    throw new FormatException("A DNS compression pointer does not point strictly backwards.");
                }

                hops++;
                if (hops > MaxPointerHops)
                {
                    throw new FormatException("A DNS name has too many compression pointer hops.");
                }

                if (!jumped)
                {
                    resumeOffset = position + 2;
                    jumped = true;
                }

                mustBeLessThan = target;
                position = target;
                continue;
            }

            if ((lengthByte & 0xC0) != 0)
            {
                throw new FormatException("A DNS name label uses a reserved length encoding.");
            }

            position++;
            int labelLength = lengthByte;
            if (labelLength == 0)
            {
                break;
            }

            if (labelLength > MaxLabelLength)
            {
                throw new FormatException("A DNS name label exceeds 63 bytes.");
            }

            RequireBytes(buffer, position, labelLength);

            decodedLength += labelLength + 1;
            if (decodedLength > MaxNameLength)
            {
                throw new FormatException("A decoded DNS name exceeds 255 bytes.");
            }

            labels.Add(Encoding.ASCII.GetString(buffer, position, labelLength));
            position += labelLength;
        }

        nextOffset = jumped ? resumeOffset : position;
        return string.Join('.', labels);
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
    }

    private static void RequireBytes(byte[] buffer, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new FormatException("The DNS message is truncated, or a length field overruns it.");
        }
    }
}
