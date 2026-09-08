// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>One chunk header and the absolute bounds of its body.</summary>
internal readonly record struct AloChunk(uint Type, int BodyStart, int BodyLength, bool IsContainer)
{
    public int BodyEnd => BodyStart + BodyLength;
}

/// <summary>
///     Walks the chunk tree an ALO file is built from, shared by the model, animation and particle
///     readers.
/// </summary>
/// <remarks>
///     <para>
///         Two framings exist. A <em>chunk</em> is a little-endian <c>uint32</c> type followed by a
///         <c>uint32</c> whose high bit marks a container and whose low 31 bits are the body length.
///         A <em>mini-chunk</em>, used inside the connections and particle blocks, is one type byte
///         and one size byte - so a mini-chunk body can never exceed 255 bytes, which is why the
///         format uses them only for scalars and short strings.
///     </para>
///     <para>
///         Everything here is strict: a header that would run past its enclosing region raises rather
///         than truncating. A geometry reader that stops silently produces a model with missing pieces
///         and no explanation, which is the worst outcome for someone trying to work out why their
///         unit looks wrong. Callers that genuinely need to survive a bad file - the repository-wide
///         name scan in <c>ModelNameCatalog</c> is the only one - catch at their own boundary, where
///         leniency is a stated requirement rather than a silent default.
///     </para>
///     <para>
///         Every count that comes out of the file and drives an allocation is bounded against the
///         bytes actually remaining before it is used. The C++ reader was hardened this way after a
///         malformed-input audit and the same guards are kept here.
///     </para>
/// </remarks>
internal static class AloChunkStream
{
    public const int HeaderSize = 8;
    public const int MiniHeaderSize = 2;
    private const uint ContainerBit = 0x80000000;
    private const uint SizeMask = 0x7FFFFFFF;

    /// <summary>The chunks directly inside <c>[start, end)</c>, in file order.</summary>
    public static List<AloChunk> Children(byte[] buffer, int start, int end)
    {
        var result = new List<AloChunk>();
        var pos = start;

        while (pos < end)
        {
            if (pos + HeaderSize > end)
                throw Malformed($"a chunk header at offset {pos} runs past its parent, which ends at {end}");

            var type = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(pos));
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(pos + 4));
            var length = (int)(raw & SizeMask);
            var bodyStart = pos + HeaderSize;

            if (bodyStart + length > end)
                throw Malformed(
                    $"chunk 0x{type:X} at offset {pos} declares {length} body bytes but only " +
                    $"{end - bodyStart} remain before its parent ends");

            result.Add(new AloChunk(type, bodyStart, length, (raw & ContainerBit) != 0));
            pos = bodyStart + length;
        }

        return result;
    }

    /// <summary>The mini-chunks directly inside <c>[start, end)</c>, in file order.</summary>
    public static List<AloChunk> MiniChildren(byte[] buffer, int start, int end)
    {
        var result = new List<AloChunk>();
        var pos = start;

        while (pos < end)
        {
            if (pos + MiniHeaderSize > end)
                throw Malformed($"a mini-chunk header at offset {pos} runs past its parent");

            var type = buffer[pos];
            var length = buffer[pos + 1];
            var bodyStart = pos + MiniHeaderSize;

            if (bodyStart + length > end)
                throw Malformed(
                    $"mini-chunk 0x{type:X} at offset {pos} declares {length} body bytes but only " +
                    $"{end - bodyStart} remain");

            result.Add(new AloChunk(type, bodyStart, length, false));
            pos = bodyStart + length;
        }

        return result;
    }

    /// <summary>The single child of <paramref name="type" />, or a refusal naming what was found.</summary>
    public static AloChunk Expect(List<AloChunk> children, int index, uint type, string what)
    {
        if (index >= children.Count)
            throw Malformed($"expected {what} (chunk 0x{type:X}) but the block ended");

        var chunk = children[index];
        if (chunk.Type != type)
            throw Malformed(
                $"expected {what} (chunk 0x{type:X}) at offset {chunk.BodyStart - HeaderSize} " +
                $"but found chunk 0x{chunk.Type:X}");

        return chunk;
    }

    /// <summary>A NUL-terminated ASCII string filling the chunk body.</summary>
    public static string ReadString(byte[] buffer, AloChunk chunk)
    {
        var body = buffer.AsSpan(chunk.BodyStart, chunk.BodyLength);
        var end = body.IndexOf((byte)0);
        if (end < 0) end = body.Length;
        return Encoding.ASCII.GetString(body[..end]).Trim();
    }

    public static int ReadInt32(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset));
    }

    public static uint ReadUInt32(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));
    }

    public static float ReadSingle(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(offset));
    }

    /// <summary>
    ///     Refuses a count that could not possibly be backed by the bytes left in the file.
    /// </summary>
    /// <remarks>
    ///     Each counted item is at least one chunk, so it costs at least a header. Checking the count
    ///     against the remaining bytes before allocating is what stops a crafted file from asking for
    ///     a multi-gigabyte list it never intends to fill.
    /// </remarks>
    public static int CheckedCount(uint count, int bytesRemaining, string what)
    {
        if (count > (uint)Math.Max(bytesRemaining, 0))
            throw Malformed(
                $"declares {count} {what} but only {bytesRemaining} bytes remain, so the count " +
                "cannot be honest");

        return (int)count;
    }

    public static AloFormatException Malformed(string detail)
    {
        return new AloFormatException($"Malformed ALO file: {detail}.");
    }
}
