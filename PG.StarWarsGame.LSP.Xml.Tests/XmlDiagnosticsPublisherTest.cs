// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlDiagnosticsPublisherTest
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static GameIndex IndexWithDoc(string uri, int version = 1)
    {
        var doc = new DocumentIndex(uri, version,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(uri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static GameIndex IndexWithDuplicateId(string id, string uri1, string uri2)
    {
        var sym1 = new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri1, 0, null), null);
        var sym2 = new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri2, 0, null), null);
        var doc1 = new DocumentIndex(uri1, 1, [sym1], []);
        var doc2 = new DocumentIndex(uri2, 1, [sym2], []);
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(uri1, doc1).Add(uri2, doc2),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add(id, ImmutableArray.Create(sym1, sym2)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    private static GameIndex IndexWithRef(string documentUri, string targetId, string? targetType = null)
    {
        var reference = new GameReference(targetId, GameSymbolKind.XmlObject, targetType,
            documentUri, 2, 0, targetId.Length);
        var doc = new DocumentIndex(documentUri, 1, [], [reference]);
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(documentUri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add(targetId, ImmutableArray.Create(reference)));
    }

    private static GameIndex IndexWithHardcodedEnums(
        ImmutableDictionary<string, ImmutableArray<string>> hardcoded)
    {
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            DateTimeOffset.UtcNow, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            hardcoded,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);
        return GameIndex.Empty with { Baseline = baseline };
    }

    private static (XmlDiagnosticsPublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeGameIndexService indexService,
        FakeGameWorkspaceHost workspaceHost) BuildSubscribed(FakeSchemaProvider? schema = null,
            FakeFileTypeRegistry? registry = null)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var effectiveSchema = schema ?? new FakeSchemaProvider();
        var effectiveRegistry = registry ?? new FakeFileTypeRegistry();

        IXmlDiagnosticsHandler[] handlers =
        [
            new XmlDuplicateTagHandler(),
            new XmlNotesHandler(),
            new DuplicateSymbolHandler(),
            new UnresolvedReferenceHandler(),
            new TypeMismatchHandler(),
            new DeprecatedEventTypeHandler(),
            new EventTypeNotesHandler(),
            new StoryParamRequiredHandler(),
            new StoryParamNotesHandler(),
            new StoryParamValueHandler(),
            new StoryParamEnumHandler(),
            new StoryParamReferenceHandler(),
            new StoryParamUnknownSlotHandler()
        ];

        var fileHelper = new FileHelper(new MockFileSystem());
        var publisher = new XmlDiagnosticsPublisher(
            p => published.Add(p),
            indexService,
            workspaceHost,
            effectiveSchema,
            new XmlDiagnosticsHandlerRegistry(handlers),
            new XmlDocumentFactProducer(fileHelper, effectiveSchema, effectiveRegistry, new XmlStructuralValidator()),
            new XmlIndexFactProducer(),
            new StoryFactProducer(effectiveSchema),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            effectiveRegistry,
            fileHelper);
        return (publisher, published, indexService, workspaceHost);
    }

    // ── subscription / routing ───────────────────────────────────────────────

    [Fact]
    public void IndexChanged_Fires_Publishes_For_Each_OpenDocument()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_TwoOpenDocuments_PublishesTwice()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");
        workspaceHost.Set("file:///b.xml", "<Root/>");

        var index = IndexWithDoc("file:///a.xml") with
        {
            Documents = IndexWithDoc("file:///a.xml").Documents
                .Add("file:///b.xml", new DocumentIndex("file:///b.xml", 1, [], []))
        };
        indexService.Fire(index);

        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void IndexChanged_NoOpenDocuments_PublishesNothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void IndexChanged_DocumentClosed_ClearsDiagnostics_For_That_Uri()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        indexService.Fire(IndexWithDoc("file:///a.xml"));
        published.Clear();

        workspaceHost.Remove("file:///a.xml");
        indexService.Fire(GameIndex.Empty);

        var clear = Assert.Single(published);
        Assert.Equal("file:///a.xml", clear.Uri.ToString());
        Assert.Empty(clear.Diagnostics!);
    }

    [Fact]
    public void IndexChanged_OnlyPublishesForOpenFiles_NotForIndexOnlyEntries()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        var indexWithBoth = IndexWithDoc("file:///a.xml") with
        {
            Documents = IndexWithDoc("file:///a.xml").Documents
                .Add("a.xml", new DocumentIndex("a.xml", 0, [], []))
        };
        indexService.Fire(indexWithBoth);

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_ScannerFiresAfterOpen_StillPublishesForOpenFile()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        indexService.Fire(IndexWithDoc("file:///a.xml"));
        published.Clear();

        var scanIndex = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("a.xml", new DocumentIndex("a.xml", 0, [], []))
        };
        indexService.Fire(scanIndex);

        var pub = Assert.Single(published);
        Assert.Equal("file:///a.xml", pub.Uri.ToString());
    }

    // ── enum boundary diagnostics ────────────────────────────────────────────

    [Fact]
    public void CollectEnumBoundaryDiagnostics_NonGameConstantsFile_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = """
                           <GameConstants>
                             <Damage_Types>NEW_TYPE
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           EXPLOSIVE
                             </Damage_Types>
                           </GameConstants>
                           """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///units.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_EmptyHardcodedBaseline_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var index = IndexWithHardcodedEnums(
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);

        const string xml = """
                           <GameConstants>
                             <Damage_Types>CUSTOM
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           MYSTERY_TYPE
                             </Damage_Types>
                           </GameConstants>
                           """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_NoBoundaryComment_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = "<GameConstants><Damage_Types>EXPLOSIVE ENERGY CUSTOM</Damage_Types></GameConstants>";

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_KnownHardcodedToken_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE", "ENERGY"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = """
                           <GameConstants>
                             <Damage_Types>CUSTOM_TYPE
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           EXPLOSIVE ENERGY
                             </Damage_Types>
                           </GameConstants>
                           """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_MisplacedToken_EmitsWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE", "ENERGY"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = """
                           <GameConstants>
                             <Damage_Types>CUSTOM_TYPE
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           EXPLOSIVE SABER_SLASH ENERGY
                             </Damage_Types>
                           </GameConstants>
                           """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Single(diags);
        Assert.Contains("SABER_SLASH", diags[0].Message);
        Assert.Equal(DiagnosticSeverity.Warning, diags[0].Severity);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_MultipleEnums_BothChecked()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE"])
            .Add("ArmorType", ["ARMOR_INFANTRY"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = """
                           <GameConstants>
                             <Damage_Types>CUSTOM
                           <!-- PLEASE add your new damage types ABOVE this point. -->
                           EXPLOSIVE MOD_DAMAGE_BELOW
                             </Damage_Types>
                             <Armor_Types>MY_ARMOR
                           <!-- PLEASE add your new armor types ABOVE this point. -->
                           ARMOR_INFANTRY MOD_ARMOR_BELOW
                             </Armor_Types>
                           </GameConstants>
                           """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Equal(2, diags.Count);
        Assert.Contains(diags, d => d.Message.Contains("MOD_DAMAGE_BELOW"));
        Assert.Contains(diags, d => d.Message.Contains("MOD_ARMOR_BELOW"));
    }

    // ── story parser guard ───────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_StoryParserDocument_EmitsStoryDiagnostics()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("storyplots.xml", ImmutableArray.Create("StoryParser"));
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        schema.AddEnum(new EnumDefinition
        {
            Name = "StoryRewardType", Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "ZOOM_IN", Params = [] }]
        });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema, registry);

        const string uri = "file:///StoryPlots.xml";
        const string xml = "<StoryParser><Event>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "<Reward_Type>ZOOM_IN</Reward_Type>" +
                           "<Reward_Param1>extra</Reward_Param1>" +
                           "</Event></StoryParser>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.Contains(storyDiags, d => d.Message.Contains("Reward_Param1") && d.Message.Contains("ZOOM_IN"));
    }

    [Fact]
    public void OnIndexChanged_NonStoryParserDocument_EmitsNoStoryDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "Faction" });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///Factions.xml";
        const string xml = "<Faction>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "</Faction>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.DoesNotContain(storyDiags, d => d.Message.Contains("is not used by") || d.Message.Contains("requires"));
    }

    [Fact]
    public void OnIndexChanged_StoryParserDocument_RegistryMatch_ArbitraryRootElement_EmitsStoryDiagnostics()
    {
        var registry = new FakeFileTypeRegistry();
        const string uri = "file:///StoryPlots.xml";
        registry.Register("storyplots.xml", ImmutableArray.Create("StoryParser"));
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        schema.AddEnum(new EnumDefinition
        {
            Name = "StoryRewardType", Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "ZOOM_IN", Params = [] }]
        });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema, registry);

        const string xml = "<SomeContainer><Event>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "<Reward_Type>ZOOM_IN</Reward_Type>" +
                           "<Reward_Param1>extra</Reward_Param1>" +
                           "</Event></SomeContainer>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.Contains(storyDiags, d => d.Message.Contains("Reward_Param1") && d.Message.Contains("ZOOM_IN"));
    }

    [Fact]
    public void OnIndexChanged_StoryParserRootElement_NotInRegistry_NoStoryDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///StoryPlots.xml";
        const string xml = "<StoryParser><Event>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "<Reward_Type>ZOOM_IN</Reward_Type>" +
                           "<Reward_Param1>extra</Reward_Param1>" +
                           "</Event></StoryParser>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.DoesNotContain(storyDiags, d => d.Message.Contains("Reward_Param1"));
    }

    // ── tag Notes hints ──────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_TagWithNotes_EmitsHintDiagnostic()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(new XmlTagDefinition
        {
            Tag = "Old_Tag",
            ValueType = XmlValueType.Float,
            Notes = new Dictionary<string, string> { ["en"] = "Never used in vanilla." }
        });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///test.xml";
        const string xml = "<Root><Old_Tag>500</Old_Tag></Root>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var diags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(diags);
        Assert.Contains(diags,
            d => d.Severity == DiagnosticSeverity.Hint && d.Message.Contains("Never used in vanilla."));
    }

    // ── URI normalization ────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_MixedCaseWorkspaceUri_IndexProducerStillEmitsDuplicateDiagnostic()
    {
        // Workspace host stores the raw LSP URI — VS Code may send mixed case on Windows.
        // The index (from GameIndexService) stores canonical lowercase URIs. The publisher
        // must normalize before looking up in the index.
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///A.xml", "<Unit><Name>UNIT_A</Name></Unit>");
        workspaceHost.Set("file:///b.xml", "<Unit><Name>UNIT_A</Name></Unit>");
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        indexService.Fire(index);

        var pub = published.FirstOrDefault(p => p.Uri.ToString() == "file:///A.xml");
        Assert.NotNull(pub);
        Assert.Contains(pub.Diagnostics!, d => d.Message.Contains("UNIT_A"));
    }

    // ── closed-file suppression ──────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_ClosedFileWithDuplicateId_PublishesNothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        indexService.Fire(index);

        Assert.Empty(published);
    }

    [Fact]
    public void OnIndexChanged_OpenFileWithDuplicateId_PublishesDuplicateIdDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        workspaceHost.Set("file:///a.xml", "<Unit><Name>UNIT_A</Name></Unit>");
        workspaceHost.Set("file:///b.xml", "<Unit><Name>UNIT_A</Name></Unit>");

        indexService.Fire(index);

        Assert.Equal(2, published.Count);
        Assert.All(published, p => Assert.Contains(p.Diagnostics!, d => d.Message.Contains("UNIT_A")));
    }

    [Fact]
    public void OnIndexChanged_ClosedFileWithUnresolvedRef_PublishesNothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();
        var index = IndexWithRef("file:///a.xml", "UNIT_MISSING");

        indexService.Fire(index);

        Assert.Empty(published);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, EnumDefinition> _enums = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string name)
        {
            return _tags.GetValueOrDefault(name);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _types.GetValueOrDefault(name);
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [.. _types.Values];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string name)
        {
            return _enums.GetValueOrDefault(name);
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];

        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void AddTag(XmlTagDefinition tag)
        {
            _tags[tag.Tag] = tag;
        }

        public void AddType(GameObjectTypeDefinition type)
        {
            _types[type.TypeName] = type;
        }

        public void AddEnum(EnumDefinition enumDef)
        {
            _enums[enumDef.Name] = enumDef;
        }
    }

    private sealed class FakeGameIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        public void Fire(GameIndex index)
        {
            IndexChanged?.Invoke(index);
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeFileTypeRegistry : IFileTypeRegistry
    {
        private readonly Dictionary<string, ImmutableArray<string>> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public ImmutableArray<string> GetTypesForFile(string normalizedPath)
        {
            return _map.TryGetValue(normalizedPath, out var types) ? types : ImmutableArray<string>.Empty;
        }

        public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
        {
            _map[normalizedPath] = typeNames;
        }

        public void UnregisterFile(string normalizedPath)
        {
            _map.Remove(normalizedPath);
        }

        public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;

        public void Register(string key, ImmutableArray<string> types)
        {
            _map["file:///" + key] = types;
        }
    }

    private sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version)
        {
            Set(uri, text, version);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;

        public void Set(string uri, string text, int version = 1)
        {
            _docs[uri] = new TrackedDocument(uri, text, version);
        }
    }
}