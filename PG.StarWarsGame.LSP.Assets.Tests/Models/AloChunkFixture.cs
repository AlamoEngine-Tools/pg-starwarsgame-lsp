// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using System.Text;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Builds synthetic ALO chunk streams that are byte-for-byte the shape the exporter writes.
/// </summary>
/// <remarks>
///     <para>
///         Written BY HAND rather than by round-tripping through a writer of our own, for the same
///         reason the mega texture fixture is: a fixture produced by the code under test lets a
///         layout bug cancel itself out and pass, while every real game model would be read wrong.
///         Every offset here was read off <c>alo-viewer/src/Assets/Models.cpp</c> and cross-checked
///         against the shipped files in <c>eaw/Data/Art/Models/</c>.
///     </para>
///     <para>
///         Chunk header: little-endian <c>uint32</c> type, then a <c>uint32</c> whose high bit marks
///         a container and whose low 31 bits are the body length. Mini-chunks - used inside the
///         connections block - are instead a single type byte and a single size byte.
///     </para>
/// </remarks>
internal static class AloChunkFixture
{
    private const uint ContainerBit = 0x80000000;

    // ── primitives ────────────────────────────────────────────────────────────

    public static byte[] Chunk(uint type, bool container, params byte[][] body)
    {
        var payload = Concat(body);
        var size = (uint)payload.Length | (container ? ContainerBit : 0u);
        return [.. BitConverter.GetBytes(type), .. BitConverter.GetBytes(size), .. payload];
    }

    /// <summary>A mini-chunk: one type byte, one size byte, then the body.</summary>
    public static byte[] Mini(byte type, params byte[][] body)
    {
        var payload = Concat(body);
        return [type, checked((byte)payload.Length), .. payload];
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var result = new List<byte>();
        foreach (var p in parts) result.AddRange(p);
        return [.. result];
    }

    /// <summary>ASCII with the trailing NUL the exporter writes.</summary>
    public static byte[] Str(string value)
    {
        return [.. Encoding.ASCII.GetBytes(value), 0x00];
    }

    public static byte[] I32(int value)
    {
        return BitConverter.GetBytes(value);
    }

    public static byte[] U32(uint value)
    {
        return BitConverter.GetBytes(value);
    }

    public static byte[] F32(params float[] values)
    {
        var result = new List<byte>(values.Length * 4);
        foreach (var v in values) result.AddRange(BitConverter.GetBytes(v));
        return [.. result];
    }

    public static byte[] Zeros(int count)
    {
        return new byte[count];
    }

    // ── skeleton (0x200) ──────────────────────────────────────────────────────

    /// <summary>
    ///     A skeleton block. The bone-count chunk is a fixed 128 bytes of which only the leading
    ///     <c>uint32</c> is meaningful; the exporter pads the rest, and the reader must skip exactly
    ///     124 bytes rather than assuming the chunk ends after the count.
    /// </summary>
    public static byte[] Skeleton(params byte[][] bones)
    {
        return Chunk(0x200, true,
            Chunk(0x201, false, U32((uint)bones.Length), Zeros(124)),
            Concat(bones));
    }

    /// <summary>
    ///     One bone. <paramref name="billboard" /> non-null selects the <c>0x206</c> variant, which
    ///     carries an extra billboard-type integer ahead of the transform; <c>0x205</c> is the older
    ///     form without it.
    /// </summary>
    /// <param name="transform">
    ///     The 12 floats as stored: three rows of four, the fourth column of each row being that
    ///     axis's translation component.
    /// </param>
    public static byte[] Bone(
        string name, int parent, bool visible, float[] transform, int? billboard = null)
    {
        if (transform.Length != 12)
            throw new ArgumentException("A bone transform is 12 floats.", nameof(transform));

        var data = billboard is null
            ? Chunk(0x205, false, I32(parent), I32(visible ? 1 : 0), F32(transform))
            : Chunk(0x206, false,
                I32(parent), I32(visible ? 1 : 0), I32(billboard.Value), F32(transform));

        return Chunk(0x202, true, Chunk(0x203, false, Str(name)), data);
    }

    /// <summary>The 12 stored floats for a pure translation.</summary>
    public static float[] Translation(float x, float y, float z)
    {
        return [1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z];
    }

    // ── mesh (0x400) ──────────────────────────────────────────────────────────

    /// <summary>
    ///     A mesh block: name, the padded info record, then one <c>0x10100</c>/<c>0x10000</c> pair per
    ///     sub-mesh. That child order (<c>0x401, 0x402, 0x10100, 0x10000</c>) is what all 3217 sampled
    ///     meshes in the shipped trees use.
    /// </summary>
    /// <param name="hidden">
    ///     The stored field, which is INVERTED: zero means the mesh is visible. Named for what the
    ///     file holds rather than what it means, so the fixture cannot quietly disagree with the file.
    /// </param>
    public static byte[] Mesh(
        string name,
        byte[][] subMeshes,
        Vector3 min = default,
        Vector3 max = default,
        int hidden = 0,
        bool collidable = false)
    {
        return Chunk(0x400, true,
            Chunk(0x401, false, Str(name)),
            MeshInfo(subMeshes.Length, min, max, hidden, collidable),
            Concat(subMeshes));
    }

