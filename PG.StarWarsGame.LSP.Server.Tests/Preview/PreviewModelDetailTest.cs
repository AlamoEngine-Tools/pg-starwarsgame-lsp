// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The facts the inspector cannot read out of the glTF.
/// </summary>
/// <remarks>
///     Bone transforms and mesh bounds have to come from here rather than from the client, because
///     the client only holds the Y-up rotated, split-apart skeleton the exporter built. Printing
///     three.js matrices would show an author numbers that appear nowhere in their file - which is
///     worse than showing nothing.
/// </remarks>
public sealed class PreviewModelDetailTest
{
    private static AlamoModelBone Bone(
        int index, string name, int parent, Matrix4x4? relative = null,
        AlamoBillboardType billboard = AlamoBillboardType.Disable, bool visible = true)
    {
        var rel = relative ?? Matrix4x4.Identity;

        return new AlamoModelBone(index, name, parent, visible, billboard, rel, rel);
    }

    private static AlamoMesh Mesh(string name, int bone, int? alt = null, int? lod = null)
    {
        return new AlamoMesh(
            0, name,
            new AlamoBoundingBox(new Vector3(-1, -2, -3), new Vector3(4, 5, 6)),
            true, true, alt, lod, [])
        { BoneIndex = bone };
    }

    private static AlamoModelContent Model(
        IReadOnlyList<AlamoModelBone>? bones = null,
        IReadOnlyList<AlamoMesh>? meshes = null,
        IReadOnlyList<AlamoProxy>? proxies = null,
        IReadOnlyList<AlamoLight>? lights = null)
    {
        return new AlamoModelContent(
            bones ?? [Bone(0, "Root", -1)], meshes ?? [], lights ?? [], proxies ?? [], []);
    }

    [Fact]
    public void Bones_CarryIndexParentAndVisibility()
    {
        var detail = PreviewModelDetail.From("Ev_test", Model([
            Bone(0, "Root", -1),
            Bone(1, "HP_F-L", 0, visible: false),
        ]));

        Assert.Equal(2, detail.Bones.Count);
        Assert.Equal("HP_F-L", detail.Bones[1].Name);
        Assert.Equal(0, detail.Bones[1].ParentIndex);
        Assert.False(detail.Bones[1].Visible);
    }

    [Fact]
    public void Bones_SendBothTransformsAsSixteenFloats()
    {
        // Four rows of four, the way AloViewer's own bone panel shows them - and the way the author
        // recognises them, with the translation as the last row.
        var relative = Matrix4x4.CreateTranslation(new Vector3(10, 20, 30));
        var detail = PreviewModelDetail.From("Ev_test", Model([Bone(0, "Root", -1, relative)]));

        Assert.Equal(16, detail.Bones[0].RelativeTransform.Count);
        Assert.Equal(16, detail.Bones[0].AbsoluteTransform.Count);
        Assert.Equal([10f, 20f, 30f, 1f], detail.Bones[0].RelativeTransform.Skip(12).ToArray());
    }

    [Fact]
    public void Bones_ReportABillboardModeByName()
    {
        // Never the ordinal: the wire has already cost one bug where an enum crossed as a number and
        // the client read it as a name.
        var detail = PreviewModelDetail.From("Ev_test", Model([
            Bone(0, "Root", -1, billboard: AlamoBillboardType.ZAxisView),
        ]));

        Assert.Equal("ZAxisView", detail.Bones[0].Billboard);
    }

    [Fact]
    public void Meshes_CarryTheBoundsTheFileStores()
    {
        var detail = PreviewModelDetail.From("Ev_test", Model(meshes: [Mesh("hull", 0)]));

        Assert.Equal([-1f, -2f, -3f], detail.Meshes[0].BoundsMin);
        Assert.Equal([4f, 5f, 6f], detail.Meshes[0].BoundsMax);
    }

