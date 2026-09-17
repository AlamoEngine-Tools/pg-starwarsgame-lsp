// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.PgNet;

public sealed class ReliableChannelTest
{
    private readonly FakeTimeProvider _time = new();
    private readonly IPgNetDatagramCodec _codec;
    private readonly IReliableChannel _channel;

    public ReliableChannelTest()
    {
        var provider = TestServices.Build(services => services.AddSingleton<TimeProvider>(_time));
        _codec = provider.GetRequiredService<IPgNetDatagramCodec>();
        _channel = provider.GetRequiredService<IReliableChannel>();
    }

    // -- helpers ------------------------------------------------------------------------------

    /// <summary>A small inner payload distinguishable by its message id.</summary>
    private static BitBuffer Payload(uint messageId)
    {
        var writer = new BitWriter();
        writer.WriteBits(0xD, 4);
        writer.WriteUInt32(messageId);
        return writer.ToBuffer();
    }

    /// <summary>A payload of <paramref name="byteCount" /> pseudo-random bytes, 3 bits short of full.</summary>
    private static BitBuffer LargePayload(int byteCount)
    {
        var bytes = new byte[byteCount];
        new Random(42).NextBytes(bytes);
        bytes[^1] &= 0x1F;
        return new BitBuffer(bytes, byteCount * 8 - 3);
    }

    /// <summary>An inbound packet as the wire delivers it: encoded, then decoded, padding included.</summary>
    private PgNetPacket Inbound(uint packetId, BitBuffer payload, PgNetPacketKind kind = PgNetPacketKind.Guaranteed)
    {
        return _codec.Decode(_codec.Encode(new PgNetPacket(packetId, kind, false, payload)));
    }

    private PgNetPacket Ack(uint packetId)
    {
        return _codec.Decode(_codec.EncodeAck(packetId));
    }

    private PgNetPacket Nack(uint packetId)
    {
        return _codec.Decode(_codec.EncodeNack(packetId));
    }

    private static byte[] MeaningfulBytes(BitBuffer delivered, int bitCount)
    {
        return delivered.CreateReader().ReadBuffer(bitCount).ToArray();
    }

    // -- outbound ids and retention ------------------------------------------------------------

    [Fact]
    public void MakeGuaranteed_ThreeSmallPayloads_AssignsConsecutiveIdsAndRetainsThem()
    {
        var first = _channel.MakeGuaranteed(Payload(1)).Single();
        var second = _channel.MakeGuaranteed(Payload(2)).Single();
        var third = _channel.MakeGuaranteed(Payload(3)).Single();

        Assert.Equal(0u, _codec.Decode(first).PacketId);
        Assert.Equal(1u, _codec.Decode(second).PacketId);
        Assert.Equal(2u, _codec.Decode(third).PacketId);
        Assert.Equal(PgNetPacketKind.Guaranteed, _codec.Decode(first).Kind);
        Assert.Equal(3u, _channel.NextSendId);
        Assert.Equal(3, _channel.PendingCount);
    }

    [Fact]
    public void Process_AckForPending_ReleasesOnlyThatPacket()
    {
        _channel.MakeGuaranteed(Payload(1));
        _channel.MakeGuaranteed(Payload(2));

        var result = _channel.Process(Ack(1));

        Assert.Equal(1, _channel.PendingCount);
        Assert.Empty(result.Outbound);
        Assert.Empty(result.Deliveries);
    }

    [Fact]
    public void Process_AckForUnknownId_ChangesNothing()
    {
        _channel.MakeGuaranteed(Payload(1));

        _channel.Process(Ack(9));

        Assert.Equal(1, _channel.PendingCount);
    }

    [Fact]
    public void Process_NackForPending_ReturnsThatDatagramAsResend()
    {
        var sent = _channel.MakeGuaranteed(Payload(1)).Single();

        var result = _channel.Process(Nack(0));

        Assert.Equal([sent], result.Resends);
        Assert.Empty(result.Acks);
        Assert.Equal(1, _channel.PendingCount);
    }

    [Fact]
    public void Process_NackForUnknownId_ReturnsNothing()
    {
        Assert.Empty(_channel.Process(Nack(5)).Outbound);
    }

    // -- inbound ordering ---------------------------------------------------------------------

    [Fact]
    public void Process_GuaranteedInOrder_AcksAndDelivers()
    {
        var payload = Payload(7);

        var result = _channel.Process(Inbound(0, payload));

        Assert.Equal([_codec.EncodeAck(0)], result.Acks);
        Assert.Empty(result.Nacks);
        Assert.Equal(payload.ToArray(), MeaningfulBytes(result.Deliveries.Single(), payload.BitCount));
        Assert.Equal(1u, _channel.ExpectedReceiveId);
    }

