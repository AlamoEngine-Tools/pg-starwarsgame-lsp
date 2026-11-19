// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>Where one section of a preview cache entry sits in its file.</summary>
public readonly record struct PreviewCacheSection(long Offset, long Length);

/// <summary>The sections a preview cache entry's header names, by section type.</summary>
public sealed class PreviewCacheEntryLayout(IReadOnlyDictionary<uint, PreviewCacheSection> sections)
{
    /// <summary>The section of a type, or null when the entry has none.</summary>
    public PreviewCacheSection? Section(uint type)
    {
        return sections.TryGetValue(type, out var section) ? section : null;
    }
}

/// <summary>
///     The file a preview cache entry is: a header, a section table, and the sections.
/// </summary>
/// <remarks>
///     <para>All integers little-endian.</para>
///     <code>
///     offset  size  field
///          0     4  magic "AETP"
///          4     2  format version
///          6     2  reserved, zero
///          8     4  header length: everything before the first section, the table included
///         12     4  section count
///         16  20*n  section table: type (4), offset from the file's start (8), length (8)
///        ...        whatever a later writer adds to the header, up to the header length
///     </code>
///     <para>
///         <strong>Room to grow without a new version.</strong> A reader goes by the header length
///         and by the table, so a later writer may add header fields after the table and sections
///         of a new type; this reader steps over both. The format version is raised only for a
///         change an older reader would misread.
///     </para>
///     <para>
///         <strong>Not the converter version.</strong> That one is part of an entry's KEY and says
///         what the payload was made by; this one says how the file around it is laid out.
///     </para>
///     <para>
///         The payload section is the bytes exactly as they are sent to the preview, so a hit copies
///         it from the file to the transfer without holding it.
///     </para>
/// </remarks>
public static class PreviewCacheEntryFormat
{
    /// <summary>The extension of an entry file.</summary>
    public const string Extension = ".aetpm";

    public const ushort FormatVersion = 1;

    /// <summary>The clip names baked in and what the conversion had to say, as UTF-8 JSON.</summary>
    public const uint MetadataSection = 1;

    /// <summary>The converted model: a GLB.</summary>
    public const uint PayloadSection = 2;

    private const int FixedHeaderLength = 16;
    private const int TableEntryLength = 20;

    /// <summary>More than any writer has reason to produce; a count above it is a damaged header.</summary>
    private const int MostSections = 64;

    private static ReadOnlySpan<byte> Magic => "AETP"u8;

    /// <summary>Writes an entry: the header, then each section in the order given.</summary>
    public static void Write(Stream target, IReadOnlyList<(uint Type, ReadOnlyMemory<byte> Content)> sections)
    {
        var headerLength = FixedHeaderLength + TableEntryLength * sections.Count;
        var header = new byte[headerLength];

        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)headerLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)sections.Count);

        long offset = headerLength;
        for (var i = 0; i < sections.Count; i++)
        {
            var entry = header.AsSpan(FixedHeaderLength + TableEntryLength * i);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, sections[i].Type);
            BinaryPrimitives.WriteInt64LittleEndian(entry[4..], offset);
            BinaryPrimitives.WriteInt64LittleEndian(entry[12..], sections[i].Content.Length);
            offset += sections[i].Content.Length;
        }

        target.Write(header);
        foreach (var (_, content) in sections)
            target.Write(content.Span);
    }

    /// <summary>
    ///     Reads the header at the start of <paramref name="source" />. Null for anything that is not
    ///     a whole entry of this format version: a caller treats that as "not in the cache".
    /// </summary>
    /// <param name="source">The entry file, at its start.</param>
    /// <param name="fileLength">The file's length, which every section has to lie inside.</param>
    public static PreviewCacheEntryLayout? TryReadLayout(Stream source, long fileLength)
    {
        Span<byte> head = stackalloc byte[FixedHeaderLength];
        if (!TryFill(source, head) || !head[..4].SequenceEqual(Magic))
            return null;

        if (BinaryPrimitives.ReadUInt16LittleEndian(head[4..]) != FormatVersion)
            return null;

        long headerLength = BinaryPrimitives.ReadUInt32LittleEndian(head[8..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(head[12..]);
        if (count > MostSections
            || headerLength < FixedHeaderLength + TableEntryLength * count
            || headerLength > fileLength)
            return null;

        var table = new byte[TableEntryLength * (int)count];
        if (!TryFill(source, table))
            return null;

        var sections = new Dictionary<uint, PreviewCacheSection>();
        for (var i = 0; i < count; i++)
        {
            var entry = table.AsSpan(TableEntryLength * i);
            var type = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var offset = BinaryPrimitives.ReadInt64LittleEndian(entry[4..]);
            var length = BinaryPrimitives.ReadInt64LittleEndian(entry[12..]);

            // Inside the file and behind the header, or the entry was cut short or written over.
            if (offset < headerLength || length < 0 || offset > fileLength - length)
                return null;

            // The first of a type counts; a type this reader does not know is simply not asked for.
            sections.TryAdd(type, new PreviewCacheSection(offset, length));
        }

        return new PreviewCacheEntryLayout(sections);
    }

    private static bool TryFill(Stream source, Span<byte> buffer)
    {
        return source.ReadAtLeast(buffer, buffer.Length, false) == buffer.Length;
    }
}