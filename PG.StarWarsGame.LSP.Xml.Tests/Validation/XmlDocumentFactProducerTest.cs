// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class XmlDocumentFactProducerTest
{
    private const string Uri = "file:///units/SpaceUnitData.xml";

    private static XmlDocumentFactProducer Build(
        ISchemaProvider? schema = null,
        IFileTypeRegistry? registry = null,
        IXmlStructuralValidator? validator = null)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            schema ?? new SingleTagSchemaProvider(),
            registry ?? new EmptyFileTypeRegistry(),
            validator ?? new XmlStructuralValidator());
    }

    // ── tag value facts ───────────────────────────────────────────────────────

    [Fact]
    public void Non_empty_leaf_value_emits_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Max_Speed>10.0</Max_Speed></SpaceUnit></GameObjectFiles>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal("Max_Speed", tvf.Tag.Tag, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("10.0", tvf.RawValue);
        Assert.Equal(Uri, tvf.DocumentUri);
    }

    [Fact]
    public void Empty_leaf_value_does_not_emit_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Max_Speed></Max_Speed></SpaceUnit></GameObjectFiles>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlTagValueFact>());
    }

    [Fact]
    public void Whitespace_only_leaf_value_does_not_emit_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Max_Speed>   </Max_Speed></SpaceUnit></GameObjectFiles>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlTagValueFact>());
    }

    [Fact]
    public void Unknown_tag_does_not_emit_XmlTagValueFact()
    {
        const string xml = "<GameObjectFiles><SpaceUnit><Unknown_Tag>42</Unknown_Tag></SpaceUnit></GameObjectFiles>";
        var facts = Build(new EmptySchemaProvider()).Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlTagValueFact>());
    }

    [Fact]
    public void XmlTagValueFact_carries_correct_tag_definition()
    {
        const string xml = "<Root><Max_Speed>5.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(XmlValueType.Float, tvf.Tag.ValueType);
    }

    // ── duplicate tag facts ───────────────────────────────────────────────────

    [Fact]
    public void Singleton_tag_appearing_twice_emits_XmlDuplicateTagFact_for_each_occurrence()
    {
        const string xml = "<Root><Max_Speed>1.0</Max_Speed><Max_Speed>2.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var dups = facts.OfType<XmlDuplicateTagFact>().ToList();
        Assert.Equal(2, dups.Count);
    }

    [Fact]
    public void XmlDuplicateTagFact_references_other_lines()
    {
        const string xml = "<Root>\n<Max_Speed>1.0</Max_Speed>\n<Max_Speed>2.0</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);
        var dups = facts.OfType<XmlDuplicateTagFact>().ToList();
        Assert.Equal(2, dups.Count);
        // Each occurrence records the other's line
        Assert.All(dups, d => Assert.Single(d.OtherLines));
        Assert.NotEqual(dups[0].OtherLines[0], dups[1].OtherLines[0]);
    }

    [Fact]
    public void MultipleAllowed_tag_appearing_twice_does_not_emit_XmlDuplicateTagFact()
    {
        const string xml = "<Root><Multi_Tag>a</Multi_Tag><Multi_Tag>b</Multi_Tag></Root>";
        var schema = new MultiTagSchemaProvider();
        var facts = Build(schema).Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlDuplicateTagFact>());
    }

    // ── notes facts ───────────────────────────────────────────────────────────

    [Fact]
    public void Tag_with_notes_emits_XmlNotesFact()
    {
        const string xml = "<Root><Notes_Tag>1.0</Notes_Tag></Root>";
        var schema = new NotesTagSchemaProvider();
        var facts = Build(schema).Produce(xml, Uri);
        Assert.Single(facts.OfType<XmlNotesFact>());
    }

    [Fact]
    public void Tag_without_notes_does_not_emit_XmlNotesFact()
    {
        const string xml = "<Root><Max_Speed>1.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlNotesFact>());
    }

    // ── position ─────────────────────────────────────────────────────────────

    [Fact]
    public void XmlTagValueFact_has_non_negative_line_and_column()
    {
        const string xml = "<Root><Max_Speed>5.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.True(tvf.Line >= 0);
        Assert.True(tvf.Column >= 0);
    }

    [Fact]
    public void XmlTagValueFact_InlineValue_PointsToValueStart()
    {
        // <Root><Max_Speed>90.0</Max_Speed></Root>
        //                  ^ col 17 (0-based), length 4
        const string xml = "<Root><Max_Speed>90.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(0, tvf.Line);
        Assert.Equal(17, tvf.Column);
        Assert.Equal(4, tvf.Length);
    }

    [Fact]
    public void XmlTagValueFact_MultiLineValue_PointsToValueLine()
    {
        // Line 0: <Root>
        // Line 1: <Max_Speed>
        // Line 2:     90.0     ← value at col 4, length 4
        // Line 3: </Max_Speed>
        // Line 4: </Root>
        const string xml = "<Root>\n<Max_Speed>\n    90.0\n</Max_Speed>\n</Root>";
        var facts = Build().Produce(xml, Uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(2, tvf.Line);
        Assert.Equal(4, tvf.Column);
        Assert.Equal(4, tvf.Length);
    }

    // ── ability sub-object fact production ───────────────────────────────────

    [Fact]
    public void AbilityField_EmitsFactWithAbilityTypeTagDefinition()
    {
        // <Root><Abilities SubObjectList="Yes"><Lucky_Shot_Attack_Ability Name="...">
        //   <Applicable_Unit_Categories>INFANTRY</Applicable_Unit_Categories>
        // </Lucky_Shot_Attack_Ability></Abilities></Root>
        const string xml =
            "<Root><Abilities SubObjectList=\"Yes\">" +
            "<Lucky_Shot_Attack_Ability Name=\"Luke_Shot\">" +
            "<Applicable_Unit_Categories>INFANTRY</Applicable_Unit_Categories>" +
            "</Lucky_Shot_Attack_Ability></Abilities></Root>";

        var facts = Build(new AbilitySubObjectSchemaProvider()).Produce(xml, Uri);

        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal("Applicable_Unit_Categories", tvf.Tag.Tag, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("INFANTRY", tvf.RawValue);
        // Fact must carry the ability type's tag definition (NameReference), not a global Float
        Assert.Equal(XmlValueType.NameReference, tvf.Tag.ValueType);
    }

    [Fact]
    public void AbilityField_UnknownInAbilityType_FallsBackToGlobalTagGracefully()
    {
        const string xml =
            "<Root><Abilities SubObjectList=\"Yes\">" +
            "<Lucky_Shot_Attack_Ability Name=\"Luke_Shot\">" +
            "<Max_Speed>5.0</Max_Speed>" +
            "</Lucky_Shot_Attack_Ability></Abilities></Root>";

        // Max_Speed is not in LuckyShotAttackAbility schema, but IS a global tag (Float)
        var facts = Build(new AbilitySubObjectSchemaProvider()).Produce(xml, Uri);

        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal("Max_Speed", tvf.Tag.Tag, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(XmlValueType.Float, tvf.Tag.ValueType);
    }

    // ── type-container context resolution ────────────────────────────────────

    [Fact]
    public void TypeContainer_SFXEvent_TextId_EmitsNameReferenceListFact()
    {
        // SFXEvent.Text_ID is NameReferenceList; global GetTag("Text_ID") returns NameReference.
        // Without object-type context the wrong ValueType reaches the handler.
        const string uri = "file:///sfx/SFXEvents.xml";
        const string xml =
            "<SFXEventFiles><SFXEvent Name=\"foo\"><Text_ID>K1 K2</Text_ID></SFXEvent></SFXEventFiles>";
        var facts = Build(new SfxEventSchemaProvider(), new SfxEventFileTypeRegistry()).Produce(xml, uri);
        var tvf = Assert.Single(facts.OfType<XmlTagValueFact>());
        Assert.Equal(XmlValueType.NameReferenceList, tvf.Tag.ValueType);
    }

    // ── structural validation ─────────────────────────────────────────────────

    [Fact]
    public void Well_formed_xml_emits_no_XmlStructureFact()
    {
        const string xml = "<Root><Max_Speed>5.0</Max_Speed></Root>";
        var facts = Build().Produce(xml, Uri);
        Assert.Empty(facts.OfType<XmlStructureFact>());
    }

    [Fact]
    public void Mismatched_closing_tag_emits_XmlStructureFact()
    {
        const string xml = "<Foo><Bar></Foo>";
        var facts = Build().Produce(xml, Uri);
        var sf = Assert.Single(facts.OfType<XmlStructureFact>());
        Assert.Equal(Uri, sf.DocumentUri);
        Assert.Contains("Bar", sf.Reason);
    }

    [Fact]
    public void Unclosed_tag_emits_XmlStructureFact()
    {
        const string xml = "<Foo><Bar>";
        var facts = Build().Produce(xml, Uri);
        Assert.NotEmpty(facts.OfType<XmlStructureFact>());
    }

    [Fact]
    public void Malformed_attribute_emits_XmlStructureFact()
    {
        const string xml = "<Foo attr=value />";
        var facts = Build().Produce(xml, Uri);
        Assert.NotEmpty(facts.OfType<XmlStructureFact>());
    }

    [Fact]
    public void XmlStructureFact_carries_nonnegative_line_and_column()
    {
        const string xml = "<Foo>\n  <Bar>\n</Foo>";
        var facts = Build().Produce(xml, Uri);
        var sf = Assert.Single(facts.OfType<XmlStructureFact>());
        Assert.True(sf.Line >= 0);
        Assert.True(sf.Column >= 0);
    }
}

// ── fakes ────────────────────────────────────────────────────────────────────

file sealed class SingleTagSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition MaxSpeed = new()
        { Tag = "Max_Speed", ValueType = XmlValueType.Float, MultipleAllowed = false };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Max_Speed", StringComparison.OrdinalIgnoreCase) ? MaxSpeed : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [MaxSpeed];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class MultiTagSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition MultiTag = new()
        { Tag = "Multi_Tag", ValueType = XmlValueType.Float, MultipleAllowed = true };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Multi_Tag", StringComparison.OrdinalIgnoreCase) ? MultiTag : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [MultiTag];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class NotesTagSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition NotesTag = new()
    {
        Tag = "Notes_Tag", ValueType = XmlValueType.Float, MultipleAllowed = false,
        Notes = new Dictionary<string, string> { ["en"] = "A tag with notes" }
    };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Notes_Tag", StringComparison.OrdinalIgnoreCase) ? NotesTag : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [NotesTag];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class EmptySchemaProvider : ISchemaProvider
{
    public XmlTagDefinition? GetTag(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class AbilitySubObjectSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition AbilitiesTag = new()
        { Tag = "Abilities", ValueType = XmlValueType.AbilityDefinitionSubObjectList };

    private static readonly XmlTagDefinition ApplicableUnitCategoriesTag = new()
        { Tag = "Applicable_Unit_Categories", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition MaxSpeedTag = new()
        { Tag = "Max_Speed", ValueType = XmlValueType.Float };

    public XmlTagDefinition? GetTag(string tagName)
    {
        if (tagName.Equals("Abilities", StringComparison.OrdinalIgnoreCase)) return AbilitiesTag;
        if (tagName.Equals("Max_Speed", StringComparison.OrdinalIgnoreCase)) return MaxSpeedTag;
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        if (typeName.Equals("LuckyShotAttackAbility", StringComparison.OrdinalIgnoreCase))
            return [ApplicableUnitCategoriesTag];
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [AbilitiesTag, ApplicableUnitCategoriesTag, MaxSpeedTag];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class SfxEventSchemaProvider : ISchemaProvider
{
    private static readonly XmlTagDefinition GlobalTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReference };

    private static readonly XmlTagDefinition SfxTextId = new()
        { Tag = "Text_ID", ValueType = XmlValueType.NameReferenceList };

    private static readonly GameObjectTypeDefinition SfxEventTypeDef = new()
        { TypeName = "SFXEvent", NameTag = "Name" };

    public XmlTagDefinition? GetTag(string tagName)
    {
        return tagName.Equals("Text_ID", StringComparison.OrdinalIgnoreCase) ? GlobalTextId : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string typeName)
    {
        return typeName.Equals("SFXEvent", StringComparison.OrdinalIgnoreCase) ? SfxEventTypeDef : null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
    {
        return typeName.Equals("SFXEvent", StringComparison.OrdinalIgnoreCase) ? [SfxTextId] : [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [GlobalTextId, SfxTextId];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [SfxEventTypeDef];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class SfxEventFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray.Create("SFXEvent");
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All => new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }
}