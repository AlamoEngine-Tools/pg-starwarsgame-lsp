// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.PgNet;

/// <summary>
///     An immutable run of bits packed least-significant-bit first. The byte array holds exactly
///     <c>ceil(BitCount / 8)</c> bytes; bits past <see cref="BitCount" /> in the last byte are zero
///     padding and carry no meaning.
/// </summary>
public sealed class BitBuffer
{
    private readonly byte[] _data;

    public BitBuffer(byte[] data, int bitCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        if (data.Length != (bitCount + 7) / 8)
            throw new ArgumentException(
                $"A {bitCount}-bit buffer needs {(bitCount + 7) / 8} bytes, got {data.Length}", nameof(data));

        _data = data;
        BitCount = bitCount;
    }

    public static BitBuffer Empty { get; } = new([], 0);

    public int BitCount { get; }

    public ReadOnlySpan<byte> Span => _data;

    public byte[] ToArray()
    {
        return (byte[])_data.Clone();
    }

    public BitReader CreateReader()
    {
        return new BitReader(_data, 0, BitCount);
    }
}