    /// <summary>
    ///     The mesh info record. Only the leading 40 bytes are meaningful; the exporter pads the chunk
    ///     to a fixed 128, which is why a reader must trust the chunk length rather than the fields.
    /// </summary>
    private static byte[] MeshInfo(int subMeshCount, Vector3 min, Vector3 max, int hidden, bool collidable)
    {
        return Chunk(0x402, false,
            I32(subMeshCount),
            F32(min.X, min.Y, min.Z),
            F32(max.X, max.Y, max.Z),
            I32(0), // unknown; zero in every sampled mesh
            I32(hidden),
            I32(collidable ? 1 : 0),
            Zeros(88));
    }

    /// <summary>
    ///     One sub-mesh, which is the sibling PAIR the format actually uses: a <c>0x10100</c> material
    ///     block followed by a <c>0x10000</c> data block.
    /// </summary>
    public static byte[] SubMesh(
        string shader,
        string vertexFormat,
        byte[][] vertices,
        ushort[] indices,
        byte[][]? parameters = null,
        int[]? skinBones = null,
        bool collisionTree = false,
        bool oldVertexFormat = false)
    {
        var indexBytes = new List<byte>(indices.Length * 2);
        foreach (var i in indices) indexBytes.AddRange(BitConverter.GetBytes(i));

        var data = new List<byte[]>
        {
            // Vertex and face counts, in that order. Faces, not indices - the index chunk holds
            // three per face.
            Chunk(0x10001, false, U32((uint)vertices.Length), U32((uint)(indices.Length / 3))),
            Chunk(0x10002, false, Str(vertexFormat)),
            Chunk(oldVertexFormat ? 0x10005u : 0x10007u, false, Concat(vertices)),
            Chunk(0x10004, false, [.. indexBytes])
        };

        if (skinBones is not null)
            data.Add(Chunk(0x10006, false, Concat([.. skinBones.Select(I32)])));

        // The collision tree is present on about a third of shipped sub-meshes and is not needed for
        // display, so the reader skips it - but it must skip it rather than trip over it.
        if (collisionTree)
            data.Add(Chunk(0x1200, false, Zeros(16)));

        return Concat(
            Chunk(0x10100, true, Chunk(0x10101, false, Str(shader)), Concat(parameters ?? [])),
            Chunk(0x10000, true, Concat([.. data])));
    }

    // ── shader parameters (0x10102 - 0x10106) ─────────────────────────────────
    //
    // Each is a container of two mini-chunks: 1 = name, 2 = value. The chunk TYPE carries the value's
    // type, which is why there is one builder per type rather than a type field.

    public static byte[] IntParam(string name, int value)
    {
        return Chunk(0x10102, true, Mini(1, Str(name)), Mini(2, I32(value)));
    }

    public static byte[] FloatParam(string name, float value)
    {
        return Chunk(0x10103, true, Mini(1, Str(name)), Mini(2, F32(value)));
    }

    public static byte[] Float3Param(string name, Vector3 value)
    {
        return Chunk(0x10104, true, Mini(1, Str(name)), Mini(2, F32(value.X, value.Y, value.Z)));
    }

    public static byte[] TextureParam(string name, string texture)
    {
        return Chunk(0x10105, true, Mini(1, Str(name)), Mini(2, Str(texture)));
    }

    public static byte[] Float4Param(string name, Vector4 value)
    {
        return Chunk(0x10106, true,
            Mini(1, Str(name)), Mini(2, F32(value.X, value.Y, value.Z, value.W)));
    }

    // ── vertices ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     One <c>MASTER_VERTEX</c>: 144 bytes, which is the stride every one of the 3443 sampled
    ///     <c>0x10007</c> chunks divides by exactly.
    /// </summary>
    /// <remarks>
    ///     Layout, by offset: position 0, normal 12, four UV pairs 24, tangent 56, binormal 68,
    ///     colour 80 (four floats), an unused float4 at 96, four bone indices at 112 and four bone
    ///     weights at 128. UV sets 2 and 3 were zero across all 102712 vertices sampled from the
    ///     shipped trees, and nothing binds the field at 96 - it nonetheless holds data, so it is
    ///     skipped rather than asserted to be zero.
    /// </remarks>
    public static byte[] MasterVertex(
        Vector3 position,
        Vector3 normal = default,
        Vector2 uv0 = default,
        Vector2 uv1 = default,
        Vector3 tangent = default,
        Vector3 binormal = default,
        Vector4? color = null,
        uint[]? boneIndices = null,
        float[]? boneWeights = null)
    {
        var c = color ?? Vector4.One;
        var bi = boneIndices ?? [0, 0, 0, 0];
        var bw = boneWeights ?? [1, 0, 0, 0];

        return Concat(
            F32(position.X, position.Y, position.Z),
            F32(normal.X, normal.Y, normal.Z),
            F32(uv0.X, uv0.Y),
            F32(uv1.X, uv1.Y),
            F32(0, 0), // UV set 2 - always zero in the shipped trees
            F32(0, 0), // UV set 3 - likewise
            F32(tangent.X, tangent.Y, tangent.Z),
            F32(binormal.X, binormal.Y, binormal.Z),
            F32(c.X, c.Y, c.Z, c.W),
            F32(0, 0, 0, 0), // bound by no vertex declaration
            Concat([.. bi.Select(U32)]),
            F32(bw));
    }

