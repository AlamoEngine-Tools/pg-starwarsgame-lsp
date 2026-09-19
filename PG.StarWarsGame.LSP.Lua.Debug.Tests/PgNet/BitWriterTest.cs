// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Debug.PgNet;

namespace PG.StarWarsGame.LSP.Lua.Debug.Tests.PgNet;

public sealed class BitWriterTest
{
    [Fact]
    public void WriteBits_FieldsCrossByteBoundaries_PacksLeastSignificantBitFirst()
    {
        var writer = new BitWriter();

        writer.WriteBits(0b101, 3);
        writer.WriteBits(0b10010, 5);
        writer.WriteBits(0xABC, 12);

        Assert.Equal(Convert.FromHexString("95bc0a"), writer.ToArray());
        Assert.Equal(20, writer.BitCount);
    }

    [Fact]
    public void WriteString_TwoStrings_PrefixesEachWithOneLengthByte()
    {
        var writer = new BitWriter();

        writer.WriteString("Yo!");
        writer.WriteString("Spoot");

        Assert.Equal("Yo!Spoot"u8.ToArray(), writer.ToArray());
    }

    [Fact]
    public void WriteString_255Bytes_Throws()
    {
        var writer = new BitWriter();

        Assert.Throws<ArgumentException>(() => writer.WriteString(new string('x', 255)));
    }

    [Fact]
    public void WriteString_NonAscii_Throws()
    {
        var writer = new BitWriter();

        Assert.Throws<ArgumentException>(() => writer.WriteString("café"));
    }

    [Fact]
    public void WriteBits_ValueWiderThanField_Throws()
    {
        var writer = new BitWriter();

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteBits(0b100, 2));
    }

    [Fact]
    public void WriteBuffer_PartialByteBuffer_AppendsOnlyTheMeaningfulBits()
    {
        var inner = new BitWriter();
        inner.WriteBits(0b1, 1);
        var writer = new BitWriter();
        writer.WriteBits(0b11, 2);

        writer.WriteBuffer(inner.ToBuffer());

        Assert.Equal(3, writer.BitCount);
        Assert.Equal(new byte[] { 0b111 }, writer.ToArray());
    }

    [Fact]
    public void ToBuffer_ThenReader_RoundTripsEveryFieldKind()
    {
        var writer = new BitWriter();
        writer.WriteBool(true);
        writer.WriteByte(0xA5);
        writer.WriteUInt32(0xF000F000);
        writer.WriteInt32(-1);
        writer.WriteString("StarWarsI:7884");

        var reader = writer.ToBuffer().CreateReader();

        Assert.True(reader.ReadBool());
        Assert.Equal(0xA5, reader.ReadByte());
        Assert.Equal(0xF000F000, reader.ReadUInt32());
        Assert.Equal(-1, reader.ReadInt32());
        Assert.Equal("StarWarsI:7884", reader.ReadString());
        Assert.Equal(0, reader.RemainingBits);
    }
}