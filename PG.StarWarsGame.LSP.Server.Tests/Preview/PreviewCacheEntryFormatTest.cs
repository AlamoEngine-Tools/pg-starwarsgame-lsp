// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The preview cache entry's file (#436): a header that says how long it is, a section table, and
///     the sections - laid out so a later writer can add to it without an older reader tripping.
/// </summary>
public sealed class PreviewCacheEntryFormatTest
{
    private static byte[] Entry(params (uint Type, byte[] Content)[] sections)
    {
        using var stream = new MemoryStream();
        PreviewCacheEntryFormat.Write(stream,
            [.. sections.Select(s => (s.Type, (ReadOnlyMemory<byte>)s.Content))]);
        return stream.ToArray();
    }

    private static PreviewCacheEntryLayout? Read(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return PreviewCacheEntryFormat.TryReadLayout(stream, file.Length);
    }

    private static byte[] Slice(byte[] file, PreviewCacheSection section)
    {
        return file.AsSpan((int)section.Offset, (int)section.Length).ToArray();
    }

    [Fact]
    public void Write_ThenRead_FindsEachSectionWhereItIs()
    {
        var file = Entry(
            (PreviewCacheEntryFormat.MetadataSection, [1, 2, 3]),
            (PreviewCacheEntryFormat.PayloadSection, [9, 8, 7, 6]));

        var layout = Read(file);

        Assert.NotNull(layout);
        Assert.Equal([1, 2, 3], Slice(file, layout.Section(PreviewCacheEntryFormat.MetadataSection)!.Value));
        Assert.Equal([9, 8, 7, 6], Slice(file, layout.Section(PreviewCacheEntryFormat.PayloadSection)!.Value));
    }

    [Fact]
    public void Write_StartsWithTheMagicTheVersionAndTheHeadersOwnLength()
    {
        var file = Entry((PreviewCacheEntryFormat.PayloadSection, [5]));

        Assert.Equal("AETP"u8.ToArray(), file[..4]);
        Assert.Equal(PreviewCacheEntryFormat.FormatVersion, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4)));
        // 16 fixed bytes and one 20-byte table entry; the one payload byte follows directly.
        Assert.Equal(36u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8)));
        Assert.Equal(37, file.Length);
    }

    [Fact]
    public void Read_ASectionTypeItDoesNotKnow_StepsOverIt()
    {
        var file = Entry(
            (0xBEEF, [0, 0, 0, 0, 0]),
            (PreviewCacheEntryFormat.PayloadSection, [4, 2]));

        var layout = Read(file);

        Assert.Equal([4, 2], Slice(file, layout!.Section(PreviewCacheEntryFormat.PayloadSection)!.Value));
        Assert.Null(layout.Section(PreviewCacheEntryFormat.MetadataSection));
    }

    /// <summary>
    ///     A later writer may put fields of its own between the table and the first section and say
    ///     so in the header length. This reader goes by that length and never looks at them.
    /// </summary>
    [Fact]
    public void Read_AHeaderLongerThanItKnows_GoesByTheLengthTheHeaderStates()
    {
        var original = Entry((PreviewCacheEntryFormat.PayloadSection, [4, 2]));
        byte[] extra = [0xAA, 0xBB, 0xCC, 0xDD];

        // The same entry with four unknown header bytes behind the table: the header length and
        // the section's offset both grow by four.
        byte[] file = [.. original[..36], .. extra, .. original[36..]];
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 40);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(16 + 4), 40);

        var layout = Read(file);

        Assert.Equal([4, 2], Slice(file, layout!.Section(PreviewCacheEntryFormat.PayloadSection)!.Value));
    }

    [Fact]
    public void Read_AnotherFormatVersion_IsNotAnEntry()
    {
        var file = Entry((PreviewCacheEntryFormat.PayloadSection, [4, 2]));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), (ushort)(PreviewCacheEntryFormat.FormatVersion + 1));

        Assert.Null(Read(file));
    }

    [Fact]
    public void Read_SomethingElse_IsNotAnEntry()
    {
        Assert.Null(Read([]));
        Assert.Null(Read([1, 2, 3]));
        Assert.Null(Read("glTF and then some more bytes to fill a header"u8.ToArray()));
    }

    /// <summary>A file cut short names a section that runs past its end.</summary>
    [Fact]
    public void Read_ASectionPastTheEndOfTheFile_IsNotAnEntry()
    {
        var file = Entry((PreviewCacheEntryFormat.PayloadSection, [4, 2, 1]));

        Assert.Null(Read(file[..^1]));
    }

    [Fact]
    public void Read_ASectionInsideTheHeader_IsNotAnEntry()
    {
        var file = Entry((PreviewCacheEntryFormat.PayloadSection, [4, 2, 1]));
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(16 + 4), 8);

        Assert.Null(Read(file));
    }

    [Fact]
    public void Read_ATableLongerThanTheHeaderSaysItIs_IsNotAnEntry()
    {
        var file = Entry((PreviewCacheEntryFormat.PayloadSection, [4, 2, 1]));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 3);

        Assert.Null(Read(file));
    }
}