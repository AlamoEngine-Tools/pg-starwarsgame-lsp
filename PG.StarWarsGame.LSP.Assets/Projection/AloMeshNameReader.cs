// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     TEMPORARY hand-rolled reader for the mesh (object) names in an ALO model file.
/// </summary>
/// <remarks>
///     <para>
///         The engine treats every mesh as an implicit bone at the mesh's origin, so an XML bone
///         reference (<c>Attachment_Bone</c>, <c>Collision_Mesh</c>, <c>Damage_Decal</c>, ...) resolves
///         against the union of a model's skeleton bones <em>and</em> its mesh names. The vendored
///         <c>PG.StarWarsGame.Files.ALO</c> loader currently exposes only <c>AlamoModel.Bones</c>
///         (its <c>ReadMesh</c> harvests textures/shaders but skips the <c>0x401</c> mesh-name chunk),
///         so this shim recovers the mesh names the loader drops.
///     </para>
///     <para>
///         DEPRECATED BY DESIGN: delete this type as soon as the ALO loader gains an
///         <c>AlamoModel.Meshes</c> collection, and replace call sites with <c>model.Content.Meshes</c>.
///         The single consumer is <c>ModelNameCatalog</c>, which is the stable seam — nothing
///         else should take a dependency on this parser.
///     </para>
///     <para>
///         Parses only what it needs: top-level chunks, descending into each mesh container
///         (<c>0x400</c>) to read its name mini-chunk (<c>0x401</c>). Chunk header is a 4-byte
///         little-endian type followed by a 4-byte little-endian size whose high bit flags a container;
///         the low 31 bits are the body length in bytes. Never throws for a malformed buffer - it
///         returns whatever it read before the structure stopped making sense, matching the loader's
///         "one bad model must not abort the scan" contract.
///     </para>
/// </remarks>
[Obsolete("Temporary shim: remove once PG.StarWarsGame.Files.ALO exposes AlamoModel.Meshes and " +
          "replace with model.Content.Meshes. See ModelNameCatalog for the drop-in seam.")]
public static class AloMeshNameReader
{
    private const uint MeshChunk = 0x0400;
    private const uint MeshNameChunk = 0x0401;
    private const uint SizeMask = 0x7FFFFFFF;
    private const int HeaderSize = 8;

    /// <summary>
    ///     Returns the mesh names in <paramref name="aloBytes" />, in file order. Empty when the buffer
    ///     is not a readable ALO model or contains no meshes.
    /// </summary>
    public static IReadOnlyList<string> ReadMeshNames(byte[] aloBytes)
    {
        var names = new List<string>();
        if (aloBytes is null || aloBytes.Length < HeaderSize)
            return names;

        var span = aloBytes.AsSpan();
        foreach (var chunk in ChunkHeaders(span, 0, span.Length))
        {
            if (chunk.Type != MeshChunk)
                continue;

            foreach (var child in ChunkHeaders(span, chunk.BodyStart, chunk.BodyLength))
            {
                if (child.Type != MeshNameChunk)
                    continue;

                var name = ReadCString(span.Slice(child.BodyStart, child.BodyLength));
                if (name.Length > 0)
                    names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    ///     Walks the chunk headers within <c>[start, start + length)</c> of <paramref name="buffer" />,
    ///     returning absolute body offsets. Stops (rather than throws) the moment a header or body would
    ///     run past the region, so a truncated or non-ALO buffer simply yields fewer chunks. Materialised
    ///     to a list because a <see cref="ReadOnlySpan{T}" /> cannot cross an iterator/yield boundary.
    /// </summary>
    private static List<ChunkSpan> ChunkHeaders(ReadOnlySpan<byte> buffer, int start, int length)
    {
        var result = new List<ChunkSpan>();
        var end = start + length;
        var pos = start;
        while (pos + HeaderSize <= end)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(pos));
            var bodyLength = (int)(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(pos + 4)) & SizeMask);
            var bodyStart = pos + HeaderSize;

            if (bodyLength < 0 || bodyStart + bodyLength > end)
                break;

            result.Add(new ChunkSpan(type, bodyStart, bodyLength));
            pos = bodyStart + bodyLength;
        }

        return result;
    }

    private static string ReadCString(ReadOnlySpan<byte> body)
    {
        var end = body.IndexOf((byte)0);
        if (end < 0) end = body.Length;
        return Encoding.ASCII.GetString(body.Slice(0, end)).Trim();
    }

    private readonly record struct ChunkSpan(uint Type, int BodyStart, int BodyLength);
}
