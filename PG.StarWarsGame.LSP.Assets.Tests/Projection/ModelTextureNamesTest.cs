// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Assets.Tests.Models;
using System.Numerics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

/// <summary>
///     The texture names a model carries inside itself, pulled from the bytes the bone pass has
///     already read.
/// </summary>
/// <remarks>
///     MEASURED 2026-09-30: parsing these lazily during validation was the whole of the workspace
///     sweep's cost - one prop file spent 17.2s of its 17.5s inside <c>TexturesOf</c>, while the
///     catalog lookups it was first blamed on were 30ms. Extracting them in the pass that already
///     opens every <c>.alo</c> is what makes them free.
/// </remarks>
public sealed class ModelTextureNamesTest
{
    [Fact]
    public void Read_NotAnAlo_IsEmptyRatherThanThrowing()
    {
        // The scan must never fail for one bad file; ModelFileFormat owns "this .alo is unreadable".
        Assert.Empty(ModelTextureNames.Read([1, 2, 3, 4]));
    }

    [Fact]
    public void Read_EmptyBytes_IsEmpty()
    {
        Assert.Empty(ModelTextureNames.Read([]));
    }

    /// <summary>
    ///     #292: the shipped particle systems name their sprite sheets by the art machine's absolute
    ///     path, and the game opens the part after the last backslash. The names come out as WRITTEN:
    ///     every reader cuts them itself, and the XML check can only say a texture is named by a path
    ///     while the path is still there to see.
    /// </summary>
    [Fact]
    public void Read_KeepsATextureNamedByAnAbsolutePath_AsWritten()
    {
        var system = AloParticleReaderTest.System("p_engines", null,
            AloParticleReaderTest.Emitter(colorTexture: @"C:\Projects\Starwars\Art\Textures\Game\p_particle_master.tga",
                normalTexture: @"C:\Projects\Starwars\Art\Textures\Game\p_particle_depth_master.tga"));

        Assert.Equal(
        [
            @"C:\Projects\Starwars\Art\Textures\Game\p_particle_master.tga",
            @"C:\Projects\Starwars\Art\Textures\Game\p_particle_depth_master.tga"
        ], ModelTextureNames.Read(system));
    }

    [Fact]
    public void Read_ClassifiesBeforeReading()
    {
        // A particle system and a model are both .alo and are told apart by their first chunk.
        // Reading one as the other throws rather than lying, so the classify must come first -
        // this is the same rule the 3D preview follows.
        Assert.Equal(AloFileKind.Unknown, AloFile.Classify([0, 0, 0, 0]));
        Assert.Empty(ModelTextureNames.Read([0, 0, 0, 0]));
    }

    // The kind comes off the root chunk alone (#270): a particle system is known as one even when
    // its emitters cannot be read, so the model list can leave it out without parsing anything.
    [Theory]
    [InlineData(new byte[] { 0x00, 0x09, 0x00, 0x00 }, AloFileKind.Particle)]
    [InlineData(new byte[] { 0x00, 0x15, 0x00, 0x00 }, AloFileKind.Particle)]
    [InlineData(new byte[] { 0x00, 0x02, 0x00, 0x00 }, AloFileKind.Model)]
    [InlineData(new byte[] { 1, 2, 3, 4 }, AloFileKind.Unknown)]
    public void Scan_KnowsTheKind_EvenWhenTheTexturesCannotBeRead(byte[] bytes, AloFileKind kind)
    {
        var scan = ModelTextureNames.Scan(bytes);

        Assert.Equal(kind, scan.Kind);
        Assert.Empty(scan.Textures);
    }

    // The particle systems a model's proxies name come from the same read as its textures, so a
    // system can list the models that carry it without a second pass over every model (#270).
    [Fact]
    public void Scan_AModel_AlsoNamesItsProxies()
    {
        var alo = AloChunkFixture.Concat(
            AloChunkFixture.Skeleton(
                AloChunkFixture.Bone("ROOT", -1, true, AloChunkFixture.Translation(0, 0, 0)),
                AloChunkFixture.Bone("Engine_00", 0, true, AloChunkFixture.Translation(0, 0, 0))),
            AloChunkFixture.Connections(proxies: [AloChunkFixture.Proxy("P_Engine_Glow_ALT2", 1)]));

        var scan = ModelTextureNames.Scan(alo);

        Assert.Equal(AloFileKind.Model, scan.Kind);
        Assert.Equal(["P_Engine_Glow_ALT2"], scan.Proxies);
    }

    /// <summary>
    ///     The parts of a model with the two facts the cloak rule needs (#449): whether the file marks
    ///     each visible, and whether a mesh is drawn as a surface. A shadow volume is a mesh the game
    ///     uses and never shows; a proxy is no surface either.
    /// </summary>
    [Fact]
    public void Scan_AModel_NamesItsPartsWithTheirFileFlags()
    {
        var alo = AloChunkFixture.Concat(
            AloChunkFixture.Skeleton(
                AloChunkFixture.Bone("ROOT", -1, true, AloChunkFixture.Translation(0, 0, 0)),
                AloChunkFixture.Bone("Glow", 0, true, AloChunkFixture.Translation(0, 0, 0))),
            AloChunkFixture.Mesh("Body", [SubMesh("MeshGloss.fx")]),
            AloChunkFixture.Mesh("Shadow", [SubMesh("MeshShadowVolume.fx")]),
            AloChunkFixture.Mesh("Panel", [SubMesh("MeshAlpha.fx")], hidden: 1),
            AloChunkFixture.Connections(proxies:
            [
                AloChunkFixture.Proxy("p_glow", 1), AloChunkFixture.Proxy("p_off", 1, hidden: 1)
            ]));

        Assert.Equal(
        [
            new ModelPart("Body", false, true, true),
            new ModelPart("Shadow", false, true, false),
            new ModelPart("Panel", false, false, true),
            new ModelPart("p_glow", true, true, false),
            new ModelPart("p_off", true, false, false)
        ], ModelTextureNames.Scan(alo).Parts);
    }

    /// <summary>A mesh is a surface when ANY of its sub-meshes is: one visible pass draws it.</summary>
    [Fact]
    public void Scan_AMeshWithOneSurfaceSubMesh_IsASurface()
    {
        var alo = AloChunkFixture.Concat(
            AloChunkFixture.Skeleton(AloChunkFixture.Bone("ROOT", -1, true, AloChunkFixture.Translation(0, 0, 0))),
            AloChunkFixture.Mesh("Hull", [SubMesh("RSkinShadowVolume.fx"), SubMesh("RSkinGloss.fx")]),
            AloChunkFixture.Mesh("Hull_Collision", [SubMesh("MeshCollision.fx")]));

        Assert.Equal(
        [
            new ModelPart("Hull", false, true, true),
            new ModelPart("Hull_Collision", false, true, false)
        ], ModelTextureNames.Scan(alo).Parts);
    }

    [Fact]
    public void Scan_SomethingThatIsNoReadableModel_HasNoParts()
    {
        Assert.Null(ModelTextureNames.Scan([1, 2, 3, 4]).Parts);
        Assert.Null(ModelTextureNames.Scan([0x00, 0x09, 0x00, 0x00]).Parts);
    }

    private static byte[] SubMesh(string shader)
    {
        return AloChunkFixture.SubMesh(shader, "alD3dVertNU2",
            [
                AloChunkFixture.MasterVertex(new Vector3(0, 0, 0)), AloChunkFixture.MasterVertex(new Vector3(1, 0, 0)),
                AloChunkFixture.MasterVertex(new Vector3(0, 1, 0))
            ],
            [0, 1, 2]);
    }
}