    [Fact]
    public void Meshes_KeepAnUntaggedLevelNull()
    {
        // "No ALT declared" and "ALT 0" are different statements about the file, and defaulting one
        // to the other would tell an author they pinned a mesh to the undamaged state.
        var detail = PreviewModelDetail.From("Ev_test", Model(meshes: [
            Mesh("hull", 0),
            Mesh("hull_ALT2", 0, alt: 2, lod: 1),
        ]));

        Assert.Null(detail.Meshes[0].Alt);
        Assert.Null(detail.Meshes[0].Lod);
        Assert.Equal(2, detail.Meshes[1].Alt);
        Assert.Equal(1, detail.Meshes[1].Lod);
    }

    [Fact]
    public void Proxies_CarryTheRepairAsymmetryFlag()
    {
        // `altDecreaseStayHidden` exists nowhere else in the client: the exporter does not write
        // proxies into the glTF at all, so without this the flag is unreadable.
        var detail = PreviewModelDetail.From("Ev_test", Model(proxies: [
            new AlamoProxy("p_smoke", 3, true, true, 2, null),
        ]));

        var proxy = Assert.Single(detail.Proxies);
        Assert.Equal("p_smoke", proxy.Name);
        Assert.Equal(3, proxy.BoneIndex);
        Assert.True(proxy.AltDecreaseStayHidden);
        Assert.Equal(2, proxy.Alt);
    }

    [Fact]
    public void Lights_TravelAsACountAndNothingMore()
    {
        // Measured: they are 3ds Max export residue, and no shipped effect declares a point or spot
        // uniform that could consume one. The count exists only so the panel can say so.
        var detail = PreviewModelDetail.From("Ev_test", Model(lights: [
            new AlamoLight("Spot01", AlamoLightType.Spot, Vector3.One, 1, 200, 80, 0.75f, 0.78f),
        ]));

        Assert.Equal(1, detail.LightCount);
    }

    [Fact]
    public void Model_NamesItselfSoAStaleAnswerCanBeSpotted()
    {
        // Replies arrive out of order and a preview can change subject while one is in flight.
        Assert.Equal("Ev_test", PreviewModelDetail.From("Ev_test", Model()).Model);
    }

    [Fact]
    public void TheWire_UsesTheFieldNamesTheTsMirrorReads()
    {
        // The TS mirror is hand-written, so nothing but this stops the two drifting. A renamed field
        // does not fail loudly here - it arrives as undefined and the panel shows blank rows, which
        // reads as a model with no bones rather than as a protocol mistake.
        var detail = PreviewModelDetail.From("Ev_test", Model(
            [Bone(1, "HP_F-L", 0, Matrix4x4.CreateTranslation(new Vector3(1, 2, 3)))],
            [Mesh("hull", 1)],
            [new AlamoProxy("p_smoke", 1, true, true, null, null)]));

        var json = JObject.FromObject(detail, PreviewSerialization.Create().JsonSerializer);

        Assert.Equal("Ev_test", json["model"]?.Value<string>());
        Assert.Equal("HP_F-L", json["bones"]?[0]?["name"]?.Value<string>());
        Assert.Equal(0, json["bones"]?[0]?["parentIndex"]?.Value<int>());
        Assert.Equal("Disable", json["bones"]?[0]?["billboard"]?.Value<string>());
        Assert.Equal(16, json["bones"]?[0]?["relativeTransform"]?.Count());
        Assert.Equal(16, json["bones"]?[0]?["absoluteTransform"]?.Count());
        Assert.Equal("hull", json["meshes"]?[0]?["name"]?.Value<string>());
        Assert.Equal(3, json["meshes"]?[0]?["boundsMin"]?.Count());
        Assert.Equal("p_smoke", json["proxies"]?[0]?["name"]?.Value<string>());
        Assert.True(json["proxies"]?[0]?["altDecreaseStayHidden"]?.Value<bool>());
        Assert.Equal(0, json["lightCount"]?.Value<int>());
    }
}