    [Fact]
    public void Process_GuaranteedOutOfOrder_NacksTheGapThenDeliversInOrder()
    {
        var early = _channel.Process(Inbound(1, Payload(2)));

        Assert.Equal([_codec.EncodeAck(1)], early.Acks);
        Assert.Equal([_codec.EncodeNack(0)], early.Nacks);
        Assert.Empty(early.Deliveries);
        Assert.Equal(0u, _channel.ExpectedReceiveId);

        var late = _channel.Process(Inbound(0, Payload(1)));

        Assert.Equal(2, late.Deliveries.Count);
        Assert.Equal(Payload(1).ToArray(), MeaningfulBytes(late.Deliveries[0], Payload(1).BitCount));
        Assert.Equal(Payload(2).ToArray(), MeaningfulBytes(late.Deliveries[1], Payload(2).BitCount));
        Assert.Equal(2u, _channel.ExpectedReceiveId);
    }

    [Fact]
    public void Process_DuplicateOfDeliveredPacket_AcksAgainWithoutDelivering()
    {
        _channel.Process(Inbound(0, Payload(1)));

        var duplicate = _channel.Process(Inbound(0, Payload(1)));

        Assert.Equal([_codec.EncodeAck(0)], duplicate.Acks);
        Assert.Empty(duplicate.Deliveries);
        Assert.Equal(1u, _channel.ExpectedReceiveId);
    }

    [Fact]
    public void Process_SameFutureIdTwice_QueuesOnceAndDeliversOnce()
    {
        _channel.Process(Inbound(1, Payload(2)));
        _channel.Process(Inbound(1, Payload(2)));

        var late = _channel.Process(Inbound(0, Payload(1)));

        Assert.Equal(2, late.Deliveries.Count);
    }

    [Fact]
    public void Process_NonGuaranteed_IsIgnored()
    {
        var result = _channel.Process(Inbound(0, Payload(1), PgNetPacketKind.NonGuaranteed));

        Assert.Empty(result.Outbound);
        Assert.Empty(result.Deliveries);
        Assert.Equal(0u, _channel.ExpectedReceiveId);
    }

    // -- resend timer -------------------------------------------------------------------------

    [Fact]
    public void DueResends_BeforeInterval_IsEmpty()
    {
        _channel.MakeGuaranteed(Payload(1));
        _time.Advance(TimeSpan.FromSeconds(1.9));

        Assert.Empty(_channel.DueResends());
    }

    [Fact]
    public void DueResends_AfterInterval_ReturnsTheDatagramOncePerInterval()
    {
        var sent = _channel.MakeGuaranteed(Payload(1)).Single();

        _time.Advance(TimeSpan.FromSeconds(2));
        var first = _channel.DueResends();
        var immediately = _channel.DueResends();
        _time.Advance(TimeSpan.FromSeconds(2));
        var second = _channel.DueResends();

        Assert.Equal([sent], first);
        Assert.Empty(immediately);
        Assert.Equal([sent], second);
    }

    [Fact]
    public void DueResends_AcknowledgedPacket_IsNeverResent()
    {
        _channel.MakeGuaranteed(Payload(1));
        _channel.Process(Ack(0));
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Empty(_channel.DueResends());
    }

    [Fact]
    public void ResendInterval_Changed_IsHonoured()
    {
        _channel.ResendInterval = TimeSpan.FromMilliseconds(500);
        _channel.MakeGuaranteed(Payload(1));
        _time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Single(_channel.DueResends());
    }

    // -- split payloads -----------------------------------------------------------------------

    [Fact]
    public void MakeGuaranteed_LargePayload_EmitsDescriptorThenChunksWithConsecutiveIds()
    {
        var payload = LargePayload(3000);

        var datagrams = _channel.MakeGuaranteed(payload);

        // 3000 bytes in chunks of 0x495 - 6 = 1167 bytes is three chunks plus the descriptor.
        Assert.Equal(4, datagrams.Count);
        Assert.Equal([0u, 1u, 2u, 3u], datagrams.Select(d => _codec.Decode(d).PacketId));
        Assert.Equal(4, _channel.PendingCount);

        var descriptor = _codec.Decode(datagrams[0]).Payload.CreateReader();
        Assert.Equal(ReliableChannel.SequencerMagic, descriptor.ReadUInt32());
        Assert.Equal(0, descriptor.ReadByte());
        Assert.Equal(3u, descriptor.ReadUInt32());
        Assert.Equal(3000u, descriptor.ReadUInt32());
        Assert.Equal((uint)payload.BitCount, descriptor.ReadUInt32());

        var chunk = _codec.Decode(datagrams[1]).Payload.CreateReader();
        Assert.Equal(ReliableChannel.SequencerMagic, chunk.ReadUInt32());
        Assert.Equal(1, chunk.ReadByte());
    }

