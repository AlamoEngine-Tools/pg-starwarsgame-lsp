// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.InlayHints;

/// <summary>
///     Hardpoint bones do not all target the same model. The inlay must show the mounting hull for
///     parent-side bones (<c>Attachment_Bone</c> etc.) and the hardpoint's own <c>Model_To_Attach</c> only
///     for turret-side bones - never blindly the sibling <c>Model_To_Attach</c>, which was the reported bug.
/// </summary>
public sealed class BoneModelInlayHintProviderHardpointTest
{
    private const string HpUri = "file:///hardpoints.xml";
    private const string ObjUri = "file:///objects.xml";

    [Fact]
    public void TurretBone_ShowsModelToAttach()
    {
        const string hp =
            "<X><HardPoint Name=\"HP_A\"><Is_Turret>Yes</Is_Turret>" +
            "<Model_To_Attach>turret.alo</Model_To_Attach>" +
            "<Turret_Bone_Name>Turret_Root</Turret_Bone_Name></HardPoint></X>";
        var index = Index(
            bones: Bones(("turret.alo", ["Turret_Root"])),
            mounts: []); // turret bones need no mounting object
        var hint = Single(hp, "//turret_bone_name", index, new TagSource());

        Assert.Equal("turret.alo::", hint);
    }

    [Fact]
    public void ParentBone_ShowsMountingHull_NotModelToAttach()
    {
        // Attachment_Bone lives on the mounting hull. The bone also happens to exist in Model_To_Attach
        // (the turret's root is the attach point), which is exactly what made the old resolver show the
        // wrong model. It must show the hull.
        const string hp =
            "<X><HardPoint Name=\"HP_A\"><Model_To_Attach>turret.alo</Model_To_Attach>" +
            "<Attachment_Bone>HP_Bone</Attachment_Bone></HardPoint></X>";
        var source = new TagSource()
            .With("PALACE", ("Land_Model_Name", "hull.alo"), ("HardPoints", "HP_A"));
        var index = Index(
            bones: Bones(("hull.alo", ["HP_Bone", "Root"]), ("turret.alo", ["HP_Bone"])),
            mounts: [("HP_A", "PALACE")]);

        var hint = Single(hp, "//attachment_bone", index, source);

        Assert.Equal("hull.alo::", hint);
    }

    [Fact]
    public void ParentBone_ValueOnlyInModelToAttach_StaysSilent()
    {
        // The reported bug: a parent bone that resolves ONLY in Model_To_Attach must NOT be annotated
        // with it - Model_To_Attach is not a valid target for a parent-side bone.
        const string hp =
            "<X><HardPoint Name=\"HP_A\"><Model_To_Attach>turret.alo</Model_To_Attach>" +
            "<Attachment_Bone>Turret_Root</Attachment_Bone></HardPoint></X>";
        var source = new TagSource()
            .With("PALACE", ("Land_Model_Name", "hull.alo"), ("HardPoints", "HP_A"));
        var index = Index(
            bones: Bones(("hull.alo", ["Root"]), ("turret.alo", ["Turret_Root"])),
            mounts: [("HP_A", "PALACE")]);

        Assert.Empty(Hints(hp, "//attachment_bone", index, source));
    }

    [Fact]
    public void CollisionMesh_OnAttachedModel_ShowsAttachedModel()
    {
        // Collision_Mesh resolves against hull UNION Model_To_Attach; the mesh lives on the attached
        // weapon model here (as it does for every Star Destroyer / Nebulon weapon), so the hint names it.
        const string hp =
            "<X><HardPoint Name=\"HP_A\"><Model_To_Attach>weapon.alo</Model_To_Attach>" +
            "<Collision_Mesh>HP_Coll</Collision_Mesh></HardPoint></X>";
        var source = new TagSource()
            .With("PALACE", ("Land_Model_Name", "hull.alo"), ("HardPoints", "HP_A"));
        var index = Index(
            bones: Bones(("hull.alo", ["Root"]), ("weapon.alo", ["HP_Coll"])),
            mounts: [("HP_A", "PALACE")]);

        var hint = Single(hp, "//collision_mesh", index, source);

        Assert.Equal("weapon.alo::", hint);
    }

    [Fact]
    public void ParentBone_CumulativeMounts_ListsModelsCapped()
    {
        // The same hardpoint is mounted on three stations with three hulls; the bone resolves on all.
        const string hp =
            "<X><HardPoint Name=\"HP_A\"><Attachment_Bone>HP01_COM_BONE</Attachment_Bone></HardPoint></X>";
        var source = new TagSource()
            .With("UB1", ("Space_Model_Name", "ub_01_station.alo"), ("HardPoints", "HP_A"))
            .With("UB2", ("Space_Model_Name", "ub_02_station.alo"), ("HardPoints", "HP_A"))
            .With("UB3", ("Space_Model_Name", "ub_03_station.alo"), ("HardPoints", "HP_A"));
        var index = Index(
            bones: Bones(
                ("ub_01_station.alo", ["HP01_COM_BONE"]),
                ("ub_02_station.alo", ["HP01_COM_BONE"]),
                ("ub_03_station.alo", ["HP01_COM_BONE"])),
            mounts: [("HP_A", "UB1"), ("HP_A", "UB2"), ("HP_A", "UB3")]);

        var hint = Single(hp, "//attachment_bone", index, source);

        Assert.Equal("ub_01_station.alo, ub_02_station.alo, +1::", hint);
    }

