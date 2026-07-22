// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.InlayHints;

public sealed class BoneModelInlayHintProviderTest
{
    private static InlayHintContext MakeCtx(string xml, string boneXPath, FakeSchema schema, GameIndex index)
    {
        var hapDoc = new HtmlDocument();
        hapDoc.LoadHtml(xml);
        var node = hapDoc.DocumentNode.SelectSingleNode(boneXPath)!;
        var tagDef = schema.GetTag(node.Name)
                     ?? new XmlTagDefinition { Tag = node.Name, ValueType = XmlValueType.NameReference };
        var line = XmlUtility.GetLine(node);
        return new InlayHintContext("file:///t.xml", index, schema, hapDoc, node, tagDef, line,
            lineIndex: new LineOffsetIndex(xml));
    }

    private static GameIndex IndexWithBones(params (string Key, string[] Bones)[] entries)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (key, bones) in entries)
            builder[key] = bones.ToImmutableArray();
        return GameIndex.Empty with { ModelBones = builder.ToImmutable() };
    }

    private static FakeSchema Schema()
    {
        var schema = new FakeSchema();
        schema.Add("Space_Model_Name", ReferenceKind.ModelFile);
        schema.Add("Land_Model_Name", ReferenceKind.ModelFile);
        schema.Add("Fire_Bone_A", ReferenceKind.BoneName);
        return schema;
    }

    [Fact]
    public void ResolvableBone_SingleModel_PrefixesModelAtValueStart()
    {
        const string xml =
            "<SpaceUnit>\n" +
            "  <Space_Model_Name>unit.alo</Space_Model_Name>\n" +
            "  <Fire_Bone_A>Weap_FP00</Fire_Bone_A>\n" +
            "</SpaceUnit>";
        var index = IndexWithBones(("unit.alo", ["Weap_FP00", "root"]));
        var ctx = MakeCtx(xml, "//fire_bone_a", Schema(), index);

        var hints = new BoneModelInlayHintProvider(new EmptyTagSource()).Handle(ctx).ToList();

        var hint = Assert.Single(hints);
        Assert.Equal("unit.alo::", hint.Label.String);
        Assert.Equal(InlayHintKind.Type, hint.Kind);
        // Sits immediately before the value: "  <Fire_Bone_A>" is 15 chars, value starts at col 15 on line 2.
        Assert.Equal(2, hint.Position.Line);
        Assert.Equal(15, hint.Position.Character);
    }

    [Fact]
    public void MultipleModels_UsesTheModelThatActuallyContainsTheBone()
    {
        const string xml =
            "<GameObjectType>\n" +
            "  <Space_Model_Name>space.alo</Space_Model_Name>\n" +
            "  <Land_Model_Name>land.alo</Land_Model_Name>\n" +
            "  <Fire_Bone_A>land_only_bone</Fire_Bone_A>\n" +
            "</GameObjectType>";
        var index = IndexWithBones(("space.alo", ["space_root"]), ("land.alo", ["land_only_bone"]));
        var ctx = MakeCtx(xml, "//fire_bone_a", Schema(), index);

        var hints = new BoneModelInlayHintProvider(new EmptyTagSource()).Handle(ctx).ToList();

        var hint = Assert.Single(hints);
        Assert.Equal("land.alo::", hint.Label.String);
    }

    [Fact]
    public void UnresolvedBone_NotInAnyModel_ReturnsEmpty()
    {
        const string xml =
            "<SpaceUnit>\n" +
            "  <Space_Model_Name>unit.alo</Space_Model_Name>\n" +
            "  <Fire_Bone_A>Typo_Bone</Fire_Bone_A>\n" +
            "</SpaceUnit>";
        var index = IndexWithBones(("unit.alo", ["Weap_FP00", "root"]));
        var ctx = MakeCtx(xml, "//fire_bone_a", Schema(), index);

        Assert.Empty(new BoneModelInlayHintProvider(new EmptyTagSource()).Handle(ctx));
    }

    [Fact]
    public void NoModelInScope_ReturnsEmpty()
    {
        const string xml =
            "<SpaceUnit>\n" +
            "  <Fire_Bone_A>Weap_FP00</Fire_Bone_A>\n" +
            "</SpaceUnit>";
        var index = IndexWithBones(("unit.alo", ["Weap_FP00"]));
        var ctx = MakeCtx(xml, "//fire_bone_a", Schema(), index);

        Assert.Empty(new BoneModelInlayHintProvider(new EmptyTagSource()).Handle(ctx));
    }

    [Fact]
    public void NonBoneTag_ReturnsEmpty()
    {
        const string xml = "<SpaceUnit>\n  <Space_Model_Name>unit.alo</Space_Model_Name>\n</SpaceUnit>";
        var index = IndexWithBones(("unit.alo", ["Weap_FP00"]));
        var ctx = MakeCtx(xml, "//space_model_name", Schema(), index);

        Assert.Empty(new BoneModelInlayHintProvider(new EmptyTagSource()).Handle(ctx));
    }

    [Fact]
    public void EmptyBoneValue_ReturnsEmpty()
    {
        const string xml =
            "<SpaceUnit>\n" +
            "  <Space_Model_Name>unit.alo</Space_Model_Name>\n" +
            "  <Fire_Bone_A></Fire_Bone_A>\n" +
            "</SpaceUnit>";
        var index = IndexWithBones(("unit.alo", ["Weap_FP00"]));
        var ctx = MakeCtx(xml, "//fire_bone_a", Schema(), index);

        Assert.Empty(new BoneModelInlayHintProvider(new EmptyTagSource()).Handle(ctx));
    }

    [Fact]
    public void PathQualifiedModelReference_NormalisedForLookupAndDisplay()
    {
        const string xml =
            "<SpaceUnit>\n" +
            "  <Space_Model_Name>Data\\Art\\Models\\Unit.ALO</Space_Model_Name>\n" +
            "  <Fire_Bone_A>Weap_FP00</Fire_Bone_A>\n" +
            "</SpaceUnit>";
        var index = IndexWithBones(("unit.alo", ["Weap_FP00"]));
        var ctx = MakeCtx(xml, "//fire_bone_a", Schema(), index);

        var hint = Assert.Single(new BoneModelInlayHintProvider(new EmptyTagSource()).Handle(ctx));
        Assert.Equal("unit.alo::", hint.Label.String);
    }

    // A bone tag on a plain GameObject has no HardPoint parent, so the hardpoint resolver is never
    // consulted and the tag source is irrelevant to these inline cases.
    private sealed class EmptyTagSource : IVariantTagSource
    {
        public IReadOnlyList<VariantTag>? TryGetTags(string objectId) => null;
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
