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
    {
        return new GameReference("EMPIRE", GameSymbolKind.XmlObject, "Faction", DocUri, line, col, len);
    }

    private static GameSymbol Symbol(string typeName)
    {
        return new GameSymbol("EMPIRE", GameSymbolKind.XmlObject, typeName, new FileOrigin(OtherUri, 0, null), null);
    }

    private static GameIndex IndexWith(GameReference? reference, GameSymbol? symbol,
        int referencingDocRank = 0, int? originDocRank = null, string? originLayerName = null)
    {
        var refs = reference is null
            ? ImmutableArray<GameReference>.Empty
            : ImmutableArray.Create(reference);
        var doc = new DocumentIndex(DocUri, 1, ImmutableArray<GameSymbol>.Empty, refs,
            LayerRank: referencingDocRank);
        var defs = symbol is null
            ? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            : ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(symbol.Id, [symbol]);
        var docs = ImmutableDictionary<string, DocumentIndex>.Empty.Add(DocUri, doc);
        if (originDocRank is not null)
            docs = docs.Add(OtherUri, new DocumentIndex(OtherUri, 1,
                symbol is null ? ImmutableArray<GameSymbol>.Empty : [symbol],
                ImmutableArray<GameReference>.Empty,
                LayerRank: originDocRank.Value, LayerName: originLayerName));
        return new GameIndex(BaselineIndex.Empty,
            docs,
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
        var ctx = MakeCtx(index, schema, line: 1, character: 14);

        var hover = strategy.Handle(ctx);

        Assert.NotNull(hover);
        var md = hover!.Contents.MarkupContent!.Value;
        Assert.Contains("EMPIRE", md);
        Assert.Contains("Faction", md);
    }

    // ── dependency-layer indicator ────────────────────────────────────────────
    // A workspace definition living in a DEPENDENCY project's layer must say so — without the
    // note it is indistinguishable from a leaf-project definition, and users read a
    // non-navigating experience as "broken" (2026-07-05 smoketest report).

    [Fact]
    public void Handle_SymbolInDependencyLayer_HoverNamesTheDependency()
    {
        var strategy = new ReferenceHoverStrategy();
        var schema = SchemaWithType("Faction");
        // Referencing doc is the leaf (rank 1); the definition lives in a rank-0 dependency.
        var index = IndexWith(Ref(1, 13, 6), Symbol("Faction"),
            referencingDocRank: 1, originDocRank: 0, originLayerName: "Base EaW");
        var ctx = MakeCtx(index, schema, line: 1, character: 14);

        var hover = strategy.Handle(ctx);

        Assert.NotNull(hover);
        var md = hover!.Contents.MarkupContent!.Value;
        Assert.Contains("dependency", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Base EaW", md);
    }

    [Fact]
    public void Handle_SymbolInDependencyLayerWithoutName_HoverShowsGenericDependencyNote()
    {
        var strategy = new ReferenceHoverStrategy();
        var schema = SchemaWithType("Faction");
        var index = IndexWith(Ref(1, 13, 6), Symbol("Faction"),
            referencingDocRank: 1, originDocRank: 0);
        var ctx = MakeCtx(index, schema, line: 1, character: 14);

        var hover = strategy.Handle(ctx);

        Assert.NotNull(hover);
        Assert.Contains("dependency", hover!.Contents.MarkupContent!.Value,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_SymbolInLeafLayer_NoDependencyNote()
    {
        var strategy = new ReferenceHoverStrategy();
        var schema = SchemaWithType("Faction");
        // Definition doc is at the same (leaf) rank as the referencing doc.
        var index = IndexWith(Ref(1, 13, 6), Symbol("Faction"),
            referencingDocRank: 1, originDocRank: 1, originLayerName: "My Mod");
        var ctx = MakeCtx(index, schema, line: 1, character: 14);

        var hover = strategy.Handle(ctx);

        Assert.NotNull(hover);
        Assert.DoesNotContain("dependency", hover!.Contents.MarkupContent!.Value,
            StringComparison.OrdinalIgnoreCase);
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
        var ctx = MakeCtx(index, new StubSchemaProvider(), line: 1, character: 14);
        Assert.Null(strategy.Handle(ctx));
    }

    // ── file-scoped fakes ─────────────────────────────────────────────────────

    private sealed class StubSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new();

        public XmlTagDefinition? GetTag(string _)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _types.GetValueOrDefault(name);
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

        public void AddType(GameObjectTypeDefinition type)
        {
            _types[type.TypeName] = type;
        }
    }
}