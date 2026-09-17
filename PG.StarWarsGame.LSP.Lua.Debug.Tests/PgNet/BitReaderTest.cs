// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.PgNet;

public sealed class BitReaderTest
{
    [Fact]
    public void ReadBits_KnownPackedBytes_UnpacksLeastSignificantBitFirst()
    {
        var reader = new BitReader(Convert.FromHexString("95bc0a"));

        Assert.Equal(0b101u, reader.ReadBits(3));
        Assert.Equal(0b10010u, reader.ReadBits(5));
        Assert.Equal(0xABCu, reader.ReadBits(12));
        Assert.Equal(0u, reader.ReadBits(4));
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void ReadString_LengthPrefixedAscii_ReturnsBothStrings()
    {
        var reader = new BitReader("Yo!Spoot"u8.ToArray());

        Assert.Equal("Yo!", reader.ReadString());
        Assert.Equal("Spoot", reader.ReadString());
    }

    [Fact]
    public void ReadBits_PastTheEnd_ThrowsProtocolException()
    {
        var reader = new BitReader(new byte[] { 0xFF });

        reader.ReadBits(4);

        Assert.Throws<PgNetProtocolException>(() => reader.ReadBits(5));
    }

    [Fact]
    public void ReadString_NonAsciiByte_ThrowsProtocolException()
    {
        var reader = new BitReader(new byte[] { 0x01, 0xE9 });

        Assert.Throws<PgNetProtocolException>(() => reader.ReadString());
    }

    [Fact]
    public void Constructor_BitOffset_SkipsLeadingBits()
    {
        // 0x95bc0a with the first 3 bits skipped leaves 0b10010 as the next 5-bit field.
        var reader = new BitReader(Convert.FromHexString("95bc0a"), bitOffset: 3);

        Assert.Equal(0b10010u, reader.ReadBits(5));
        Assert.Equal(16, reader.RemainingBits);
    }

    [Fact]
    public void Constructor_BitCount_BoundsTheReadableRange()
    {
        var reader = new BitReader(Convert.FromHexString("95bc0a"), bitOffset: 0, bitCount: 8);

        reader.ReadBits(8);

        Assert.Equal(0, reader.RemainingBits);
        Assert.Throws<PgNetProtocolException>(() => reader.ReadBool());
    }

    [Fact]
    public void ReadBuffer_MidStream_ReturnsExactBitSliceIncludingPartialByte()
    {
        var reader = new BitReader(Convert.FromHexString("95bc0a"));
        reader.ReadBits(3);

        var slice = reader.ReadBuffer(5);

        Assert.Equal(5, slice.BitCount);
        Assert.Equal(0b10010u, slice.CreateReader().ReadBits(5));
        Assert.Equal(16, reader.RemainingBits);
    }
}