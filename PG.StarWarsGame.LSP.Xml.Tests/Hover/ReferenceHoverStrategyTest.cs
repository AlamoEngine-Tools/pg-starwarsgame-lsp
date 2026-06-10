// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Hover;

public sealed class ReferenceHoverStrategyTest
{
    private const string DocUri = "file:///test.xml";
    private const string OtherUri = "file:///other.xml";

    private static HoverContext MakeCtx(
        GameIndex index,
        ISchemaProvider? schema = null,
        bool isOnTagName = false,
        int line = 1,
        int character = 14,
        string locale = "en")
    {
        var hapDoc = XmlUtility.CreateHtmlDocument("<Root><Affiliation>EMPIRE</Affiliation></Root>");
        XmlUtility.TryGetRootNode(hapDoc, out var rootNode);
        XmlUtility.TryFindNode(hapDoc, 0, out var node);
        return new HoverContext(DocUri, index, schema ?? new StubSchemaProvider(),
            hapDoc, rootNode!, node ?? new HtmlDocument().DocumentNode,
            isOnTagName, line, character, locale);
    }

    private static GameReference Ref(int line, int col, int len)
        => new("EMPIRE", GameSymbolKind.XmlObject, "Faction", DocUri, line, col, len);

    private static GameSymbol Symbol(string typeName)
        => new("EMPIRE", GameSymbolKind.XmlObject, typeName, new FileOrigin(OtherUri, 0, null), null);

    private static GameIndex IndexWith(GameReference? reference, GameSymbol? symbol)
    {
        var refs = reference is null
            ? ImmutableArray<GameReference>.Empty
            : ImmutableArray.Create(reference);
        var doc = new DocumentIndex(DocUri, 1, ImmutableArray<GameSymbol>.Empty, refs);
        var defs = symbol is null
            ? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            : ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(symbol.Id, [symbol]);
        return new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(DocUri, doc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static StubSchemaProvider SchemaWithType(string typeName)
    {
        var p = new StubSchemaProvider();
        p.AddType(new GameObjectTypeDefinition
        {
            TypeName = typeName,
            Description = new Dictionary<string, string> { ["en"] = "A faction." }
        });
        return p;
    }

    [Fact]
    public void Handle_IsOnTagName_ReturnsNull()
    {
        var strategy = new ReferenceHoverStrategy();
        var ctx = MakeCtx(GameIndex.Empty, isOnTagName: true);
        Assert.Null(strategy.Handle(ctx));
    }

    [Fact]
    public void Handle_NoDocumentInIndex_ReturnsNull()
    {
        var strategy = new ReferenceHoverStrategy();
        var ctx = MakeCtx(GameIndex.Empty);
        Assert.Null(strategy.Handle(ctx));
    }

    [Fact]
    public void Handle_NoReferenceAtPosition_ReturnsNull()
    {
        var strategy = new ReferenceHoverStrategy();
        var index = IndexWith(Ref(1, 0, 5), Symbol("Faction")); // ref at col 0-4, cursor at 14
        var ctx = MakeCtx(index, character: 14);
        Assert.Null(strategy.Handle(ctx));
    }

    [Fact]
    public void Handle_CursorOnReference_ReturnsHover()
    {
        var strategy = new ReferenceHoverStrategy();
        var schema = SchemaWithType("Faction");
        var index = IndexWith(Ref(1, 13, 6), Symbol("Faction")); // ref at col 13-18
        var ctx = MakeCtx(index, schema: schema, line: 1, character: 14);

        var hover = strategy.Handle(ctx);

        Assert.NotNull(hover);
        var md = hover!.Contents.MarkupContent!.Value;
        Assert.Contains("EMPIRE", md);
        Assert.Contains("Faction", md);
    }

    [Fact]
    public void Handle_UnresolvedSymbol_ReturnsNull()
    {
        var strategy = new ReferenceHoverStrategy();
        var index = IndexWith(Ref(1, 13, 6), null);
        var ctx = MakeCtx(index, line: 1, character: 14);
        Assert.Null(strategy.Handle(ctx));
    }

    [Fact]
    public void Handle_SymbolTypeNotInSchema_ReturnsNull()
    {
        var strategy = new ReferenceHoverStrategy();
        var index = IndexWith(Ref(1, 13, 6), Symbol("Faction"));
        var ctx = MakeCtx(index, schema: new StubSchemaProvider(), line: 1, character: 14);
        Assert.Null(strategy.Handle(ctx));
    }

    // ── file-scoped fakes ─────────────────────────────────────────────────────

    private sealed class StubSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new();

        public void AddType(GameObjectTypeDefinition type) => _types[type.TypeName] = type;

        public XmlTagDefinition? GetTag(string _) => null;
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public GameObjectTypeDefinition? GetObjectType(string name) => _types.GetValueOrDefault(name);
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
        public EnumDefinition? GetEnum(string _) => null;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public event EventHandler? SchemaRefreshed { add { } remove { } }
    }
}
