// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using PG.Commons.Hashing;

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <inheritdoc />
public sealed class PgNetDatagramCodec : IPgNetDatagramCodec
{
    private const int CrcBytes = 4;
    private const int PacketIdBits = 22;
    private const int KindBits = 2;

    /// <summary>The CRC plus a body of at least one byte for the 25-bit header.</summary>
    private const int MinimumLength = CrcBytes + 4;

    private readonly ICrc32HashingService _hashing;

    public PgNetDatagramCodec(ICrc32HashingService hashing)
    {
        _hashing = hashing;
    }

    public byte[] Encode(PgNetPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var writer = new BitWriter();
        writer.WriteBits(packet.PacketId, PacketIdBits);
        writer.WriteBits((uint)packet.Kind, KindBits);
        writer.WriteBool(packet.Resend);
        writer.WriteBuffer(packet.Payload);

        var body = writer.ToArray();
        var datagram = new byte[CrcBytes + body.Length];
        body.CopyTo(datagram, CrcBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram, (uint)_hashing.GetCrc32(body));
        return datagram;
    }

    public PgNetPacket Decode(ReadOnlySpan<byte> datagram, bool validateCrc = true)
    {
        if (datagram.Length < MinimumLength)
            throw new PgNetProtocolException($"Datagram of {datagram.Length} bytes is shorter than a header");

        var body = datagram[CrcBytes..].ToArray();
        var stored = BinaryPrimitives.ReadUInt32LittleEndian(datagram);
        var actual = (uint)_hashing.GetCrc32(body);
        if (validateCrc && stored != actual)
            throw new PgNetCrcException(stored, actual);

        var reader = new BitReader(body);
        var packetId = reader.ReadBits(PacketIdBits);
        var kind = (PgNetPacketKind)reader.ReadBits(KindBits);
        var resend = reader.ReadBool();
        var payload = kind is PgNetPacketKind.Ack or PgNetPacketKind.Nack
            ? BitBuffer.Empty
            : reader.ReadBuffer(reader.RemainingBits);
        return new PgNetPacket(packetId, kind, resend, payload);
    }

    public byte[] EncodeAck(uint packetId)
    {
        return Encode(new PgNetPacket(packetId, PgNetPacketKind.Ack, false, BitBuffer.Empty));
    }

    public byte[] EncodeNack(uint packetId)
    {
        return Encode(new PgNetPacket(packetId, PgNetPacketKind.Nack, false, BitBuffer.Empty));
    }
}
