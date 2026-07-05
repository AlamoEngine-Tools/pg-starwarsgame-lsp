// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Caching;

public sealed class SchemaFingerprintTest
{
    private static XmlTagDefinition Tag(string name, string? referenceType = null)
    {
        return new XmlTagDefinition
        {
            Tag = name,
            ValueType = XmlValueType.NameReference,
            ReferenceKind = referenceType is null ? ReferenceKind.Unknown : ReferenceKind.XmlObject,
            ObjectType = referenceType is null
                ? null
                : new GameObjectTypeDefinition { TypeName = referenceType, NameTag = "Name" }
        };
    }

    [Fact]
    public void Compute_SameContentDifferentInstances_SameFingerprint()
    {
        var a = SchemaFingerprint.Compute(new StubSchema([Tag("Foo", "SFXEvent")]));
        var b = SchemaFingerprint.Compute(new StubSchema([Tag("Foo", "SFXEvent")]));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_TagReferenceTypeChanged_DifferentFingerprint()
    {
        // The exact scenario this exists for: flipping a tag from referenceKind: unknown to a
        // typed reference changes what the parser emits, without any file content changing.
        var before = SchemaFingerprint.Compute(new StubSchema([Tag("Blob_Material_Name")]));
        var after = SchemaFingerprint.Compute(
            new StubSchema([Tag("Blob_Material_Name", "ShadowBlobMaterial")]));
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Compute_TagOrderIrrelevant_SameFingerprint()
    {
        var a = SchemaFingerprint.Compute(new StubSchema([Tag("A", "X"), Tag("B", "Y")]));
        var b = SchemaFingerprint.Compute(new StubSchema([Tag("B", "Y"), Tag("A", "X")]));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_MetafileAdded_DifferentFingerprint()
    {
        var meta = new MetafileDefinition(
            "data/xml/shadowblobmaterials.xml", MetafileType.DirectContent, ["ShadowBlobMaterial"]);
        var without = SchemaFingerprint.Compute(new StubSchema([]));
        var with = SchemaFingerprint.Compute(new StubSchema([], [meta]));
        Assert.NotEqual(without, with);
    }

    [Fact]
    public void Compute_EnumValueAdded_DifferentFingerprint()
    {
        var e1 = new EnumDefinition
            { Name = "ArmorType", Kind = EnumKind.DynamicXml, Values = [new EnumValueDefinition { Name = "A" }] };
        var e2 = new EnumDefinition
        {
            Name = "ArmorType", Kind = EnumKind.DynamicXml,
            Values = [new EnumValueDefinition { Name = "A" }, new EnumValueDefinition { Name = "B" }]
        };
        Assert.NotEqual(
            SchemaFingerprint.Compute(new StubSchema([], enums: [e1])),
            SchemaFingerprint.Compute(new StubSchema([], enums: [e2])));
    }

    private sealed class StubSchema(
        IReadOnlyList<XmlTagDefinition> tags,
        IReadOnlyList<MetafileDefinition>? metafiles = null,
        IReadOnlyList<EnumDefinition>? enums = null) : ISchemaProvider
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

        public IReadOnlyList<XmlTagDefinition> AllTags => tags;
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => enums ?? [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => metafiles ?? [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}
