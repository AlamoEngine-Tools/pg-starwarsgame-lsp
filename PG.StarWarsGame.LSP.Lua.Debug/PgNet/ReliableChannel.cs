// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <inheritdoc />
public sealed class ReliableChannel : IReliableChannel
{
    /// <summary>A payload of this many bytes or more travels as a descriptor plus chunks.</summary>
    public const int DirectSendThresholdBytes = 0x495;

    /// <summary>Each chunk carries this many payload bytes after its 6-byte marker.</summary>
    public const int ChunkDataBytes = DirectSendThresholdBytes - 6;

    /// <summary>Marks a descriptor or chunk of a split payload.</summary>
    public const uint SequencerMagic = 0x49960201;

    private const byte DescriptorSubtype = 0;
    private const byte ChunkSubtype = 1;
    private const int MarkerBits = 32 + 8;
    private const long IdModulus = PgNetPacket.MaxPacketId + 1L;

    private readonly IPgNetDatagramCodec _codec;
    private readonly TimeProvider _time;
    private readonly List<PendingPacket> _pending = [];
    private readonly Dictionary<uint, BitBuffer> _queued = [];
    private LargeSequence? _largeSequence;

    public ReliableChannel(IPgNetDatagramCodec codec, TimeProvider time) : this(codec, time, 0, 0)
    {
    }

    internal ReliableChannel(IPgNetDatagramCodec codec, TimeProvider time, uint nextSendId, uint expectedReceiveId)
    {
        _codec = codec;
        _time = time;
        NextSendId = nextSendId;
        ExpectedReceiveId = expectedReceiveId;
    }

    public TimeSpan ResendInterval { get; set; } = TimeSpan.FromSeconds(2);

    public uint NextSendId { get; private set; }

    public uint ExpectedReceiveId { get; private set; }

    public int PendingCount => _pending.Count;

    public IReadOnlyList<byte[]> MakeGuaranteed(BitBuffer payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Span.Length < DirectSendThresholdBytes)
            return [MakeOne(payload)];

        var bytes = payload.ToArray();
        var chunkCount = (bytes.Length + ChunkDataBytes - 1) / ChunkDataBytes;
        var lastChunkId = (uint)((NextSendId + chunkCount) % IdModulus);

        var descriptor = new BitWriter();
        descriptor.WriteUInt32(SequencerMagic);
        descriptor.WriteByte(DescriptorSubtype);
        descriptor.WriteUInt32(lastChunkId);
        descriptor.WriteUInt32((uint)bytes.Length);
        descriptor.WriteUInt32((uint)payload.BitCount);

        var datagrams = new List<byte[]>(1 + chunkCount) { MakeOne(descriptor.ToBuffer()) };
        for (var offset = 0; offset < bytes.Length; offset += ChunkDataBytes)
        {
            var chunk = new BitWriter();
            chunk.WriteUInt32(SequencerMagic);
            chunk.WriteByte(ChunkSubtype);
            chunk.WriteBytes(bytes.AsSpan(offset, Math.Min(ChunkDataBytes, bytes.Length - offset)));
            datagrams.Add(MakeOne(chunk.ToBuffer()));
        }

