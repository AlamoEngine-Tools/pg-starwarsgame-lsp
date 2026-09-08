// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Core.Symbols;
using static PG.StarWarsGame.LSP.Assets.Models.AloChunkStream;

namespace PG.StarWarsGame.LSP.Assets.Models;

/// <summary>
///     Reads the geometry, skeleton and attachments of an ALO model file.
/// </summary>
/// <remarks>
///     <para>
///         Ported from <c>alo-viewer/src/Assets/Models.cpp</c>, which is the only complete description
///         of the format we have. The vendored <c>PG.StarWarsGame.Files.ALO</c> loader reads names
///         only - it skips every geometry chunk - so nothing there could be extended into this.
///     </para>
///     <para>
///         Strict by design: a structural surprise raises <see cref="AloFormatException" /> naming the
///         chunk rather than yielding a partly-read model. See <see cref="AloChunkStream" /> for why.
///     </para>
/// </remarks>
public static class AloModelReader
{
    private const uint ChunkSkeleton = 0x200;
    private const uint ChunkBoneCount = 0x201;
    private const uint ChunkBone = 0x202;
    private const uint ChunkBoneName = 0x203;
    private const uint ChunkBoneData = 0x205;
    private const uint ChunkBoneDataBillboard = 0x206;

    private const uint ChunkMesh = 0x400;
    private const uint ChunkMeshName = 0x401;
    private const uint ChunkMeshInfo = 0x402;
    private const uint ChunkSubMeshMaterial = 0x10100;
    private const uint ChunkShaderName = 0x10101;
    private const uint ChunkParamInt = 0x10102;
    private const uint ChunkParamFloat = 0x10103;
    private const uint ChunkParamFloat3 = 0x10104;
    private const uint ChunkParamTexture = 0x10105;
    private const uint ChunkParamFloat4 = 0x10106;
    private const uint ChunkSubMeshData = 0x10000;
    private const uint ChunkCounts = 0x10001;
    private const uint ChunkVertexFormat = 0x10002;
    private const uint ChunkVerticesOld = 0x10005;
    private const uint ChunkSkin = 0x10006;
    private const uint ChunkVertices = 0x10007;
    private const uint ChunkIndices = 0x10004;
    private const uint ChunkCollisionTree = 0x1200;

    private const uint ChunkLight = 0x1300;
    private const uint ChunkLightName = 0x1301;
    private const uint ChunkLightInfo = 0x1302;

    private const uint ChunkConnections = 0x600;
    private const uint ChunkConnectionCounts = 0x601;
    private const uint ChunkConnection = 0x602;
    private const uint ChunkProxy = 0x603;
    private const uint ChunkDazzle = 0x604;

    private const byte MiniParamName = 1;
    private const byte MiniParamValue = 2;

    private const byte MiniConnectionCount = 1;
    private const byte MiniConnectionObject = 2;
    private const byte MiniConnectionBone = 3;
    private const byte MiniProxyCount = 4;
    private const byte MiniProxyName = 5;
    private const byte MiniProxyBone = 6;
    private const byte MiniProxyHidden = 7;
    private const byte MiniProxyAltDecreaseStayHidden = 8;
    private const byte MiniDazzleCount = 9;

    /// <summary>
    ///     The light info record: a type int, an RGB colour, then five floats. Exactly 36 bytes on all
    ///     246 lights across the shipped trees.
    /// </summary>
    private const int LightInfoSize = 36;

    /// <summary>
    ///     The bone-count chunk is a fixed-size record of which only the leading <c>uint32</c> is
    ///     meaningful; the exporter pads the remainder. Skipping to the chunk's declared end rather
    ///     than assuming 4 bytes is what keeps the following bone chunks aligned.
    /// </summary>
    private const int BoneCountChunkSize = 128;

    /// <summary>
    ///     The mesh info record is padded the same way: 128 bytes of which the first 40 carry the
    ///     sub-mesh count, the bounds and the two flags. Every one of the 3217 meshes sampled from the
    ///     shipped trees is exactly this size.
    /// </summary>
    private const int MeshInfoChunkSize = 128;

