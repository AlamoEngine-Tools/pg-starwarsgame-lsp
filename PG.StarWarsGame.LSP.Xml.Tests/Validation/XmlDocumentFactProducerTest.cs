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
        IFileTypeRegistry? registry = null)
    {
        return new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            schema ?? new SingleTagSchemaProvider(),
            registry ?? new EmptyFileTypeRegistry());
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