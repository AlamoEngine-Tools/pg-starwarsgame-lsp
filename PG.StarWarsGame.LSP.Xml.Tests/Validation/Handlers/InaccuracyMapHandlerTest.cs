// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class InaccuracyMapHandlerTest
{
    private static readonly InaccuracyMapHandler Sut = new();

    private static readonly XmlTagDefinition Tag =
        XmlHandlerTestFixtures.MakeTag("Inaccuracy_Value", XmlValueType.InaccuracyMap);

    [Theory]
    [InlineData("LONG_RANGE, 0.5")]
    [InlineData("SHORT_RANGE,1.0")]
    public void Valid_pair_returns_no_diagnostics(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LONG_RANGE")]
    [InlineData(",0.5")]
    [InlineData("LONG_RANGE, not_a_float")]
    [InlineData("LONG_RANGE,")]
    public void Invalid_values_return_error(string value)
    {
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), XmlHandlerTestFixtures.EmptyCtx).ToList();
        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
    }

    // ── category (slot 0) membership against GameObjectCategoryType ─────────

    private static DiagnosticsContext CtxWithCategories(params string[] workspaceValues)
    {
        var schema = new StubSchemaWithCategoryEnum(new EnumDefinition
        {
            Name = "GameObjectCategoryType", Kind = EnumKind.DynamicXml, Values = []
        });
        var index = GameIndex.Empty with
        {
            WorkspaceDynamicEnumValues = ImmutableDictionary
                .Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase)
                .Add("GameObjectCategoryType", [.. workspaceValues])
        };
        return new DiagnosticsContext(schema, index, "file:///test.xml", "en");
    }

    [Fact]
    public void UnknownCategory_WithEnumValues_ReturnsErrorAtCategoryToken()
    {
        // The vanilla data even ships this exact typo: <Fire_Inaccuracy_Distance> Bombe, 70.0 </…>
        var ctx = CtxWithCategories("Air", "Bomber", "Fighter");
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, "Bombe, 70.0"), ctx).ToList();

        var d = Assert.Single(results);
        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Bombe", d.Message);
        Assert.Equal(0, d.OverrideColumn);
        Assert.Equal("Bombe".Length, d.OverrideLength);
    }

    [Theory]
    [InlineData("Bomber, 15.0")]
    [InlineData("bomber, 15.0")]
    public void KnownCategory_ReturnsNoDiagnostics(string value)
    {
        var ctx = CtxWithCategories("Air", "Bomber", "Fighter");
        Assert.Empty(Sut.Handle(XmlHandlerTestFixtures.MakeFact(Tag, value), ctx));
    }

    [Fact]
    public void Wrong_type_returns_no_diagnostics()
    {
        var floatTag = XmlHandlerTestFixtures.MakeTag("Speed", XmlValueType.Float);
        var results = Sut.Handle(XmlHandlerTestFixtures.MakeFact(floatTag, ""), XmlHandlerTestFixtures.EmptyCtx)
            .ToList();
        Assert.Empty(results);
    }

    private sealed class StubSchemaWithCategoryEnum(EnumDefinition enumDef) : ISchemaProvider
    {
        public XmlTagDefinition? GetTag(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public GameObjectTypeDefinition? GetObjectType(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];

        public EnumDefinition? GetEnum(string name) =>
            string.Equals(name, enumDef.Name, StringComparison.OrdinalIgnoreCase) ? enumDef : null;

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [enumDef];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }
    }
}