    /// <summary>
    ///     The stored vertex is always the fat one regardless of the declared format: 144 bytes for
    ///     <see cref="ChunkVertices" />, or 128 for the older <see cref="ChunkVerticesOld" />, which is
    ///     the same layout minus the unused <c>float4</c>. Both strides divide their chunks exactly
    ///     across the whole shipped corpus.
    /// </summary>
    private const int MasterVertexSize = 144;

    private const int OldVertexSize = 128;

    private const int IndicesPerFace = 3;

    /// <summary>Parses <paramref name="bytes" /> as an ALO model.</summary>
    /// <param name="options">
    ///     Use <see cref="AloReadOptions.SkipGeometry" /> when only names and structure are wanted.
    /// </param>
    /// <exception cref="AloFormatException">The buffer is not a well-formed ALO model.</exception>
    public static AlamoModelContent Read(byte[] bytes, AloReadOptions options = AloReadOptions.All)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var top = Children(bytes, 0, bytes.Length);
        if (top.Count == 0)
            throw Malformed("the file contains no chunks");

        var skeleton = Expect(top, 0, ChunkSkeleton, "the skeleton block");
        var bones = ReadSkeleton(bytes, skeleton);

        var meshes = new List<AlamoMesh>();
        var lights = new List<AlamoLight>();

        // Meshes and lights are ONE list as far as the connections block is concerned: it indexes
        // attachables in top-level file order, interleaved. Recording that order here is what keeps
        // a model carrying both from attaching its lights to the wrong bones.
        var attachables = new List<(bool IsLight, int Index)>();

        AloChunk? connections = null;

        for (var i = 1; i < top.Count; i++)
        {
            var chunk = top[i];
            switch (chunk.Type)
            {
                case ChunkMesh:
                    attachables.Add((false, meshes.Count));
                    meshes.Add(ReadMesh(bytes, chunk, meshes.Count, options));
                    break;
                case ChunkLight:
                    attachables.Add((true, lights.Count));
                    lights.Add(ReadLight(bytes, chunk, lights.Count));
                    break;
                case ChunkConnections:
                    connections = chunk;
                    break;
                default:
                    throw Malformed(
                        $"unexpected top-level chunk 0x{chunk.Type:X} at offset " +
                        $"{chunk.BodyStart - AloChunkStream.HeaderSize}");
            }
        }

        var proxies = new List<AlamoProxy>();
        var dazzles = new List<AlamoDazzle>();
        if (connections is not null)
            ReadConnections(bytes, connections.Value, bones.Count, attachables, meshes, lights,
                proxies, dazzles);

