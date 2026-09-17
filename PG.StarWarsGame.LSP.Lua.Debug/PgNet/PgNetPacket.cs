// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     One decoded datagram: the 22-bit packet id, the packet kind, the resend flag and the payload
///     bits that follow the header. For <see cref="PgNetPacketKind.Ack" /> and
///     <see cref="PgNetPacketKind.Nack" /> the payload is empty.
/// </summary>
public sealed class PgNetPacket
{
    public const uint MaxPacketId = 0x3FFFFF;

    public PgNetPacket(uint packetId, PgNetPacketKind kind, bool resend, BitBuffer payload)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(packetId, MaxPacketId);
        ArgumentNullException.ThrowIfNull(payload);

        PacketId = packetId;
        Kind = kind;
        Resend = resend;
        Payload = payload;
    }

    public uint PacketId { get; }

    public PgNetPacketKind Kind { get; }

    public bool Resend { get; }

    public BitBuffer Payload { get; }
}
