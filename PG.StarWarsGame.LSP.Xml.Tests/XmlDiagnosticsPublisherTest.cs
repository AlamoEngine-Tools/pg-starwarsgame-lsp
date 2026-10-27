// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlDiagnosticsPublisherTest
{
    // ── XML strictness ──────────────────────────────────────────────────────

    private const string StrayAmpersandDoc = "<?xml version=\"1.0\"?>\n<Root>\n\t<A>Tom & Jerry</A>\n</Root>";

    private const string DroppedFileDoc = "<?xml version=\"1.0\"?>\n<Root>\n\t<A>1</B>\n</Root>";
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

    // ReSharper disable UnusedTupleComponentInReturnValue - the helpers return everything a test might need; some tests ignore a part
    private static (XmlDiagnosticsPublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeGameIndexService indexService,
        FakeGameWorkspaceHost workspaceHost) BuildSubscribed(FakeSchemaProvider? schema = null,
            FakeFileTypeRegistry? registry = null,
            ILspConfigurationProvider? config = null,
            IStoryChainProblemStore? storyChainProblems = null,
            bool withStructure = false,
            IMessageCatalog? messages = null)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var effectiveSchema = schema ?? new SchemaProviderBuilder().Build();
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
            new StoryParamUnknownSlotHandler()
        ];
        // Opt-in: most fixtures here are bare fragments without an XML declaration, which the
        // game's reader rejects; only the structure tests want that reported.
        if (withStructure) handlers = [.. handlers, new XmlStructureHandler()];

        var fileHelper = new FileHelper(new MockFileSystem());
        var publisher = XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(published),
            indexService,
            workspaceHost,
            effectiveSchema,
            new XmlDiagnosticsHandlerRegistry(handlers),
            new XmlDocumentFactProducer(fileHelper, effectiveSchema, effectiveRegistry, new XmlStructuralValidator()),
            new XmlIndexFactProducer(DistinctObjectNameHash.Instance),
            new StoryFactProducer(effectiveSchema),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            effectiveRegistry,
            fileHelper,
            configProvider: config,
            storyChainProblems: storyChainProblems,
            messages: messages);
        return (publisher, published, indexService, workspaceHost);
    }

    private static (XmlDiagnosticsPublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeGameIndexService indexService,
        FakeGameWorkspaceHost workspaceHost) BuildSubscribedWithHandlers(
            FakeSchemaProvider schema, IXmlDiagnosticsHandler[] handlers)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var registry = new FakeFileTypeRegistry();
        var fileHelper = new FileHelper(new MockFileSystem());
        var publisher = XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(published),
            indexService,
            workspaceHost,
            schema,
            new XmlDiagnosticsHandlerRegistry(handlers),
            new XmlDocumentFactProducer(fileHelper, schema, registry, new XmlStructuralValidator()),
            new XmlIndexFactProducer(DistinctObjectNameHash.Instance),
            new StoryFactProducer(schema),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            registry,
            fileHelper);
        return (publisher, published, indexService, workspaceHost);
    }

    // ── story-chain problems ─────────────────────────────────────────────────

    [Fact]
    public async Task RevalidateDocument_StoryChainProblem_IsPublishedAsDiagnostic()
    {
        var store = new StoryChainProblemStore();
        store.Replace([
            new StoryChainProblem("Campaigns_Test.xml", "file:///a.xml", 2, 4, 2, 24,
                StoryChainProblemKind.UnresolvedStoryName, "Story_Plots_Gone.xml",
                new CatalogueText("StoryChain_PlotFileMissing", "Story_Plots_Gone.xml"),
                XmlDiagnosticSeverity.Error)
        ]);
        var (publisher, published, _, workspaceHost) = BuildSubscribed(storyChainProblems: store);
        workspaceHost.With("file:///a.xml",
            "<Campaigns>\n<Campaign Name=\"T\">\n    <Rebel_Story_Name>Story_Plots_Gone.xml</Rebel_Story_Name>\n</Campaign>\n</Campaigns>");

        await publisher.RevalidateDocumentAsync("file:///a.xml", CancellationToken.None);

        var diag = Assert.Single(Assert.Single(published).Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(2, diag.Range.Start.Line);
        Assert.Equal(4, diag.Range.Start.Character);
        Assert.Equal(24, diag.Range.End.Character);
        Assert.Contains("Story_Plots_Gone.xml", diag.Message);
    }

    [Fact]
    public async Task RevalidateDocument_StoryChainProblemForOtherDocument_IsNotPublished()
    {
        var store = new StoryChainProblemStore();
        store.Replace([
            new StoryChainProblem("Campaigns_Other.xml", "file:///other.xml", 0, 0, 0, 5,
                StoryChainProblemKind.UnresolvedStoryName, "X", new CatalogueText("StoryChain_PlotFileMissing", "X"),
                XmlDiagnosticSeverity.Error)
        ]);
        var (publisher, published, _, workspaceHost) = BuildSubscribed(storyChainProblems: store);
        workspaceHost.With("file:///a.xml", "<Campaigns/>");

        await publisher.RevalidateDocumentAsync("file:///a.xml", CancellationToken.None);

        Assert.Empty(Assert.Single(published).Diagnostics);
    }

    // ── subscription / routing ───────────────────────────────────────────────

    [Fact]
    public void IndexChanged_Fires_Publishes_For_Each_OpenDocument()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.With("file:///a.xml", "<Root/>");

        indexService.Raise(IndexWithDoc("file:///a.xml"));

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_TwoOpenDocuments_PublishesTwice()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.With("file:///a.xml", "<Root/>");
        workspaceHost.With("file:///b.xml", "<Root/>");

        var index = IndexWithDoc("file:///a.xml") with
        {
            Documents = IndexWithDoc("file:///a.xml").Documents
                .Add("file:///b.xml", new DocumentIndex("file:///b.xml", 1, [], []))
        };
        indexService.Raise(index);

        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void IndexChanged_NoOpenDocuments_PublishesNothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();

        indexService.Raise(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void IndexChanged_DocumentClosed_ClearsDiagnostics_For_That_Uri()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.With("file:///a.xml", "<Root/>");

        indexService.Raise(IndexWithDoc("file:///a.xml"));
        published.Clear();

        workspaceHost.Remove("file:///a.xml");
        indexService.Raise(GameIndex.Empty);

        var clear = Assert.Single(published);
        Assert.Equal("file:///a.xml", clear.Uri.ToString());
        Assert.Empty(clear.Diagnostics);
    }

    [Fact]
    public void IndexChanged_LuaFileOpen_DoesNotPublishForLuaFile()
    {
        // Lua files may be in the workspace host (opened via LuaTextDocumentSyncHandler).
        // The XML publisher must ignore them - they are handled by LuaDiagnosticsPublisher.
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.With("file:///script.lua", "function Definitions() end");

        indexService.Raise(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void IndexChanged_OnlyPublishesForOpenFiles_NotForIndexOnlyEntries()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.With("file:///a.xml", "<Root/>");

        var indexWithBoth = IndexWithDoc("file:///a.xml") with
        {
            Documents = IndexWithDoc("file:///a.xml").Documents
                .Add("a.xml", new DocumentIndex("a.xml", 0, [], []))
        };
        indexService.Raise(indexWithBoth);

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_ScannerFiresAfterOpen_StillPublishesForOpenFile()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.With("file:///a.xml", "<Root/>");

        indexService.Raise(IndexWithDoc("file:///a.xml"));
        published.Clear();

        var scanIndex = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("a.xml", new DocumentIndex("a.xml", 0, [], []))
        };
        indexService.Raise(scanIndex);

        var pub = Assert.Single(published);
        Assert.Equal("file:///a.xml", pub.Uri.ToString());
    }

    // ── story parser guard ───────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_StoryParserDocument_EmitsStoryDiagnostics()
    {
        var registry = new FakeFileTypeRegistry();
        registry.Register("storyplots.xml", ImmutableArray.Create("StoryParser"));
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        schema.Add(new EnumDefinition
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

        workspaceHost.With(uri, xml);
        indexService.Raise(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.Contains(storyDiags, d => d.Message.Contains("Reward_Param1") && d.Message.Contains("ZOOM_IN"));
    }

    [Fact]
    public void OnIndexChanged_NonStoryParserDocument_EmitsNoStoryDiagnostics()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new GameObjectTypeDefinition { TypeName = "Faction" });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///Factions.xml";
        const string xml = "<Faction>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "</Faction>";

        workspaceHost.With(uri, xml);
        indexService.Raise(IndexWithDoc(uri));

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
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        schema.Add(new EnumDefinition
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

        workspaceHost.With(uri, xml);
        indexService.Raise(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.Contains(storyDiags, d => d.Message.Contains("Reward_Param1") && d.Message.Contains("ZOOM_IN"));
    }

    [Fact]
    public void OnIndexChanged_StoryParserRootElement_NotInRegistry_NoStoryDiagnostics()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///StoryPlots.xml";
        const string xml = "<StoryParser><Event>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "<Reward_Type>ZOOM_IN</Reward_Type>" +
                           "<Reward_Param1>extra</Reward_Param1>" +
                           "</Event></StoryParser>";

        workspaceHost.With(uri, xml);
        indexService.Raise(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.DoesNotContain(storyDiags, d => d.Message.Contains("Reward_Param1"));
    }

    // ── tag Notes hints ──────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_TagWithNotes_EmitsHintDiagnostic()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new XmlTagDefinition
        {
            Tag = "Old_Tag",
            ValueType = XmlValueType.Float,
            Notes =
            [
                new SchemaNote(SchemaNoteKind.Remark,
                    new Dictionary<string, string> { ["en"] = "Never used in vanilla." })
            ]
        });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///test.xml";
        const string xml = "<Root><Old_Tag>500</Old_Tag></Root>";

        workspaceHost.With(uri, xml);
        indexService.Raise(IndexWithDoc(uri));

        var diags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(diags);
        Assert.Contains(diags,
            d => d.Severity == DiagnosticSeverity.Hint && d.Message.Contains("Never used in vanilla."));
    }

    // ── URI normalization ────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_MixedCaseWorkspaceUri_IndexProducerStillEmitsDuplicateDiagnostic()
    {
        // Workspace host stores the raw LSP URI - VS Code may send mixed case on Windows.
        // The index (from GameIndexService) stores canonical lowercase URIs. The publisher
        // must normalize before looking up in the index.
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.With("file:///A.xml", "<Unit><Name>UNIT_A</Name></Unit>");
        workspaceHost.With("file:///b.xml", "<Unit><Name>UNIT_A</Name></Unit>");
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        indexService.Raise(index);

        var pub = published.FirstOrDefault(p => p.Uri.ToString() == "file:///A.xml");
        Assert.NotNull(pub);
        Assert.Contains(pub.Diagnostics, d => d.Message.Contains("UNIT_A"));
    }

    [Fact]
    public void Strictness_ReportsAStrictOnlyFindingAsAWarning()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed(withStructure: true);
        workspaceHost.With("file:///a.xml", StrayAmpersandDoc);

        indexService.Raise(IndexWithDoc("file:///a.xml"));

        var d = Assert.Single(published.Last().Diagnostics,
            x => x.Code?.String == DiagnosticIds.XmlStrayAmpersand.ToString());
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(2, d.Range.Start.Line);
    }

    [Fact]
    public void Strictness_AFileTheGameDrops_IsAnError()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed(withStructure: true);
        workspaceHost.With("file:///a.xml", DroppedFileDoc);

        indexService.Raise(IndexWithDoc("file:///a.xml"));

        var d = Assert.Single(published.Last().Diagnostics,
            x => x.Code?.String == DiagnosticIds.XmlEndTagMismatch.ToString());
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Strictness_SuppressionCommentSilencesTheWarning()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed(withStructure: true);
        var doc = "<?xml version=\"1.0\"?>\n<Root>\n\t<!-- aetswg:suppress " + DiagnosticIds.XmlStrayAmpersand +
                  " -->\n\t<A>Tom & Jerry</A>\n</Root>";
        workspaceHost.With("file:///a.xml", doc);

        indexService.Raise(IndexWithDoc("file:///a.xml"));

        Assert.DoesNotContain(published.Last().Diagnostics,
            x => x.Code?.String == DiagnosticIds.XmlStrayAmpersand.ToString());
    }

    // ── suppression directives ───────────────────────────────────────────────

    private static GameIndex IndexWithDuplicateAtLine(string id, string uri1, string uri2, int line)
    {
        var sym1 = new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri1, line, null), null);
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

    [Fact]
    public void OnIndexChanged_SuppressAnnotation_Before_Duplicate_Symbol_Suppresses_Diagnostic()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        // Symbol is at line 1; suppression comment is at line 0.
        const string xml = "<!-- aetswg:suppress aetswg-010-0001 -->\n<Unit Name=\"Default\"/>";
        workspaceHost.With("file:///a.xml", xml);
        workspaceHost.With("file:///b.xml", "<Unit Name=\"Default\"/>");
        var index = IndexWithDuplicateAtLine("Default", "file:///a.xml", "file:///b.xml", 1);

        indexService.Raise(index);

        var pub = published.FirstOrDefault(p => p.Uri.ToString() == "file:///a.xml");
        Assert.NotNull(pub);
        Assert.DoesNotContain(pub.Diagnostics, d => d.Message.Contains("Duplicate"));
    }

    [Fact]
    public void OnIndexChanged_SuppressAnnotation_Only_Suppresses_That_File()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        const string xmlA = "<!-- aetswg:suppress aetswg-010-0001 -->\n<Unit Name=\"Default\"/>";
        const string xmlB = "<Unit Name=\"Default\"/>";
        workspaceHost.With("file:///a.xml", xmlA);
        workspaceHost.With("file:///b.xml", xmlB);
        var index = IndexWithDuplicateAtLine("Default", "file:///a.xml", "file:///b.xml", 1);

        indexService.Raise(index);

        var pubB = published.FirstOrDefault(p => p.Uri.ToString() == "file:///b.xml");
        Assert.NotNull(pubB);
        Assert.Contains(pubB.Diagnostics, d => d.Message.Contains("Duplicate"));
    }

    // ── closed-file suppression ──────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_ClosedFileWithDuplicateId_PublishesNothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        indexService.Raise(index);

        Assert.Empty(published);
    }

    [Fact]
    public void OnIndexChanged_OpenFileWithDuplicateId_PublishesDuplicateIdDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        workspaceHost.With("file:///a.xml", "<Unit><Name>UNIT_A</Name></Unit>");
        workspaceHost.With("file:///b.xml", "<Unit><Name>UNIT_A</Name></Unit>");

        indexService.Raise(index);

        Assert.Equal(2, published.Count);
        Assert.All(published, p => Assert.Contains(p.Diagnostics, d => d.Message.Contains("UNIT_A")));
    }

    /// <summary>
    ///     A diagnostic's text comes from the ONE injected catalogue (#306): the context a handler gets
    ///     carries the publisher's catalogue, not one built from the locale per run.
    /// </summary>
    [Fact]
    public void OnIndexChanged_TheDiagnosticsSpeakTheInjectedCatalogue()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed(messages: new MarkerMessageCatalog());
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");
        workspaceHost.With("file:///a.xml", "<Unit><Name>UNIT_A</Name></Unit>");

        indexService.Raise(index);

        var diagnostic = Assert.Single(Assert.Single(published).Diagnostics);
        Assert.Equal("[DuplicateSymbol_DuplicateSymbolAlsoDefinedIn]", diagnostic.Message);
    }

    [Fact]
    public void OnIndexChanged_ClosedFileWithUnresolvedRef_PublishesNothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();
        var index = IndexWithRef("file:///a.xml", "UNIT_MISSING");

        indexService.Raise(index);

        Assert.Empty(published);
    }

    // ── SuggestedFix → Diagnostic.Data pipeline ─────────────────────────────

    [Fact]
    public void ToLspDiagnostic_WhenHandlerReturnsSuggestedFix_EmbedsFix_In_DiagnosticData()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new XmlTagDefinition { Tag = "Priority", ValueType = XmlValueType.Int });
        var (_, published, indexService, workspaceHost) =
            BuildSubscribedWithHandlers(schema, [new SuggestedFixStubHandler()]);

        workspaceHost.With("file:///test.xml", "<Root><Priority>1.5</Priority></Root>");
        indexService.Raise(IndexWithDoc("file:///test.xml"));

        var diags = published.Single().Diagnostics.ToList();
        var diag = Assert.Single(diags);
        Assert.Equal("42", diag.Data?["fix"]?.Value<string>());
    }

    [Fact]
    public void ToLspDiagnostic_WhenHandlerReturnsNoFix_DiagnosticDataIsNull()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new XmlTagDefinition { Tag = "Priority", ValueType = XmlValueType.Int });
        var (_, published, indexService, workspaceHost) =
            BuildSubscribedWithHandlers(schema, [new NoFixStubHandler()]);

        workspaceHost.With("file:///test.xml", "<Root><Priority>1.5</Priority></Root>");
        indexService.Raise(IndexWithDoc("file:///test.xml"));

        var diag = Assert.Single(published.Single().Diagnostics);
        Assert.Null(diag.Data);
    }

    [Fact]
    public void ToLspDiagnostic_WhenHandlerReturnsUnnecessaryTag_SetsDiagnosticTag_And_RemovalData()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new XmlTagDefinition { Tag = "Priority", ValueType = XmlValueType.Int });
        var (_, published, indexService, workspaceHost) =
            BuildSubscribedWithHandlers(schema, [new UnnecessaryRemovableStubHandler()]);

        workspaceHost.With("file:///test.xml", "<Root><Priority>1.5</Priority></Root>");
        indexService.Raise(IndexWithDoc("file:///test.xml"));

        var diag = Assert.Single(published.Single().Diagnostics);
        Assert.NotNull(diag.Tags);
        Assert.Contains(DiagnosticTag.Unnecessary, diag.Tags!);
        Assert.True(diag.Data?["removeRedundantOverride"]?.Value<bool>());
    }

    [Fact]
    public void ToLspDiagnostic_WhenHandlerReturnsCreateLocKey_EmbedsCreateLocKey_In_DiagnosticData()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new XmlTagDefinition { Tag = "TextId", ValueType = XmlValueType.NameReference });
        var (_, published, indexService, workspaceHost) =
            BuildSubscribedWithHandlers(schema, [new CreateLocKeyStubHandler()]);

        workspaceHost.With("file:///test.xml", "<Root><TextId>TEXT_FOO</TextId></Root>");
        indexService.Raise(IndexWithDoc("file:///test.xml"));

        var diag = Assert.Single(published.Single().Diagnostics);
        Assert.Equal("TEXT_FOO", diag.Data?["createLocKey"]?.Value<string>());
    }

    [Fact]
    public void ToLspDiagnostic_WhenHandlerReturnsBothFixAndCreateLocKey_BothEmbeddedInData()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new XmlTagDefinition { Tag = "TextId", ValueType = XmlValueType.NameReference });
        var (_, published, indexService, workspaceHost) =
            BuildSubscribedWithHandlers(schema, [new FixAndCreateLocKeyStubHandler()]);

        workspaceHost.With("file:///test.xml", "<Root><TextId>TEXT_FOO</TextId></Root>");
        indexService.Raise(IndexWithDoc("file:///test.xml"));

        var diag = Assert.Single(published.Single().Diagnostics);
        Assert.Equal("suggested_fix", diag.Data?["fix"]?.Value<string>());
        Assert.Equal("TEXT_FOO", diag.Data?["createLocKey"]?.Value<string>());
    }

    // ── cross-line ranges (result override) ──────────────────────────────────

    [Fact]
    public void ToLspDiagnostic_WhenResultOverridesEndPosition_ProducesCrossLineRange()
    {
        var schema = new SchemaProviderBuilder().Build();
        schema.Add(new XmlTagDefinition { Tag = "Priority", ValueType = XmlValueType.Int });
        var (_, published, indexService, workspaceHost) =
            BuildSubscribedWithHandlers(schema, [new EndPositionOverrideStubHandler()]);

        workspaceHost.With("file:///test.xml", "<Root>\n<Priority>1.5</Priority>\n</Root>");
        indexService.Raise(IndexWithDoc("file:///test.xml"));

        var diag = Assert.Single(published.Single().Diagnostics);
        Assert.Equal(1, diag.Range.Start.Line);
        Assert.Equal(3, diag.Range.End.Line);
        Assert.Equal(7, diag.Range.End.Character);
    }

    [Fact]
    public void ToLspDiagnostic_WhenFactCarriesEndPosition_HandlerNeedNotOverride_ProducesCrossLineRange()
    {
        // Proves the fact-level fallthrough path used by VariantRedundantOverrideFact: a producer
        // that knows a node's full span sets EndLine/EndColumn once on the fact, and a handler that
        // never touches position at all (returns a plain XmlDiagnosticResult) still gets a correct
        // cross-line Range.
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var fileHelper = new FileHelper(new MockFileSystem());
        _ = XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(published),
            indexService,
            workspaceHost,
            new SchemaProviderBuilder().Build(),
            new XmlDiagnosticsHandlerRegistry([new PlainResultForSpanFactHandler()]),
            new SpanFactStubDocumentProducer(),
            new StubIndexFactProducerForSpan(),
            new StoryFactProducer(new SchemaProviderBuilder().Build()),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            new FakeFileTypeRegistry(),
            fileHelper);

        workspaceHost.With("file:///test.xml", "irrelevant text");
        indexService.Raise(IndexWithDoc("file:///test.xml"));

        var diag = Assert.Single(published.Single().Diagnostics);
        Assert.Equal(2, diag.Range.Start.Line);
        Assert.Equal(4, diag.Range.Start.Character);
        Assert.Equal(6, diag.Range.End.Line);
        Assert.Equal(9, diag.Range.End.Character);
    }

    // ── revalidation ────────────────────────────────────────────────────────────

    private static (XmlDiagnosticsPublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeGameIndexService indexService,
        FakeGameWorkspaceHost workspaceHost) BuildSubscribedWithFs(MockFileSystem fs)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var schema = new SchemaProviderBuilder().Build();
        var registry = new FakeFileTypeRegistry();
        var fileHelper = new FileHelper(fs);

        IXmlDiagnosticsHandler[] handlers = [];
        var publisher = XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(published),
            indexService,
            workspaceHost,
            schema,
            new XmlDiagnosticsHandlerRegistry(handlers),
            new XmlDocumentFactProducer(fileHelper, schema, registry, new XmlStructuralValidator()),
            new XmlIndexFactProducer(DistinctObjectNameHash.Instance),
            new StoryFactProducer(schema),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            registry,
            fileHelper);
        return (publisher, published, indexService, workspaceHost);
    }

    [Fact]
    public async Task RevalidateDocument_OpenFile_PublishesUsingInMemoryText()
    {
        const string uri = "file:///test.xml";
        var fs = new MockFileSystem();
        var (publisher, published, indexService, workspaceHost) = BuildSubscribedWithFs(fs);
        indexService.Current = IndexWithDoc(uri);
        workspaceHost.With(uri, "<Root/>");

        await publisher.RevalidateDocumentAsync(uri, CancellationToken.None);

        var pub = Assert.Single(published);
        Assert.Equal(uri, pub.Uri.ToString());
    }

    [Fact]
    public async Task RevalidateDocument_ClosedFile_ReadsFromDiskAndPublishes()
    {
        const string uri = "file:///test.xml";
        const string path = "/test.xml";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [path] = new("<Root/>")
        });
        var (publisher, published, indexService, _) = BuildSubscribedWithFs(fs);
        indexService.Current = IndexWithDoc(uri);

        await publisher.RevalidateDocumentAsync(uri, CancellationToken.None);

        var pub = Assert.Single(published);
        Assert.Equal(uri, pub.Uri.ToString());
    }

    [Fact]
    public async Task RevalidateWorkspace_PublishesForAllIndexedUris()
    {
        const string uri1 = "file:///a.xml";
        const string uri2 = "file:///b.xml";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/a.xml"] = new("<Root/>"),
            ["/b.xml"] = new("<Root/>")
        });
        var (publisher, published, indexService, _) = BuildSubscribedWithFs(fs);

        var docA = new DocumentIndex(uri1, 1, [], []);
        var docB = new DocumentIndex(uri2, 1, [], []);
        indexService.Current = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(uri1, docA).Add(uri2, docB),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        await publisher.RevalidateWorkspaceAsync(CancellationToken.None);

        Assert.Equal(2, published.Count);
        Assert.Contains(published, p => p.Uri.ToString() == uri1);
        Assert.Contains(published, p => p.Uri.ToString() == uri2);
    }

    /// <summary>
    ///     The index holds every language's documents. A sweep that read them all as XML put "No XML
    ///     declaration" on every Lua script of the project.
    /// </summary>
    [Fact]
    public async Task RevalidateWorkspace_SkipsDocumentsThatAreNotXml()
    {
        const string xml = "file:///a.xml";
        const string lua = "file:///script.lua";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/a.xml"] = new("<Root/>"),
            ["/script.lua"] = new("function Definitions() end")
        });
        var (publisher, published, indexService, _) = BuildSubscribedWithFs(fs);

        indexService.Current = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(xml, new DocumentIndex(xml, 1, [], []))
                .Add(lua, new DocumentIndex(lua, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        await publisher.RevalidateWorkspaceAsync(CancellationToken.None);

        Assert.Contains(published, p => p.Uri.ToString() == xml);
        Assert.DoesNotContain(published, p => p.Uri.ToString() == lua);
    }

    [Fact]
    public async Task RevalidateWorkspace_Clears_Diagnostics_For_Previously_Published_Uris_Not_In_Index()
    {
        const string uri = "file:///stale.xml";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/stale.xml"] = new("<Root/>")
        });
        var (publisher, published, indexService, workspaceHost) = BuildSubscribedWithFs(fs);

        // First: simulate that this URI got diagnostics via index change
        workspaceHost.With(uri, "<Root/>");
        indexService.Raise(IndexWithDoc(uri)); // records uri in _lastPublishedUris
        published.Clear();

        // Now remove it from the index - uri no longer indexed
        indexService.Current = GameIndex.Empty;

        // Revalidate: should clear the stale uri
        await publisher.RevalidateWorkspaceAsync(CancellationToken.None);

        var clear = Assert.Single(published, p => p.Uri.ToString() == uri);
        Assert.Empty(clear.Diagnostics);
    }

    private sealed record SpanFact(
        string DocumentUri,
        int Line,
        int Column,
        int Length,
        int? EndLine,
        int? EndColumn) : XmlFact(DocumentUri, Line, Column, Length, EndLine, EndColumn);

    private sealed class SpanFactStubDocumentProducer : IXmlDocumentFactProducer
    {
        public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri)
        {
            return [new SpanFact(documentUri, 2, 4, 3, 6, 9)];
        }
    }

    private sealed class StubIndexFactProducerForSpan : IXmlIndexFactProducer
    {
        public IReadOnlyList<XmlFact> Produce(string documentUri, GameIndex index)
        {
            return [];
        }
    }

    private sealed class PlainResultForSpanFactHandler : XmlDiagnosticsHandler<SpanFact>
    {
        protected override IEnumerable<XmlDiagnosticResult> Handle(SpanFact fact, DiagnosticsContext ctx)
        {
            return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, "whole-node span")];
        }
    }

    private sealed class EndPositionOverrideStubHandler : XmlDiagnosticsHandler<XmlTagValueFact>
    {
        protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
        {
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, "spans multiple lines",
                    OverrideEndLine: 3, OverrideEndColumn: 7)
            ];
        }
    }

    private sealed class SuggestedFixStubHandler : XmlDiagnosticsHandler<XmlTagValueFact>
    {
        protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
        {
            return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "stub warning", SuggestedFix: "42")];
        }
    }

    private sealed class NoFixStubHandler : XmlDiagnosticsHandler<XmlTagValueFact>
    {
        protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
        {
            return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "stub warning no fix")];
        }
    }

    private sealed class CreateLocKeyStubHandler : XmlDiagnosticsHandler<XmlTagValueFact>
    {
        protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
        {
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "missing loc key",
                    CreateLocalisationKey: fact.RawValue)
            ];
        }
    }

    private sealed class FixAndCreateLocKeyStubHandler : XmlDiagnosticsHandler<XmlTagValueFact>
    {
        protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
        {
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, "both set",
                    SuggestedFix: "suggested_fix", CreateLocalisationKey: fact.RawValue)
            ];
        }
    }

    private sealed class UnnecessaryRemovableStubHandler : XmlDiagnosticsHandler<XmlTagValueFact>
    {
        protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
        {
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Hint, "redundant override",
                    Tags: [XmlDiagnosticTag.Unnecessary], RemoveRedundantOverride: true)
            ];
        }
    }

    // ── fakes ────────────────────────────────────────────────────────────────

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
}