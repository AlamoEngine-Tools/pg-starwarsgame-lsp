// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     Reads fields from a bounded range of a least-significant-bit-first bit stream. Reading past
///     the range throws <see cref="PgNetProtocolException" />, never returns padding as data.
/// </summary>
public sealed class BitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _end;
    private int _position;

    public BitReader(ReadOnlyMemory<byte> data, int bitOffset = 0, int? bitCount = null)
    {
        var available = data.Length * 8;
        ArgumentOutOfRangeException.ThrowIfNegative(bitOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitOffset, available);
        var end = bitCount is null ? available : bitOffset + bitCount.Value;
        if (end < bitOffset || end > available)
            throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "Bit range is outside the data");

        _data = data;
        _position = bitOffset;
        _end = end;
    }

    public int RemainingBits => _end - _position;

    public uint ReadBits(int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 32);
        if (width > RemainingBits)
            throw new PgNetProtocolException(
                $"Unexpected end of bit stream: wanted {width} bits, {RemainingBits} left");

        var span = _data.Span;
        uint value = 0;
        for (var bit = 0; bit < width; bit++)
        {
            if ((span[_position / 8] >> (_position % 8) & 1) != 0)
                value |= 1u << bit;
            _position++;
        }

        return value;
    }

    public bool ReadBool()
    {
        return ReadBits(1) != 0;
    }

    public byte ReadByte()
    {
        return (byte)ReadBits(8);
    }

    public uint ReadUInt32()
    {
        return ReadBits(32);
    }

    public int ReadInt32()
    {
        return unchecked((int)ReadBits(32));
    }

    public byte[] ReadBytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var result = new byte[count];
        for (var i = 0; i < count; i++)
            result[i] = ReadByte();
        return result;
    }

    /// <summary>Reads a one-byte-length-prefixed ASCII string.</summary>
    public string ReadString()
    {
        var bytes = ReadBytes(ReadByte());
        if (bytes.Any(b => b > 0x7F))
            throw new PgNetProtocolException("Wire string is not ASCII");
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>Consumes an exact bit slice and returns it repacked from bit zero.</summary>
    public BitBuffer ReadBuffer(int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        if (bitCount > RemainingBits)
            throw new PgNetProtocolException(
                $"Unexpected end of bit stream: wanted {bitCount} bits, {RemainingBits} left");

        var writer = new BitWriter();
        for (var bit = 0; bit < bitCount; bit++)
            writer.WriteBool(ReadBool());
        return writer.ToBuffer();
    }
}