        return datagrams;
    }

    public IReadOnlyList<byte[]> DueResends()
    {
        var due = new List<byte[]>();
        foreach (var pending in _pending)
        {
            if (_time.GetElapsedTime(pending.SentAt) < ResendInterval)
                continue;
            pending.SentAt = _time.GetTimestamp();
            pending.Attempts++;
            due.Add(pending.Datagram);
        }

        return due;
    }

    public ReliableReceiveResult Process(PgNetPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var result = new ReliableReceiveResult();

        switch (packet.Kind)
        {
            case PgNetPacketKind.Ack:
                _pending.RemoveAll(p => p.PacketId == packet.PacketId);
                return result;

            case PgNetPacketKind.Nack:
                var wanted = _pending.Find(p => p.PacketId == packet.PacketId);
                if (wanted is not null)
                {
                    wanted.SentAt = _time.GetTimestamp();
                    wanted.Attempts++;
                    result.AddResend(wanted.Datagram);
                }

                return result;

            case PgNetPacketKind.NonGuaranteed:
                return result;

            case PgNetPacketKind.Guaranteed:
                break;

            default:
                throw new PgNetProtocolException($"Unknown packet kind {(int)packet.Kind}");
        }

        result.AddAck(_codec.EncodeAck(packet.PacketId));
        var distance = (packet.PacketId - ExpectedReceiveId + IdModulus) % IdModulus;
        if (distance > PgNetPacket.MaxPacketId / 2)
            return result; // Already delivered: the acknowledgement was lost, nothing else to do.

        if (distance != 0)
        {
            _queued.TryAdd(packet.PacketId, packet.Payload);
            result.AddNack(_codec.EncodeNack(ExpectedReceiveId));
            return result;
        }

        Deliver(packet.PacketId, packet.Payload, result);
        while (_queued.Remove(ExpectedReceiveId, out var queued))
            Deliver(ExpectedReceiveId, queued, result);
        return result;
    }

    private byte[] MakeOne(BitBuffer payload)
    {
        var packetId = NextSendId;
        NextSendId = (uint)((NextSendId + 1) % IdModulus);
        var datagram = _codec.Encode(new PgNetPacket(packetId, PgNetPacketKind.Guaranteed, false, payload));
        _pending.Add(new PendingPacket(packetId, datagram, _time.GetTimestamp()));
        return datagram;
    }

    private void Deliver(uint packetId, BitBuffer payload, ReliableReceiveResult result)
    {
        ExpectedReceiveId = (uint)((ExpectedReceiveId + 1) % IdModulus);
        var logical = Reassemble(packetId, payload);
        if (logical is not null)
            result.AddDelivery(logical);
    }

    /// <summary>
    ///     Consumes a descriptor or chunk of a split payload, returning the whole payload once the
    ///     last chunk lands; any other payload is returned unchanged.
    /// </summary>
    private BitBuffer? Reassemble(uint packetId, BitBuffer payload)
    {
        if (payload.BitCount < MarkerBits)
            return payload;

        var reader = payload.CreateReader();
        var magic = reader.ReadUInt32();
        var subtype = reader.ReadByte();
        if (magic != SequencerMagic || subtype is not (DescriptorSubtype or ChunkSubtype))
            return payload;

        if (subtype == DescriptorSubtype)
        {
            var lastChunkId = reader.ReadUInt32();
            var byteCount = reader.ReadUInt32();
            var bitCount = reader.ReadUInt32();
            if (byteCount != (bitCount + 7) / 8)
                throw new PgNetProtocolException(
                    $"Split-payload descriptor announces {byteCount} bytes for {bitCount} bits");
            _largeSequence = new LargeSequence(lastChunkId, (int)byteCount, (int)bitCount);
            return null;
        }

        if (_largeSequence is null)
            throw new PgNetProtocolException("Split-payload chunk arrived before its descriptor");

        // An inbound payload starts 25 header bits into its datagram, so it always ends with 7
        // padding bits; strip them and the chunk data is whole bytes again.
        var chunkBits = reader.RemainingBits - 7;
        if (chunkBits < 0 || chunkBits % 8 != 0)
            throw new PgNetProtocolException($"Split-payload chunk of {reader.RemainingBits} bits is not byte-aligned");
        _largeSequence.Chunks.Add(reader.ReadBuffer(chunkBits));
        if (packetId != _largeSequence.LastChunkId)
            return null;

        var assembled = new BitWriter();
        foreach (var chunk in _largeSequence.Chunks)
            assembled.WriteBuffer(chunk);
        if (assembled.BitCount / 8 != _largeSequence.ByteCount)
            throw new PgNetProtocolException(
                $"Split-payload chunks total {assembled.BitCount / 8} bytes, descriptor announced {_largeSequence.ByteCount}");

        var whole = assembled.ToBuffer().CreateReader().ReadBuffer(_largeSequence.BitCount);
        _largeSequence = null;
        return whole;
    }

    private sealed class PendingPacket(uint packetId, byte[] datagram, long sentAt)
    {
        public uint PacketId { get; } = packetId;

        public byte[] Datagram { get; } = datagram;

        public long SentAt { get; set; } = sentAt;

        public int Attempts { get; set; } = 1;
    }

    private sealed class LargeSequence(uint lastChunkId, int byteCount, int bitCount)
    {
        public uint LastChunkId { get; } = lastChunkId;

        public int ByteCount { get; } = byteCount;

        public int BitCount { get; } = bitCount;

        public List<BitBuffer> Chunks { get; } = [];
    }
}