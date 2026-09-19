// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.PgNet;

public sealed class PgNetDatagramCodecTest
{
    private readonly IPgNetDatagramCodec _codec = TestServices.Get<IPgNetDatagramCodec>();

    private static BitBuffer InnerHello()
    {
        // 4-bit inner magic 0xd followed by a 32-bit message id of 1.
        var writer = new BitWriter();
        writer.WriteBits(0xD, 4);
        writer.WriteUInt32(1);
        return writer.ToBuffer();
    }

    [Fact]
    public void EncodeAck_PacketId1_MatchesKnownEightByteVector()
    {
        var ack = _codec.EncodeAck(1);

        Assert.Equal(Convert.FromHexString("32207ba201008000"), ack);
        Assert.Equal(8, ack.Length);
    }

    [Fact]
    public void Decode_KnownAck_HasIdKindAndEmptyPayload()
    {
        var packet = _codec.Decode(Convert.FromHexString("32207ba201008000"));

        Assert.Equal(1u, packet.PacketId);
        Assert.Equal(PgNetPacketKind.Ack, packet.Kind);
        Assert.False(packet.Resend);
        Assert.Equal(0, packet.Payload.BitCount);
    }

    [Fact]
    public void Encode_GuaranteedWithInnerPayload_RoundTripsThroughDecode()
    {
        var hello = InnerHello();
        var datagram = _codec.Encode(new PgNetPacket(0, PgNetPacketKind.Guaranteed, false, hello));

        var packet = _codec.Decode(datagram);

        Assert.Equal(0u, packet.PacketId);
        Assert.Equal(PgNetPacketKind.Guaranteed, packet.Kind);
        // The decoded payload keeps the padding bits up to the byte boundary; the bytes are equal
        // because padding is zero, and the meaningful prefix reads back identically.
        Assert.Equal(hello.ToArray(), packet.Payload.ToArray());
        Assert.True(packet.Payload.BitCount >= hello.BitCount);
        Assert.Equal(hello.ToArray(), packet.Payload.CreateReader().ReadBuffer(hello.BitCount).ToArray());
    }

    [Fact]
    public void Encode_ResendFlag_SurvivesRoundTrip()
    {
        var datagram = _codec.Encode(new PgNetPacket(7, PgNetPacketKind.Guaranteed, true, InnerHello()));

        Assert.True(_codec.Decode(datagram).Resend);
    }

    [Fact]
    public void Decode_CorruptedByte_ThrowsCrcException()
    {
        var datagram = _codec.EncodeAck(1);
        datagram[^1] ^= 0x01;

        Assert.Throws<PgNetCrcException>(() => _codec.Decode(datagram));
    }

    [Fact]
    public void Decode_CorruptedByteWithValidationOff_StillDecodes()
    {
        var datagram = _codec.EncodeAck(1);
        datagram[0] ^= 0x01;

        Assert.Equal(1u, _codec.Decode(datagram, validateCrc: false).PacketId);
    }

    [Fact]
    public void Decode_ShorterThanHeader_ThrowsProtocolException()
    {
        Assert.Throws<PgNetProtocolException>(() => _codec.Decode(new byte[7]));
    }

    [Fact]
    public void PgNetPacket_IdAboveTwentyTwoBits_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new PgNetPacket(PgNetPacket.MaxPacketId + 1, PgNetPacketKind.Ack, false, BitBuffer.Empty));
    }

    [Fact]
    public void EncodeNack_ThenDecode_IsNackWithRequestedId()
    {
        var packet = _codec.Decode(_codec.EncodeNack(0x3FFFFF));

        Assert.Equal(PgNetPacketKind.Nack, packet.Kind);
        Assert.Equal(0x3FFFFFu, packet.PacketId);
    }
}