    // ── lights (0x1300) ───────────────────────────────────────────────────────

    /// <summary>
    ///     A light block. The info record is exactly 36 bytes on all 246 lights in the shipped trees:
    ///     a type int, an RGB colour, then five floats.
    /// </summary>
    public static byte[] Light(
        string name,
        int type = 0,
        Vector3 color = default,
        float intensity = 1f,
        float farAttenuationEnd = 0f,
        float farAttenuationStart = 0f,
        float hotspotSize = 0f,
        float falloffSize = 0f)
    {
        return Chunk(0x1300, true,
            Chunk(0x1301, false, Str(name)),
            Chunk(0x1302, false,
                I32(type),
                F32(color.X, color.Y, color.Z),
                F32(intensity, farAttenuationEnd, farAttenuationStart, hotspotSize, falloffSize)));
    }

    // ── connections (0x600) ───────────────────────────────────────────────────

    /// <summary>
    ///     The connections block: a header counting each following list, then the lists themselves.
    /// </summary>
    /// <remarks>
    ///     The dazzle count (mini 9) is optional and was added for Universe at War. Exactly one model
    ///     across both shipped trees even carries the field, and no model carries a dazzle - so the
    ///     count is written here only when asked for.
    /// </remarks>
    public static byte[] Connections(
        byte[][]? connections = null, byte[][]? proxies = null, byte[][]? dazzles = null)
    {
        var conns = connections ?? [];
        var prox = proxies ?? [];
        var daz = dazzles ?? [];

        var header = daz.Length > 0
            ? Chunk(0x601, true, Mini(1, I32(conns.Length)), Mini(4, I32(prox.Length)),
                Mini(9, I32(daz.Length)))
            : Chunk(0x601, true, Mini(1, I32(conns.Length)), Mini(4, I32(prox.Length)));

        return Chunk(0x600, true, header, Concat(conns), Concat(prox), Concat(daz));
    }

    /// <summary>
    ///     Attaches one attachable to a bone.
    /// </summary>
    /// <param name="objectIndex">
    ///     Index into the meshes AND lights of the file, in the order they appear at top level - not
    ///     into the meshes alone.
    /// </param>
    public static byte[] Connection(int objectIndex, int boneIndex)
    {
        return Chunk(0x602, true, Mini(2, I32(objectIndex)), Mini(3, I32(boneIndex)));
    }

    /// <summary>
    ///     A proxy - the attachment point a particle system or light field hangs from.
    /// </summary>
    /// <param name="hidden">
    ///     Mini-chunk 7, and INVERTED like the mesh flag: zero means visible. Omitted when null, which
    ///     is what 6399 of the 9412 shipped proxies do.
    /// </param>
    /// <param name="altDecreaseStayHidden">
    ///     Mini-chunk 8: the proxy stays hidden while the ALT level is decreasing, which is what keeps
    ///     damage effects from flickering back on as a unit is repaired.
    /// </param>
    public static byte[] Proxy(
        string name, int boneIndex, int? hidden = null, bool? altDecreaseStayHidden = null)
    {
        var parts = new List<byte[]> { Mini(5, Str(name)), Mini(6, I32(boneIndex)) };
        if (hidden is not null) parts.Add(Mini(7, I32(hidden.Value)));
        if (altDecreaseStayHidden is not null)
            parts.Add(Mini(8, I32(altDecreaseStayHidden.Value ? 1 : 0)));

        return Chunk(0x603, true, Concat([.. parts]));
    }

    /// <summary>
    ///     The older 128-byte vertex (<c>0x10005</c>), which is the master vertex without the unused
    ///     float4. Still present on 50 of the sampled sub-meshes, so it is not dead format.
    /// </summary>
    public static byte[] OldVertex(
        Vector3 position,
        Vector3 normal = default,
        Vector2 uv0 = default,
        Vector4? color = null)
    {
        var c = color ?? Vector4.One;

        return Concat(
            F32(position.X, position.Y, position.Z),
            F32(normal.X, normal.Y, normal.Z),
            F32(uv0.X, uv0.Y),
            F32(0, 0), F32(0, 0), F32(0, 0),
            F32(0, 0, 0), // tangent
            F32(0, 0, 0), // binormal
            F32(c.X, c.Y, c.Z, c.W),
            Concat([.. new uint[] { 0, 0, 0, 0 }.Select(U32)]),
            F32(1, 0, 0, 0));
    }
}
