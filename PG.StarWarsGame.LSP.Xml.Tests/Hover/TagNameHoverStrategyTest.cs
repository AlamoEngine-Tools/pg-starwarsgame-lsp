// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.HoverStrategies;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Hover;

public sealed class TagNameHoverStrategyTest
{
    private const string DocUri = "file:///test.xml";

    private static (HoverContext ctx, StubSchemaProvider schema) MakeCtx(
        string xml,
        int nodeLine,
        bool isOnTagName = true,
        IFileTypeRegistry? registry = null)
    {
        var schema = new StubSchemaProvider();
        var hapDoc = XmlUtility.CreateHtmlDocument(xml);
        XmlUtility.TryGetRootNode(hapDoc, out var rootNode);
        XmlUtility.TryFindNode(hapDoc, nodeLine, out var node);
        var ctx = new HoverContext(DocUri, GameIndex.Empty, schema,
            hapDoc, rootNode!, node ?? rootNode!,
            isOnTagName, nodeLine, 1, "en");
        return (ctx, schema);
    }

    private static TagNameHoverStrategy Strategy(IFileTypeRegistry? registry = null)
        => new(registry ?? new EmptyFileTypeRegistry());

    // ── guard: not on tag name ────────────────────────────────────────────────

    [Fact]
    public void Handle_NotOnTagName_ReturnsNull()
    {
        var (ctx, _) = MakeCtx("<Root><Max_Speed>500</Max_Speed></Root>", 0, isOnTagName: false);
        Assert.Null(Strategy().Handle(ctx));
    }

    // ── no schema match ────────────────────────────────────────────────────────

    [Fact]
    public void Handle_TagAndTypeNotInSchema_ReturnsNull()
    {
        var (ctx, _) = MakeCtx("<Root>\n<Unknown_Tag/>\n</Root>", 1);
        Assert.Null(Strategy().Handle(ctx));
    }

    // ── tag hover (no type context) ────────────────────────────────────────────

    [Fact]
    public void Handle_TagInSchema_ReturnsTagHover()
    {
        var (ctx, schema) = MakeCtx("<Root>\n<Max_Speed>500</Max_Speed>\n</Root>", 1);
        schema.SetTag("max_speed", new XmlTagDefinition
        {
            Tag = "Max_Speed",
            ValueType = XmlValueType.Float,
            Description = new Dictionary<string, string> { ["en"] = "Top speed." }
        });

        var hover = Strategy().Handle(ctx);

        Assert.NotNull(hover);
        Assert.Contains("Max_Speed", hover!.Contents.MarkupContent!.Value);
    }

    // ── type hover ─────────────────────────────────────────────────────────────

    [Fact]
    public void Handle_TypeInSchema_ReturnsTypeHover()
    {
        var (ctx, schema) = MakeCtx("<Root>\n<SpaceUnit/>\n</Root>", 1);
        schema.AddType(new GameObjectTypeDefinition
        {
            TypeName = "SpaceUnit",
            NameTag = "Name",
            Description = new Dictionary<string, string> { ["en"] = "A space unit." }
        });

        var hover = Strategy().Handle(ctx);

        Assert.NotNull(hover);
        Assert.Contains("SpaceUnit", hover!.Contents.MarkupContent!.Value);
    }

    // ── registry-based type container ─────────────────────────────────────────

    [Fact]
    public void Handle_RegistryMappedContainer_ArbitraryName_ReturnsTypeHover()
    {
        var reg = new StubFileTypeRegistry();
        reg.Register(DocUri, ["SpaceUnit"]);
        var (ctx, schema) = MakeCtx("<GameObjectFiles>\n<Fighter_Mk2/>\n</GameObjectFiles>", 1);
        schema.AddType(new GameObjectTypeDefinition { TypeName = "SpaceUnit", NameTag = "Name" });

        var hover = Strategy(reg).Handle(ctx);

        Assert.NotNull(hover);
    }

    // ── file-scoped fakes ─────────────────────────────────────────────────────

    internal sealed class StubSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<XmlTagDefinition>> _tagsByType =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObjectTypeDefinition> _types =
            new(StringComparer.OrdinalIgnoreCase);

        public void SetTag(string name, XmlTagDefinition tag) => _tags[name] = tag;
        public void AddType(GameObjectTypeDefinition type) => _types[type.TypeName] = type;
        public void AddTagForType(string typeName, XmlTagDefinition tag)
        {
            if (!_tagsByType.TryGetValue(typeName, out var list))
                _tagsByType[typeName] = list = [];
            list.Add(tag);
        }

        public XmlTagDefinition? GetTag(string name) => _tags.GetValueOrDefault(name);
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public GameObjectTypeDefinition? GetObjectType(string name) => _types.GetValueOrDefault(name);
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
            => _tagsByType.TryGetValue(typeName, out var list) ? list : [];
        public EnumDefinition? GetEnum(string _) => null;
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public event EventHandler? SchemaRefreshed { add { } remove { } }
    }

    private sealed class EmptyFileTypeRegistry : IFileTypeRegistry
    {
        public ImmutableArray<string> GetTypesForFile(string _) => [];
        public void RegisterFile(string _, ImmutableArray<string> __) { }
        public void UnregisterFile(string _) { }
        public IReadOnlyDictionary<string, ImmutableArray<string>> All
            => ImmutableDictionary<string, ImmutableArray<string>>.Empty;
    }

    private sealed class StubFileTypeRegistry : IFileTypeRegistry
    {
        private readonly Dictionary<string, ImmutableArray<string>> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(string uri, string[] types) => _map[uri] = [..types];
        public ImmutableArray<string> GetTypesForFile(string path) => _map.GetValueOrDefault(path);
        public void RegisterFile(string _, ImmutableArray<string> __) { }
        public void UnregisterFile(string _) { }
        public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;
    }
}
