// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

public sealed class BoneNameCompletionHelperTest
{
    private static HtmlNode Node(string xml, string elementXPath)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(xml);
        return doc.DocumentNode.SelectSingleNode(elementXPath)!;
    }

    private static GameIndex IndexWithBones(params (string Path, string[] Bones)[] entries)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bones) in entries)
            builder[path] = bones.ToImmutableArray();
        return GameIndex.Empty with { ModelBones = builder.ToImmutable() };
    }

    private static FakeSchema SchemaWithModelTags(params string[] modelTagNames)
    {
        var schema = new FakeSchema();
        foreach (var name in modelTagNames)
            schema.Add(name, ReferenceKind.ModelFile);
        return schema;
    }

    // ── empty cases ───────────────────────────────────────────────────────────

    [Fact]
    public void GetProposals_EmptyIndex_ReturnsEmpty()
    {
        var helper = new BoneNameCompletionHelper(SchemaWithModelTags("Space_Model_Name"));
        var bone = Node(
            "<SpaceUnit><Space_Model_Name>unit.alo</Space_Model_Name><Barrel_Bone_Name></Barrel_Bone_Name></SpaceUnit>",
            "//barrel_bone_name");

        var result = helper.GetProposals(bone, "", GameIndex.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void GetProposals_NoModelSibling_ReturnsEmpty()
    {
        var helper = new BoneNameCompletionHelper(SchemaWithModelTags("Space_Model_Name"));
        var bone = Node(
            "<SpaceUnit><Barrel_Bone_Name></Barrel_Bone_Name></SpaceUnit>",
            "//barrel_bone_name");
        var index = IndexWithBones(("unit.alo", ["root", "turret"]));

        var result = helper.GetProposals(bone, "", index);

        Assert.Empty(result);
    }

    // ── direct parent sibling ─────────────────────────────────────────────────

    [Fact]
    public void GetProposals_SiblingModelTagAtDirectParent_ReturnsBones()
    {
        var helper = new BoneNameCompletionHelper(SchemaWithModelTags("Space_Model_Name"));
        var bone = Node(
            "<SpaceUnit><Space_Model_Name>unit.alo</Space_Model_Name><Barrel_Bone_Name></Barrel_Bone_Name></SpaceUnit>",
            "//barrel_bone_name");
        var index = IndexWithBones(("unit.alo", ["root", "turret_bone"]));

        var result = helper.GetProposals(bone, "", index);

        var labels = result.Select(p => p.Label).ToList();
        Assert.Contains("root", labels);
        Assert.Contains("turret_bone", labels);
    }

    // ── model tag two levels up (HardPoint inside GameObjectType) ──────────────

    [Fact]
    public void GetProposals_SiblingModelTagTwoLevelsUp_ReturnsBones()
    {
        var helper = new BoneNameCompletionHelper(SchemaWithModelTags("Space_Model_Name"));
        const string xml =
            "<GameObjectType>" +
            "<Space_Model_Name>capital.alo</Space_Model_Name>" +
            "<HardPoint><Fire_Bone_Name></Fire_Bone_Name></HardPoint>" +
            "</GameObjectType>";
        var bone = Node(xml, "//fire_bone_name");
        var index = IndexWithBones(("capital.alo", ["hp_01", "hp_02"]));

        var result = helper.GetProposals(bone, "", index);

        var labels = result.Select(p => p.Label).ToList();
        Assert.Contains("hp_01", labels);
        Assert.Contains("hp_02", labels);
    }

    // ── prefix filtering ──────────────────────────────────────────────────────

    [Fact]
    public void GetProposals_PartialPrefix_FiltersCaseInsensitive()
    {
        var helper = new BoneNameCompletionHelper(SchemaWithModelTags("Space_Model_Name"));
        var bone = Node(
            "<SpaceUnit><Space_Model_Name>unit.alo</Space_Model_Name><Barrel_Bone_Name></Barrel_Bone_Name></SpaceUnit>",
            "//barrel_bone_name");
        var index = IndexWithBones(("unit.alo", ["root", "turret_bone", "turbo_bone"]));

        var result = helper.GetProposals(bone, "tur", index);

        var labels = result.Select(p => p.Label).ToList();
        Assert.Contains("turret_bone", labels);
        Assert.Contains("turbo_bone", labels);
        Assert.DoesNotContain("root", labels);
    }

    // ── multiple model tags → union ───────────────────────────────────────────

    [Fact]
    public void GetProposals_MultipleModelTags_UnionsBones()
    {
        var helper = new BoneNameCompletionHelper(SchemaWithModelTags("Space_Model_Name", "Land_Model_Name"));
        const string xml =
            "<GameObjectType>" +
            "<Space_Model_Name>space.alo</Space_Model_Name>" +
            "<Land_Model_Name>land.alo</Land_Model_Name>" +
            "<Bone_Name></Bone_Name>" +
            "</GameObjectType>";
        var bone = Node(xml, "//bone_name");
        var index = IndexWithBones(("space.alo", ["space_root"]), ("land.alo", ["land_root"]));

        var result = helper.GetProposals(bone, "", index);

        var labels = result.Select(p => p.Label).ToList();
        Assert.Contains("space_root", labels);
        Assert.Contains("land_root", labels);
    }

    // ── path normalisation ────────────────────────────────────────────────────

    [Fact]
    public void GetProposals_ModelPathWithBackslashesAndCase_NormalisedForLookup()
    {
        var helper = new BoneNameCompletionHelper(SchemaWithModelTags("Space_Model_Name"));
        var bone = Node(
            "<SpaceUnit><Space_Model_Name>Data\\Art\\Models\\Unit.ALO</Space_Model_Name><Bone_Name></Bone_Name></SpaceUnit>",
            "//bone_name");
        var index = IndexWithBones(("data/art/models/unit.alo", ["root"]));

        var result = helper.GetProposals(bone, "", index);

        Assert.Contains("root", result.Select(p => p.Label));
    }

    // ── fake schema ───────────────────────────────────────────────────────────

    private sealed class FakeSchema : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string tagName)
        {
            return _tags.GetValueOrDefault(tagName);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void Add(string tag, ReferenceKind kind)
        {
            _tags[tag] = new XmlTagDefinition
            {
                Tag = tag, ValueType = XmlValueType.NameReference, ReferenceKind = kind
            };
        }
    }
}