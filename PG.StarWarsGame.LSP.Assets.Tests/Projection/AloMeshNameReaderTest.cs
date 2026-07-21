// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

// Exercises the deprecated hand-rolled ALO mesh-name reader (0x400 -> 0x401). This is a temporary
// shim to be dropped when PG.StarWarsGame.Files.ALO exposes AlamoModel.Meshes; the tests document the
// exact chunk shape it parses so the replacement can be verified against the same fixtures.
#pragma warning disable CS0618 // deliberately testing the obsolete shim
public sealed class AloMeshNameReaderTest
{
    private const uint Skeleton = 0x0200;
    private const uint Mesh = 0x0400;
    private const uint MeshName = 0x0401;
    private const uint SubMeshMaterial = 0x00010100;
    private const uint ContainerBit = 0x80000000;

    [Fact]
    public void ReadMeshNames_SingleMesh_ReturnsName()
    {
        var alo = Chunk(Mesh, container: true, NameChunk("01_Station"));

        Assert.Equal(["01_Station"], AloMeshNameReader.ReadMeshNames(alo));
    }

    [Fact]
    public void ReadMeshNames_MultipleMeshes_ReturnsAllInOrder()
    {
        var alo = Concat(
            Chunk(Mesh, container: true, NameChunk("01_Station")),
            Chunk(Mesh, container: true, NameChunk("HP01_SHG_Blast")),
            Chunk(Mesh, container: true, NameChunk("collision_com")));

        Assert.Equal(["01_Station", "HP01_SHG_Blast", "collision_com"],
            AloMeshNameReader.ReadMeshNames(alo));
    }

    [Fact]
    public void ReadMeshNames_IgnoresNonNameSubChunks()
    {
        // A real mesh chunk carries material/shader sub-chunks alongside the name; only 0x401 counts.
        var alo = Chunk(Mesh, container: true, Concat(
            NameChunk("HP02_LC_Blast"),
            Chunk(SubMeshMaterial, container: true, [0x01, 0x02, 0x03, 0x04])));

        Assert.Equal(["HP02_LC_Blast"], AloMeshNameReader.ReadMeshNames(alo));
    }

    [Fact]
    public void ReadMeshNames_SkeletonOnly_ReturnsEmpty()
    {
        var alo = Chunk(Skeleton, container: true, [0xAA, 0xBB, 0xCC, 0xDD]);

        Assert.Empty(AloMeshNameReader.ReadMeshNames(alo));
    }

    [Fact]
    public void ReadMeshNames_Garbage_ReturnsEmptyWithoutThrowing()
    {
        var garbage = new byte[] { 0x03, 0xFF, 0x11, 0x00, 0x7F, 0x42, 0x99, 0x01, 0x02 };

        Assert.Empty(AloMeshNameReader.ReadMeshNames(garbage));
    }

    [Fact]
    public void ReadMeshNames_Empty_ReturnsEmpty()
    {
        Assert.Empty(AloMeshNameReader.ReadMeshNames([]));
    }

    // ── fixture builders ──────────────────────────────────────────────────────

    private static byte[] NameChunk(string name)
    {
        // Mesh name chunk body is an ASCII string with a trailing NUL, as written by the exporter.
        var bytes = Encoding.ASCII.GetBytes(name);
        return Chunk(MeshName, container: false, [.. bytes, 0x00]);
    }

    private static byte[] Chunk(uint type, bool container, byte[] body)
    {
        var size = (uint)body.Length | (container ? ContainerBit : 0u);
        return [.. BitConverter.GetBytes(type), .. BitConverter.GetBytes(size), .. body];
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new List<byte>();
        foreach (var p in parts) result.AddRange(p);
        return [.. result];
    }
}
#pragma warning restore CS0618
