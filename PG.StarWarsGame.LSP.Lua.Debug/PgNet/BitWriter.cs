// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     Appends fields to a least-significant-bit-first bit stream. Fields may cross byte
///     boundaries; bit zero of an integer is written first. Strings are one length byte followed by
///     raw ASCII, so a string of 255 bytes or more cannot be written.
/// </summary>
public sealed class BitWriter
{
    /// <summary>The length prefix is one byte and 255 is reserved by the wire format.</summary>
    public const int MaxStringBytes = 254;

    private readonly List<byte> _bytes = [];

    public int BitCount { get; private set; }

    public void WriteBits(uint value, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 32);
        if (width < 32 && value >> width != 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value does not fit in {width} bits");

        for (var bit = 0; bit < width; bit++)
            AppendBit(((value >> bit) & 1) != 0);
    }

    public void WriteBool(bool value)
    {
        AppendBit(value);
    }

    public void WriteByte(byte value)
    {
        WriteBits(value, 8);
    }

    public void WriteUInt32(uint value)
    {
        WriteBits(value, 32);
    }

    public void WriteInt32(int value)
    {
        WriteBits(unchecked((uint)value), 32);
    }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            WriteByte(b);
    }

    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(c => c > 0x7F))
            throw new ArgumentException("Wire strings are ASCII only", nameof(value));
        if (value.Length > MaxStringBytes)
            throw new ArgumentException(
                $"Wire strings are at most {MaxStringBytes} bytes, got {value.Length}", nameof(value));

        WriteByte((byte)value.Length);
        WriteBytes(Encoding.ASCII.GetBytes(value));
    }

    /// <summary>Appends exactly the meaningful bits of another buffer, never its padding.</summary>
    public void WriteBuffer(BitBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var span = buffer.Span;
        for (var bit = 0; bit < buffer.BitCount; bit++)
            AppendBit((span[bit / 8] >> (bit % 8) & 1) != 0);
    }

    public byte[] ToArray()
    {
        return _bytes.ToArray();
    }

    public BitBuffer ToBuffer()
    {
        return new BitBuffer(ToArray(), BitCount);
    }

    private void AppendBit(bool set)
    {
        if (BitCount % 8 == 0)
            _bytes.Add(0);
        if (set)
            _bytes[BitCount / 8] |= (byte)(1 << (BitCount % 8));
        BitCount++;
    }
}