    [Fact]
    public void ParentBone_ResolvesOnSomeMountsOnly_ListsOnlyThose()
    {
        // The class-1 cumulative case: a lower-tier bone stripped from the larger station meshes. Only
        // the hulls that still carry it are named.
        const string hp =
            "<X><HardPoint Name=\"HP_A\"><Attachment_Bone>HP01_SHG_COLL</Attachment_Bone></HardPoint></X>";
        var source = new TagSource()
            .With("UB1", ("Space_Model_Name", "ub_01_station.alo"), ("HardPoints", "HP_A"))
            .With("UB5", ("Space_Model_Name", "ub_05_station.alo"), ("HardPoints", "HP_A"));
        var index = Index(
            bones: Bones(
                ("ub_01_station.alo", ["HP01_SHG_COLL"]),
                ("ub_05_station.alo", ["Root"])), // stripped on the big mesh
            mounts: [("HP_A", "UB1"), ("HP_A", "UB5")]);

        var hint = Single(hp, "//attachment_bone", index, source);

        Assert.Equal("ub_01_station.alo::", hint);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string Single(string xml, string xpath, GameIndex index, IVariantTagSource source)
    {
        var hint = Assert.Single(Hints(xml, xpath, index, source));
        return hint.Label.String!;
    }

    private static IReadOnlyList<OmniSharp.Extensions.LanguageServer.Protocol.Models.InlayHint> Hints(
        string xml, string xpath, GameIndex index, IVariantTagSource source)
    {
        var hapDoc = new HtmlDocument();
        hapDoc.LoadHtml(xml);
        var node = hapDoc.DocumentNode.SelectSingleNode(xpath)!;
        var tagDef = new XmlTagDefinition
            { Tag = node.Name, ValueType = XmlValueType.NameReference, ReferenceKind = ReferenceKind.BoneName };
        var ctx = new InlayHintContext(HpUri, index, new EmptySchemaProvider(), hapDoc, node, tagDef,
            XmlUtility.GetLine(node), lineIndex: new LineOffsetIndex(xml));
        return new BoneModelInlayHintProvider(source).Handle(ctx).ToList();
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> Bones(
        params (string Key, string[] Names)[] entries)
    {
        var b = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, names) in entries)
            b[key] = names.ToImmutableArray();
        return b.ToImmutable();
    }

    // Builds an index where each (hardpointId, ownerId) mount is reachable through WorkspaceReferences +
    // the owner's indexed document, mirroring how the workspace indexer wires cross-file mounting.
    private static GameIndex Index(
        ImmutableDictionary<string, ImmutableArray<string>> bones,
        (string Hardpoint, string Owner)[] mounts)
    {
        var ownerSymbols = mounts.Select(m => m.Owner).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(o => new GameSymbol(o, GameSymbolKind.XmlObject, "SpaceUnit",
                new FileOrigin(ObjUri, 0, 0), null, null))
            .ToList();

        var references = mounts
            .GroupBy(m => m.Hardpoint, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(
                g => g.Key,
                g => ImmutableArray.Create(
                    new GameReference(g.Key, GameSymbolKind.XmlObject, "HardPoint", ObjUri, 0, 0, 0)),
                StringComparer.OrdinalIgnoreCase);

        var documents = ImmutableDictionary<string, DocumentIndex>.Empty;
        if (ownerSymbols.Count > 0)
            documents = documents.Add(ObjUri,
                new DocumentIndex(ObjUri, 1, ownerSymbols.ToImmutableArray(), ImmutableArray<GameReference>.Empty));

        // The effective-object resolver looks the owner up by id, so it must be a workspace definition.
        var definitions = ownerSymbols.ToImmutableDictionary(
            s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase);

        return GameIndex.Empty with
        {
            Documents = documents,
            WorkspaceDefinitions = definitions,
            WorkspaceReferences = references,
            ModelBones = bones
        };
    }

    private sealed class TagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId) => _byId.GetValueOrDefault(objectId);

        public TagSource With(string id, params (string Tag, string Value)[] tags)
        {
            _byId[id] = tags.Select(t => new VariantTag(t.Tag, t.Value, "", 0)).ToList();
            return this;
        }
    }
}