    [Fact]
    public void MakeGuaranteed_PayloadJustBelowThreshold_IsOneDatagram()
    {
        var payload = new BitBuffer(new byte[ReliableChannel.DirectSendThresholdBytes - 1],
            (ReliableChannel.DirectSendThresholdBytes - 1) * 8);

        Assert.Single(_channel.MakeGuaranteed(payload));
    }

    [Fact]
    public void Process_SplitPayloadFromPeer_ReassemblesBitExactlyAfterLastChunk()
    {
        var payload = LargePayload(3000);
        var sender = TestServices.Build().GetRequiredService<IReliableChannel>();
        var datagrams = sender.MakeGuaranteed(payload);

        var results = datagrams.Select(d => _channel.Process(_codec.Decode(d))).ToList();

        Assert.All(results.Take(3), r => Assert.Empty(r.Deliveries));
        Assert.Equal(4, results.Sum(r => r.Acks.Count));
        var delivered = results[3].Deliveries.Single();
        Assert.Equal(payload.BitCount, delivered.BitCount);
        Assert.Equal(payload.ToArray(), delivered.ToArray());
        Assert.Equal(4u, _channel.ExpectedReceiveId);
    }

    [Fact]
    public void Process_SplitPayloadArrivingOutOfOrder_StillReassembles()
    {
        var payload = LargePayload(3000);
        var sender = TestServices.Build().GetRequiredService<IReliableChannel>();
        var packets = sender.MakeGuaranteed(payload).Select(d => _codec.Decode(d)).ToList();

        _channel.Process(packets[2]);
        _channel.Process(packets[0]);
        _channel.Process(packets[3]);
        var last = _channel.Process(packets[1]);

        Assert.Equal(payload.ToArray(), last.Deliveries.Single().ToArray());
    }

    [Fact]
    public void Process_ChunkBeforeDescriptor_Throws()
    {
        var chunk = new BitWriter();
        chunk.WriteUInt32(ReliableChannel.SequencerMagic);
        chunk.WriteByte(1);
        chunk.WriteBytes(new byte[8]);

        Assert.Throws<PgNetProtocolException>(() => _channel.Process(Inbound(0, chunk.ToBuffer())));
    }

    [Fact]
    public void Process_DescriptorWithMismatchedSizes_Throws()
    {
        var descriptor = new BitWriter();
        descriptor.WriteUInt32(ReliableChannel.SequencerMagic);
        descriptor.WriteByte(0);
        descriptor.WriteUInt32(1);
        descriptor.WriteUInt32(10);
        descriptor.WriteUInt32(8 * 8);

        Assert.Throws<PgNetProtocolException>(() => _channel.Process(Inbound(0, descriptor.ToBuffer())));
    }

    [Fact]
    public void Process_PayloadThatMerelyStartsLikeAMarker_IsDeliveredUnchanged()
    {
        // Same magic, unknown subtype: an ordinary payload that happens to share the prefix.
        var writer = new BitWriter();
        writer.WriteUInt32(ReliableChannel.SequencerMagic);
        writer.WriteByte(7);
        writer.WriteUInt32(99);
        var payload = writer.ToBuffer();

        var result = _channel.Process(Inbound(0, payload));

        Assert.Equal(payload.ToArray(), MeaningfulBytes(result.Deliveries.Single(), payload.BitCount));
    }

    // -- id wrap-around -----------------------------------------------------------------------

    [Fact]
    public void MakeGuaranteed_AtMaxId_WrapsToZero()
    {
        var channel = new ReliableChannel(_codec, _time, PgNetPacket.MaxPacketId, 0);

        var last = channel.MakeGuaranteed(Payload(1)).Single();
        var wrapped = channel.MakeGuaranteed(Payload(2)).Single();

        Assert.Equal(PgNetPacket.MaxPacketId, _codec.Decode(last).PacketId);
        Assert.Equal(0u, _codec.Decode(wrapped).PacketId);
        Assert.Equal(1u, channel.NextSendId);
    }

    [Fact]
    public void Process_ExpectedIdAtMax_DeliversMaxThenZeroInOrder()
    {
        var channel = new ReliableChannel(_codec, _time, 0, PgNetPacket.MaxPacketId);

        var first = channel.Process(Inbound(PgNetPacket.MaxPacketId, Payload(1)));
        var second = channel.Process(Inbound(0, Payload(2)));

        Assert.Single(first.Deliveries);
        Assert.Single(second.Deliveries);
        Assert.Empty(second.Nacks);
        Assert.Equal(1u, channel.ExpectedReceiveId);
    }
}