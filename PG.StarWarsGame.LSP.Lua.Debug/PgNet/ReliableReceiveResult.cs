// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     What one received packet produced: datagrams to send back (acknowledgements, gap notices,
///     resends the peer asked for) and logical payloads that became deliverable, in order.
/// </summary>
public sealed class ReliableReceiveResult
{
    private readonly List<byte[]> _acks = [];
    private readonly List<byte[]> _nacks = [];
    private readonly List<byte[]> _resends = [];
    private readonly List<BitBuffer> _deliveries = [];

    public IReadOnlyList<byte[]> Acks => _acks;

    public IReadOnlyList<byte[]> Nacks => _nacks;

    public IReadOnlyList<byte[]> Resends => _resends;

    /// <summary>Complete logical payloads, reassembled where the peer split them.</summary>
    public IReadOnlyList<BitBuffer> Deliveries => _deliveries;

    /// <summary>Acknowledgements, gap notices and resends, in the order they should leave.</summary>
    public IEnumerable<byte[]> Outbound => _acks.Concat(_nacks).Concat(_resends);

    internal void AddAck(byte[] datagram)
    {
        _acks.Add(datagram);
    }

    internal void AddNack(byte[] datagram)
    {
        _nacks.Add(datagram);
    }

    internal void AddResend(byte[] datagram)
    {
        _resends.Add(datagram);
    }

    internal void AddDelivery(BitBuffer payload)
    {
        _deliveries.Add(payload);
    }
}