        return new AlamoModelContent(bones, meshes, lights, proxies, dazzles);
    }

    private static List<AlamoModelBone> ReadSkeleton(byte[] bytes, AloChunk skeleton)
    {
        var children = Children(bytes, skeleton.BodyStart, skeleton.BodyEnd);

        var countChunk = Expect(children, 0, ChunkBoneCount, "the bone count");
        if (countChunk.BodyLength != BoneCountChunkSize)
            throw Malformed(
                $"the bone-count chunk is {countChunk.BodyLength} bytes, expected {BoneCountChunkSize}");

        var declared = ReadUInt32(bytes, countChunk.BodyStart);
        var boneCount = CheckedCount(declared, skeleton.BodyEnd - countChunk.BodyEnd, "bones");

        if (children.Count - 1 != boneCount)
            throw Malformed(
                $"the skeleton declares {boneCount} bones but carries {children.Count - 1} bone chunks");

        var bones = new List<AlamoModelBone>(boneCount);
        for (var i = 0; i < boneCount; i++)
            bones.Add(ReadBone(bytes, Expect(children, i + 1, ChunkBone, $"bone {i}"), i, bones));

        return bones;
    }

    /// <param name="preceding">
    ///     The bones already read. The parent's absolute transform is composed from it, so a parent
    ///     index must point backwards - which the exporter always honours, and which is checked here
    ///     rather than assumed, because a forward reference would otherwise silently compose against
    ///     an identity matrix.
    /// </param>
    private static AlamoModelBone ReadBone(
        byte[] bytes, AloChunk chunk, int index, List<AlamoModelBone> preceding)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);

        var name = ReadString(bytes, Expect(children, 0, ChunkBoneName, $"the name of bone {index}"));

        if (children.Count < 2)
            throw Malformed($"bone {index} ('{name}') carries no transform chunk");

        var data = children[1];
        var hasBillboard = data.Type == ChunkBoneDataBillboard;
        if (!hasBillboard && data.Type != ChunkBoneData)
            throw Malformed(
                $"bone {index} ('{name}') has chunk 0x{data.Type:X} where its transform " +
                $"(0x{ChunkBoneData:X} or 0x{ChunkBoneDataBillboard:X}) was expected");

        // parent | visible | [billboard] | 12 transform floats
        var expected = (hasBillboard ? 3 : 2) * sizeof(int) + 12 * sizeof(float);
        if (data.BodyLength != expected)
            throw Malformed(
                $"bone {index} ('{name}') has a {data.BodyLength}-byte transform chunk, expected {expected}");

        var pos = data.BodyStart;
        var parent = ReadInt32(bytes, pos);
        pos += sizeof(int);
        var visible = ReadInt32(bytes, pos) != 0;
        pos += sizeof(int);

        var billboard = AlamoBillboardType.Disable;
        if (hasBillboard)
        {
            billboard = (AlamoBillboardType)ReadInt32(bytes, pos);
            pos += sizeof(int);
        }

        if (parent >= index)
            throw Malformed(
                $"bone {index} ('{name}') names parent {parent}, which is not an earlier bone");

        var relative = ReadTransform(bytes, pos);
        var absolute = parent < 0
            ? relative
            : Matrix4x4.Multiply(relative, preceding[parent].AbsoluteTransform);

        return new AlamoModelBone(index, name, parent, visible, billboard, relative, absolute);
    }

    // ── meshes ────────────────────────────────────────────────────────────────

    private static AlamoMesh ReadMesh(
        byte[] bytes, AloChunk chunk, int index, AloReadOptions options)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);

        var name = ReadString(bytes, Expect(children, 0, ChunkMeshName, $"the name of mesh {index}"));
        var info = Expect(children, 1, ChunkMeshInfo, $"the info record of mesh '{name}'");
        if (info.BodyLength != MeshInfoChunkSize)
            throw Malformed(
                $"mesh '{name}' has a {info.BodyLength}-byte info record, expected {MeshInfoChunkSize}");

        var pos = info.BodyStart;
        var declared = ReadUInt32(bytes, pos);
        pos += sizeof(uint);
        var min = ReadVector3(bytes, pos);
        pos += 3 * sizeof(float);
        var max = ReadVector3(bytes, pos);
        pos += 3 * sizeof(float);
        pos += sizeof(int); // unknown; zero throughout the shipped trees

        // Stored INVERTED - the field is a hidden flag, so zero means visible.
        var visible = ReadInt32(bytes, pos) == 0;
        pos += sizeof(int);
        var collidable = ReadInt32(bytes, pos) != 0;

        var subMeshCount = CheckedCount(declared, chunk.BodyEnd - info.BodyEnd, "sub-meshes");

        // Each sub-mesh is a PAIR of sibling chunks - a material block and a data block - so the
        // count and the child list are related by two, not one.
        var expectedChildren = 2 + 2 * subMeshCount;
        if (children.Count != expectedChildren)
            throw Malformed(
                $"mesh '{name}' declares {subMeshCount} sub-meshes, which needs {expectedChildren} " +
                $"child chunks, but it carries {children.Count}");

        var subMeshes = new List<AlamoSubMesh>(subMeshCount);
        for (var i = 0; i < subMeshCount; i++)
            subMeshes.Add(ReadSubMesh(bytes, children[2 + i * 2], children[3 + i * 2], name, i, options));

        var (alt, lod) = ParseAltLod(name);

        return new AlamoMesh(index, name, new AlamoBoundingBox(min, max), visible, collidable,
            alt, lod, subMeshes);
    }

    private static AlamoSubMesh ReadSubMesh(
        byte[] bytes, AloChunk material, AloChunk data, string meshName, int index,
        AloReadOptions options)
    {
        var where = $"sub-mesh {index} of mesh '{meshName}'";

        if (material.Type != ChunkSubMeshMaterial)
            throw Malformed(
                $"{where} starts with chunk 0x{material.Type:X} where its material block " +
                $"(0x{ChunkSubMeshMaterial:X}) was expected");
        if (data.Type != ChunkSubMeshData)
            throw Malformed(
                $"{where} has chunk 0x{data.Type:X} where its geometry block " +
                $"(0x{ChunkSubMeshData:X}) was expected");

        var materialChildren = Children(bytes, material.BodyStart, material.BodyEnd);
        var shader = ReadString(bytes, Expect(materialChildren, 0, ChunkShaderName, $"the shader of {where}"));

        var parameters = new List<AlamoShaderParameter>(materialChildren.Count - 1);
        for (var i = 1; i < materialChildren.Count; i++)
            parameters.Add(ReadShaderParameter(bytes, materialChildren[i], where));

        return ReadSubMeshGeometry(bytes, data, shader, parameters, where, options);
    }

    private static AlamoSubMesh ReadSubMeshGeometry(
        byte[] bytes, AloChunk data, string shader, List<AlamoShaderParameter> parameters, string where,
        AloReadOptions options)
    {
        var children = Children(bytes, data.BodyStart, data.BodyEnd);

        var counts = Expect(children, 0, ChunkCounts, $"the vertex and face counts of {where}");
        if (counts.BodyLength < 2 * sizeof(uint))
            throw Malformed($"{where} has a {counts.BodyLength}-byte count record, expected at least 8");

        var vertexCount = ReadUInt32(bytes, counts.BodyStart);
        var faceCount = ReadUInt32(bytes, counts.BodyStart + sizeof(uint));

        var format = ReadString(bytes, Expect(children, 1, ChunkVertexFormat, $"the vertex format of {where}"));

        if (children.Count < 4)
            throw Malformed($"{where} is missing its vertex or index chunk");

        var vertices = ReadVertices(bytes, children[2], vertexCount, where, options);
        var indices = ReadIndices(bytes, Expect(children, 3, ChunkIndices, $"the indices of {where}"),
            faceCount, where, options);

        var skinBones = new List<int>();
        for (var i = 4; i < children.Count; i++)
        {
            var extra = children[i];
            if (extra.Type == ChunkSkin)
                skinBones = ReadSkinTable(bytes, extra, where);
            else if (extra.Type != ChunkCollisionTree)
                // The collision tree is skipped by design - it is a spatial index for physics, not
                // something a preview draws. Anything else here is a format surprise worth naming.
                throw Malformed($"{where} carries unexpected chunk 0x{extra.Type:X}");
        }

        return new AlamoSubMesh(shader, parameters, format, SkinningFor(format), vertices, indices,
            skinBones);
    }

    private static List<AlamoVertex> ReadVertices(
        byte[] bytes, AloChunk chunk, uint vertexCount, string where, AloReadOptions options)
    {
        int stride;
        bool hasUnusedField;

        switch (chunk.Type)
        {
            case ChunkVertices:
                stride = MasterVertexSize;
                hasUnusedField = true;
                break;
            case ChunkVerticesOld:
                stride = OldVertexSize;
                hasUnusedField = false;
                break;
            default:
                throw Malformed(
                    $"{where} has chunk 0x{chunk.Type:X} where its vertices " +
                    $"(0x{ChunkVertices:X} or 0x{ChunkVerticesOld:X}) were expected");
        }

        // Bound the count against the payload BEFORE multiplying, so a crafted count can neither
        // overflow the multiply nor drive an allocation the file could not possibly fill.
        if ((long)vertexCount * stride != chunk.BodyLength)
            throw Malformed(
                $"{where} declares {vertexCount} vertices at {stride} bytes each, which needs " +
                $"{(long)vertexCount * stride} bytes, but its vertex chunk holds {chunk.BodyLength}");

        // The size check above is the validation; only the per-vertex decode is optional.
        if (options.HasFlag(AloReadOptions.SkipGeometry))
            return [];

        var vertices = new List<AlamoVertex>((int)vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var o = chunk.BodyStart + i * stride;

            // UV sets 2 and 3 sit between TexCoord1 and the tangent and are always zero; skipped
            // rather than stored. The unused float4 sits between the colour and the bone indices on
            // the 144-byte form only, which is the sole difference between the two strides.
            var boneOffset = o + 96 + (hasUnusedField ? 16 : 0);

            vertices.Add(new AlamoVertex(
                ReadVector3(bytes, o),
                ReadVector3(bytes, o + 12),
                ReadVector2(bytes, o + 24),
                ReadVector2(bytes, o + 32),
                ReadVector3(bytes, o + 56),
                ReadVector3(bytes, o + 68),
                ReadVector4(bytes, o + 80),
                new AlamoBoneIndices(
                    ReadUInt32(bytes, boneOffset),
                    ReadUInt32(bytes, boneOffset + 4),
                    ReadUInt32(bytes, boneOffset + 8),
                    ReadUInt32(bytes, boneOffset + 12)),
                ReadVector4(bytes, boneOffset + 16)));
        }

        return vertices;
    }

    private static List<ushort> ReadIndices(
        byte[] bytes, AloChunk chunk, uint faceCount, string where, AloReadOptions options)
    {
        var expected = (long)faceCount * IndicesPerFace * sizeof(ushort);
        if (expected != chunk.BodyLength)
            throw Malformed(
                $"{where} declares {faceCount} faces, which needs {expected} index bytes, but its " +
                $"index chunk holds {chunk.BodyLength}");

        if (options.HasFlag(AloReadOptions.SkipGeometry))
            return [];

        var count = (int)faceCount * IndicesPerFace;
        var indices = new List<ushort>(count);
        for (var i = 0; i < count; i++)
            indices.Add(BitConverter.ToUInt16(bytes, chunk.BodyStart + i * sizeof(ushort)));

        return indices;
    }

    private static List<int> ReadSkinTable(byte[] bytes, AloChunk chunk, string where)
    {
        if (chunk.BodyLength % sizeof(int) != 0)
            throw Malformed($"{where} has a {chunk.BodyLength}-byte skin table, which is not whole ints");

        var count = chunk.BodyLength / sizeof(int);
        var table = new List<int>(count);
        for (var i = 0; i < count; i++)
            table.Add(ReadInt32(bytes, chunk.BodyStart + i * sizeof(int)));

        return table;
    }

    private static AlamoShaderParameter ReadShaderParameter(byte[] bytes, AloChunk chunk, string where)
    {
        var minis = MiniChildren(bytes, chunk.BodyStart, chunk.BodyEnd);
        if (minis.Count < 2 || minis[0].Type != MiniParamName || minis[1].Type != MiniParamValue)
            throw Malformed($"a shader parameter of {where} is not a name/value pair");

        var name = ReadString(bytes, minis[0]);
        var value = minis[1];

        return chunk.Type switch
        {
            ChunkParamInt => new AlamoShaderParameter(name, AlamoShaderParameterType.Int)
                { Int = ReadInt32(bytes, value.BodyStart) },
            ChunkParamFloat => new AlamoShaderParameter(name, AlamoShaderParameterType.Float)
                { Float = ReadSingle(bytes, value.BodyStart) },
            ChunkParamFloat3 => new AlamoShaderParameter(name, AlamoShaderParameterType.Float3)
                { Float3 = ReadVector3(bytes, value.BodyStart) },
            ChunkParamFloat4 => new AlamoShaderParameter(name, AlamoShaderParameterType.Float4)
                { Float4 = ReadVector4(bytes, value.BodyStart) },
            ChunkParamTexture => new AlamoShaderParameter(name, AlamoShaderParameterType.Texture)
                { Texture = ReadString(bytes, value) },
            _ => throw Malformed(
                $"shader parameter '{name}' of {where} has unknown value chunk 0x{chunk.Type:X}")
        };
    }

    // ── lights and connections ────────────────────────────────────────────────

    private static AlamoLight ReadLight(byte[] bytes, AloChunk chunk, int index)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);

        var name = ReadString(bytes, Expect(children, 0, ChunkLightName, $"the name of light {index}"));
        var info = Expect(children, 1, ChunkLightInfo, $"the info record of light '{name}'");
        if (info.BodyLength != LightInfoSize)
            throw Malformed(
                $"light '{name}' has a {info.BodyLength}-byte info record, expected {LightInfoSize}");

        var o = info.BodyStart;
        return new AlamoLight(
            name,
            (AlamoLightType)ReadInt32(bytes, o),
            ReadVector3(bytes, o + 4),
            ReadSingle(bytes, o + 16),
            ReadSingle(bytes, o + 20),
            ReadSingle(bytes, o + 24),
            ReadSingle(bytes, o + 28),
            ReadSingle(bytes, o + 32));
    }

    private static void ReadConnections(
        byte[] bytes,
        AloChunk chunk,
        int boneCount,
        List<(bool IsLight, int Index)> attachables,
        List<AlamoMesh> meshes,
        List<AlamoLight> lights,
        List<AlamoProxy> proxies,
        List<AlamoDazzle> dazzles)
    {
        var children = Children(bytes, chunk.BodyStart, chunk.BodyEnd);
        var header = Expect(children, 0, ChunkConnectionCounts, "the connection counts");
        var counts = MiniChildren(bytes, header.BodyStart, header.BodyEnd);

        var connectionCount = RequiredCount(bytes, counts, MiniConnectionCount, "connections");
        var proxyCount = RequiredCount(bytes, counts, MiniProxyCount, "proxies");

        // The dazzle count is optional - Universe at War added it, and exactly one Star Wars model
        // even carries the field. Absent means none.
        var dazzleCount = OptionalCount(bytes, counts, MiniDazzleCount);

        var available = chunk.BodyEnd - header.BodyEnd;
        var total = (long)connectionCount + proxyCount + dazzleCount;
        _ = CheckedCount((uint)Math.Min(total, uint.MaxValue), available, "connection entries");

        if (children.Count != 1 + total)
            throw Malformed(
                $"the connections block declares {connectionCount} connections, {proxyCount} proxies " +
                $"and {dazzleCount} dazzles, needing {total + 1} chunks, but it carries {children.Count}");

        var at = 1;

        for (var i = 0; i < connectionCount; i++, at++)
        {
            var (objectIndex, boneIndex) = ReadConnection(bytes, children[at], i);

            if (objectIndex < 0 || objectIndex >= attachables.Count)
                throw Malformed(
                    $"connection {i} names object {objectIndex}, but the model has " +
                    $"{attachables.Count} meshes and lights");
            CheckBone(boneIndex, boneCount, $"connection {i}");

            var (isLight, listIndex) = attachables[objectIndex];
            if (isLight)
                lights[listIndex] = lights[listIndex] with { BoneIndex = boneIndex };
            else
                meshes[listIndex] = meshes[listIndex] with { BoneIndex = boneIndex };
        }

        for (var i = 0; i < proxyCount; i++, at++)
            proxies.Add(ReadProxy(bytes, children[at], i, boneCount));

        for (var i = 0; i < dazzleCount; i++, at++)
            dazzles.Add(ReadDazzle(bytes, children[at], i, boneCount));
    }

    private static (int Object, int Bone) ReadConnection(byte[] bytes, AloChunk chunk, int index)
    {
        if (chunk.Type != ChunkConnection)
            throw Malformed(
                $"expected connection {index} (chunk 0x{ChunkConnection:X}) but found 0x{chunk.Type:X}");

        var minis = MiniChildren(bytes, chunk.BodyStart, chunk.BodyEnd);
        var objectIndex = RequiredCount(bytes, minis, MiniConnectionObject, $"connection {index}'s object");
        var boneIndex = RequiredCount(bytes, minis, MiniConnectionBone, $"connection {index}'s bone");

        return (objectIndex, boneIndex);
    }

    private static AlamoProxy ReadProxy(byte[] bytes, AloChunk chunk, int index, int boneCount)
    {
        if (chunk.Type != ChunkProxy)
            throw Malformed(
                $"expected proxy {index} (chunk 0x{ChunkProxy:X}) but found 0x{chunk.Type:X}");

        var minis = MiniChildren(bytes, chunk.BodyStart, chunk.BodyEnd);

        string? name = null;
        int? boneIndex = null;
        var visible = true;
        var altDecreaseStayHidden = false;

        foreach (var mini in minis)
            switch (mini.Type)
            {
                case MiniProxyName:
                    name = ReadString(bytes, mini);
                    break;
                case MiniProxyBone:
                    boneIndex = ReadInt32(bytes, mini.BodyStart);
                    break;
                case MiniProxyHidden:
                    // Inverted, like the mesh flag: the field is "hidden".
                    visible = ReadInt32(bytes, mini.BodyStart) == 0;
                    break;
                case MiniProxyAltDecreaseStayHidden:
                    altDecreaseStayHidden = ReadInt32(bytes, mini.BodyStart) != 0;
                    break;
                default:
                    throw Malformed($"proxy {index} carries unknown mini-chunk {mini.Type}");
            }

        if (name is null || boneIndex is null)
            throw Malformed($"proxy {index} is missing its name or its bone");

        CheckBone(boneIndex.Value, boneCount, $"proxy '{name}'");

        var (alt, lod) = ParseAltLod(name);
        return new AlamoProxy(name, boneIndex.Value, visible, altDecreaseStayHidden, alt, lod);
    }

    /// <summary>
    ///     Reads a dazzle to the layout in <c>Models.cpp</c>.
    /// </summary>
    /// <remarks>
    ///     Unexercised by real data - see <see cref="AlamoDazzle" />. Written strictly so that if a
    ///     file ever does carry one, a mismatch is reported rather than silently mis-read.
    /// </remarks>
    private static AlamoDazzle ReadDazzle(byte[] bytes, AloChunk chunk, int index, int boneCount)
    {
        if (chunk.Type != ChunkDazzle)
            throw Malformed(
                $"expected dazzle {index} (chunk 0x{ChunkDazzle:X}) but found 0x{chunk.Type:X}");

        var inner = Children(bytes, chunk.BodyStart, chunk.BodyEnd);
        if (inner.Count != 1 || inner[0].Type != 0)
            throw Malformed($"dazzle {index} is not a single type-0 record");

        var minis = MiniChildren(bytes, inner[0].BodyStart, inner[0].BodyEnd);
        var by = new Dictionary<byte, AloChunk>();
        foreach (var mini in minis) by[(byte)mini.Type] = mini;

        AloChunk Need(byte type, string what)
        {
            if (!by.TryGetValue(type, out var m))
                throw Malformed($"dazzle {index} is missing its {what} (mini-chunk {type})");
            return m;
        }

        var boneIndex = ReadInt32(bytes, Need(10, "bone").BodyStart);
        var name = ReadString(bytes, Need(11, "name"));
        CheckBone(boneIndex, boneCount, $"dazzle '{name}'");

        return new AlamoDazzle(
            name,
            boneIndex,
            ReadVector3(bytes, Need(0, "colour").BodyStart),
            ReadVector3(bytes, Need(1, "position").BodyStart),
            ReadSingle(bytes, Need(2, "radius").BodyStart),
            ReadString(bytes, Need(5, "texture")),
            ReadInt32(bytes, Need(3, "texture x").BodyStart),
            ReadInt32(bytes, Need(4, "texture y").BodyStart),
            ReadInt32(bytes, Need(6, "texture size").BodyStart),
            ReadSingle(bytes, Need(7, "frequency").BodyStart),
            ReadSingle(bytes, Need(8, "phase").BodyStart),
            by.TryGetValue(13, out var bias) ? ReadSingle(bytes, bias.BodyStart) : 1f,
            ReadInt32(bytes, Need(9, "night-only flag").BodyStart) != 0,
            ReadInt32(bytes, Need(12, "visibility flag").BodyStart) == 0);
    }

    private static int RequiredCount(byte[] bytes, List<AloChunk> minis, byte type, string what)
    {
        foreach (var mini in minis)
            if (mini.Type == type)
                return ReadInt32(bytes, mini.BodyStart);

        throw Malformed($"the block is missing {what} (mini-chunk {type})");
    }

    private static int OptionalCount(byte[] bytes, List<AloChunk> minis, byte type)
    {
        foreach (var mini in minis)
            if (mini.Type == type)
                return Math.Max(0, ReadInt32(bytes, mini.BodyStart));

        return 0;
    }

    private static void CheckBone(int boneIndex, int boneCount, string what)
    {
        if (boneIndex < 0 || boneIndex >= boneCount)
            throw Malformed($"{what} names bone {boneIndex}, but the skeleton has {boneCount} bones");
    }

    /// <summary>
    ///     Which skinning family a vertex-format string names. The format is the only record of it -
    ///     the stored vertex carries four bone slots either way.
    /// </summary>
    private static AlamoSkinningMode SkinningFor(string vertexFormat)
    {
        if (vertexFormat.Contains("RSKIN", StringComparison.OrdinalIgnoreCase))
            return AlamoSkinningMode.ReducedSkin;
        if (vertexFormat.Contains("B4I4", StringComparison.OrdinalIgnoreCase))
            return AlamoSkinningMode.FullSkin;

        return AlamoSkinningMode.Static;
    }

    /// <summary>
    ///     Recovers the <c>_ALT&lt;n&gt;</c> and <c>_LOD&lt;n&gt;</c> levels encoded in a mesh or proxy
    ///     name. A suffix with no digits after it counts as absent, matching the engine.
    /// </summary>
    /// <remarks>
    ///     The rule itself lives in <see cref="ModelLevelTag" />: the preview's particle endpoint and
    ///     the damage-stage checks read the same tags off names that never pass through this reader,
    ///     and a second copy of it here would be free to drift from them.
    /// </remarks>
    private static (int? Alt, int? Lod) ParseAltLod(string name)
    {
        return (ModelLevelTag.AltOf(name), ModelLevelTag.LodOf(name));
    }

    private static Vector2 ReadVector2(byte[] bytes, int offset)
    {
        return new Vector2(ReadSingle(bytes, offset), ReadSingle(bytes, offset + 4));
    }

    private static Vector3 ReadVector3(byte[] bytes, int offset)
    {
        return new Vector3(
            ReadSingle(bytes, offset), ReadSingle(bytes, offset + 4), ReadSingle(bytes, offset + 8));
    }

    private static Vector4 ReadVector4(byte[] bytes, int offset)
    {
        return new Vector4(
            ReadSingle(bytes, offset), ReadSingle(bytes, offset + 4),
            ReadSingle(bytes, offset + 8), ReadSingle(bytes, offset + 12));
    }

    /// <summary>
    ///     Reads the 12 stored floats into a transform.
    /// </summary>
    /// <remarks>
    ///     The file holds three rows of four - each row an axis followed by that axis's translation
    ///     component - which is the transpose of the row-vector matrix the engine works in, hence the
    ///     <c>transpose()</c> call in <c>Models.cpp</c>. Transposing on the way in rather than storing
    ///     the file's layout means translation lands where <see cref="Matrix4x4.Translation" /> looks
    ///     for it, and <see cref="Matrix4x4.Multiply" /> composes parents the same way the engine does.
    /// </remarks>
    private static Matrix4x4 ReadTransform(byte[] bytes, int offset)
    {
        float F(int i)
        {
            return ReadSingle(bytes, offset + i * sizeof(float));
        }

        return new Matrix4x4(
            F(0), F(4), F(8), 0f,
            F(1), F(5), F(9), 0f,
            F(2), F(6), F(10), 0f,
            F(3), F(7), F(11), 1f);
    }
}
