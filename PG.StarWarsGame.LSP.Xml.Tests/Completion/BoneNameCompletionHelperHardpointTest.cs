// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

/// <summary>
///     Bone-name completion inside a <c>HardPoint</c> must offer the bones of the model the tag actually
///     resolves against - the mounting hull for parent bones, the attached model for turret bones, and the
///     union for <c>Collision_Mesh</c> - not blindly the sibling <c>Model_To_Attach</c>. This is the fix
///     for completion contradicting the hardpoint validator.
/// </summary>
public sealed class BoneNameCompletionHelperHardpointTest
{
    private const string ObjUri = "file:///objects.xml";

    [Fact]
    public void AttachmentBone_OffersMountingHullBones_NotModelToAttach()
    {
        const string xml =
            "<X><HardPoint Name=\"HP_A\"><Model_To_Attach>weapon.alo</Model_To_Attach>" +
            "<Attachment_Bone></Attachment_Bone></HardPoint></X>";
        var index = Index(
            Bones(("hull.alo", ["HULL_MOUNT"]), ("weapon.alo", ["WEAPON_ROOT"])),
            [("HP_A", "PALACE")]);
        var source = new TagSource().With("PALACE", ("Land_Model_Name", "hull.alo"), ("HardPoints", "HP_A"));

        var labels = Labels(xml, "//attachment_bone", index, source);

        Assert.Contains("HULL_MOUNT", labels);
        Assert.DoesNotContain("WEAPON_ROOT", labels);
    }

    [Fact]
    public void TurretBone_OffersModelToAttachBones()
    {
        const string xml =
            "<X><HardPoint Name=\"HP_A\"><Model_To_Attach>weapon.alo</Model_To_Attach>" +
            "<Turret_Bone_Name></Turret_Bone_Name></HardPoint></X>";
        var index = Index(
            Bones(("hull.alo", ["HULL_MOUNT"]), ("weapon.alo", ["WEAPON_ROOT"])),
            [("HP_A", "PALACE")]);
        var source = new TagSource().With("PALACE", ("Land_Model_Name", "hull.alo"), ("HardPoints", "HP_A"));

        var labels = Labels(xml, "//turret_bone_name", index, source);

        Assert.Contains("WEAPON_ROOT", labels);
        Assert.DoesNotContain("HULL_MOUNT", labels);
    }

    [Fact]
    public void CollisionMesh_OffersHullAndAttachedUnion()
    {
        const string xml =
            "<X><HardPoint Name=\"HP_A\"><Model_To_Attach>weapon.alo</Model_To_Attach>" +
            "<Collision_Mesh></Collision_Mesh></HardPoint></X>";
        var index = Index(
            Bones(("hull.alo", ["HULL_COLL"]), ("weapon.alo", ["WEAPON_COLL"])),
            [("HP_A", "PALACE")]);
        var source = new TagSource().With("PALACE", ("Land_Model_Name", "hull.alo"), ("HardPoints", "HP_A"));

        var labels = Labels(xml, "//collision_mesh", index, source);

        Assert.Contains("HULL_COLL", labels);
        Assert.Contains("WEAPON_COLL", labels);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> Labels(string xml, string xpath, GameIndex index, IVariantTagSource source)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(xml);
        var node = doc.DocumentNode.SelectSingleNode(xpath)!;
        return new BoneNameCompletionHelper(new EmptySchema(), source)
            .GetProposals(node, "", index).Select(p => p.Label).ToList();
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> Bones(
        params (string Key, string[] Names)[] entries)
    {
        var b = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, names) in entries)
            b[key] = names.ToImmutableArray();
        return b.ToImmutable();
    }

    private static GameIndex Index(
        ImmutableDictionary<string, ImmutableArray<string>> bones,
        (string Hardpoint, string Owner)[] mounts)
    {
        var owners = mounts.Select(m => m.Owner).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(o => new GameSymbol(o, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(ObjUri, 0, 0), null, null))
            .ToList();

        var references = mounts
            .GroupBy(m => m.Hardpoint, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(g => g.Key,
                g => ImmutableArray.Create(new GameReference(g.Key, GameSymbolKind.XmlObject, "HardPoint", ObjUri, 0, 0, 0)),
                StringComparer.OrdinalIgnoreCase);

        return GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(ObjUri,
                new DocumentIndex(ObjUri, 1, owners.ToImmutableArray(), ImmutableArray<GameReference>.Empty)),
            WorkspaceDefinitions = owners.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
                StringComparer.OrdinalIgnoreCase),
            WorkspaceReferences = references,
            ModelBones = bones
        };
    }

    private sealed class TagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<VariantTag>? TryGetTags(string objectId) => _byId.GetValueOrDefault(objectId);

        public TagSource With(string id, params (string Tag, string Value)[] tags)
        {
            _byId[id] = tags.Select(t => new VariantTag(t.Tag, t.Value, "", 0)).ToList();
            return this;
        }
    }

    private sealed class EmptySchema : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string tagName) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName) => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public GameObjectTypeDefinition? GetObjectType(string typeName) => null;
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName) => [];
        public EnumDefinition? GetEnum(string enumName) => null;
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}
