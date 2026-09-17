// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     The reliable layer over datagrams, one instance per connection and per direction pair.
///     Outbound: assigns consecutive packet ids, retains every guaranteed datagram until its
///     acknowledgement, resends after <see cref="ResendInterval" />, and splits a payload of
///     0x495 bytes or more into a descriptor plus chunks. Inbound: acknowledges every guaranteed
///     packet, asks for the next expected id when a gap appears, suppresses duplicates, delivers in
///     id order, and reassembles split payloads. No sockets: the caller sends what this returns.
/// </summary>
public interface IReliableChannel
{
    TimeSpan ResendInterval { get; set; }

    /// <summary>The id the next guaranteed datagram will carry.</summary>
    uint NextSendId { get; }

    /// <summary>The id the next in-order inbound guaranteed packet must carry.</summary>
    uint ExpectedReceiveId { get; }

    /// <summary>Guaranteed datagrams sent and not yet acknowledged.</summary>
    int PendingCount { get; }

    /// <summary>
    ///     Encodes a payload as one guaranteed datagram, or as a descriptor followed by chunks
    ///     when it is too large for one, each with its own consecutive id. All are retained.
    /// </summary>
    IReadOnlyList<byte[]> MakeGuaranteed(BitBuffer payload);

    /// <summary>Retained datagrams whose resend interval has elapsed; their timers restart.</summary>
    IReadOnlyList<byte[]> DueResends();

    /// <summary>
    ///     Feeds one decoded inbound packet through the layer.
    /// </summary>
    /// <exception cref="PgNetProtocolException">A split payload arrived out of shape.</exception>
    ReliableReceiveResult Process(PgNetPacket packet);
}
