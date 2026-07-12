// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class ExecuteStoryCommandHandlerTest
{
    private static string Rooted(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static readonly string XmlDir = Path.Combine(Rooted("ws"), "data", "xml");
    private static readonly string DepXmlDir = Path.Combine(Rooted("dep"), "data", "xml");

    private static readonly string ThreadUri;
    private static readonly string DepThreadUri;
    private static readonly string ManifestUri;

    static ExecuteStoryCommandHandlerTest()
    {
        var fh = new FileHelper(new MockFileSystem());
        ThreadUri = fh.NormalizeUri(Path.Combine(XmlDir, "story_main.xml"));
        DepThreadUri = fh.NormalizeUri(Path.Combine(DepXmlDir, "story_dep.xml"));
        ManifestUri = fh.NormalizeUri(Path.Combine(XmlDir, "story_plots_r.xml"));
    }

    private const string ThreadText =
        "<Story>\n" +
        "\t<Event Name=\"Start\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Next\">\n" +
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
        "\t\t<Prereq>Start</Prereq>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    private const string ManifestText =
        "<Story_Mode_Plots>\n" +
        "\t<Active_Plot>story_main.xml</Active_Plot>\n" +
        "</Story_Mode_Plots>\n";

    private const string CampaignSetText =
        "<Campaigns>\n" +
        "\t<Campaign Name=\"GC\">\n" +
        "\t\t<Rebel_Story_Name>story_plots_r.xml</Rebel_Story_Name>\n" +
        "\t</Campaign>\n" +
        "</Campaigns>\n";

    private sealed class CapturingApplier(bool result = true) : IWorkspaceEditApplier
    {
        public WorkspaceEdit? Edit { get; private set; }
        public string? Label { get; private set; }

        public Task<bool> ApplyAsync(WorkspaceEdit edit, string label, CancellationToken ct)
        {
            Edit = edit;
            Label = label;
            return Task.FromResult(result);
        }
    }

    private static ExecuteStoryCommandHandler Handler(
        CapturingApplier applier, bool storyEditor = true, GameIndex? index = null)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(XmlDir, "story_main.xml")] = new(ThreadText),
            [Path.Combine(DepXmlDir, "story_dep.xml")] = new(ThreadText),
            [Path.Combine(XmlDir, "story_plots_r.xml")] = new(ManifestText),
            [Path.Combine(XmlDir, "campaigns_test.xml")] = new(CampaignSetText)
        });
        var fileHelper = new FileHelper(fs);
        var config = FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Tools = new ToolsFeatureFlags { StoryEditor = storyEditor },
            Story = new StoryFeatureFlags { Discovery = true }
        });
        return new ExecuteStoryCommandHandler(
            new StubModelService(),
            new StubIndexService(index ?? DefaultIndex()),
            new DocumentTextSource(new FakeHost(), fileHelper, NullLogger<DocumentTextSource>.Instance),
            new EmptySchema(),
            fileHelper,
            new StubReloadService(WorkspaceConfiguration.Empty with
            {
                XmlDirectories = [DepXmlDir, XmlDir]
            }),
            applier,
            config,
            NullLogger<ExecuteStoryCommandHandler>.Instance);
    }

    private static GameIndex DefaultIndex()
    {
        var documents = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(ThreadUri, new DocumentIndex(ThreadUri, 1, [], [], LayerRank: 1, LayerName: "Mod"))
            .Add(DepThreadUri, new DocumentIndex(DepThreadUri, 1, [], [], LayerRank: 0, LayerName: "Core Dependency"));
        return GameIndex.Empty with { Documents = documents };
    }

    private static ExecuteStoryCommandParams Command(string kind, string? threadUri = null,
        string? eventName = null, string? newName = null, string? value = null, bool? flag = null,
        int? groupIndex = null, string? token = null, string campaign = "GC", string? file = null,
        string? faction = null)
    {
        return new ExecuteStoryCommandParams(campaign, kind, threadUri ?? ThreadUri, eventName, newName,
            Value: value, Flag: flag, GroupIndex: groupIndex, Token: token, File: file, Faction: faction);
    }

    private static TextDocumentEdit SingleDocEdit(CapturingApplier applier)
    {
        var change = Assert.Single(applier.Edit!.DocumentChanges!);
        return change.TextDocumentEdit!;
    }

    // ── Gating & validation ──────────────────────────────────────────────────

    [Fact]
    public async Task StoryEditorOff_ReturnsDisabledMessage()
    {
        var result = await Handler(new CapturingApplier(), storyEditor: false)
            .Handle(Command("deleteEvent", eventName: "Start"), CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
    }

    [Fact]
    public async Task UnknownCampaign_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("deleteEvent", eventName: "Start", campaign: "Nope"), CancellationToken.None);

        Assert.Contains("Nope", result.Error);
    }

    [Fact]
    public async Task UnknownKind_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("explodeEvent", eventName: "Start"), CancellationToken.None);

        Assert.Contains("explodeEvent", result.Error);
    }

    [Fact]
    public async Task DependencyLayerThread_IsRejectedWithOverrideGuidance()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("deleteEvent", DepThreadUri, "Start"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("read-only", result.Error);
        Assert.Contains("Copy the file", result.Error);
    }

    [Fact]
    public async Task UnknownEvent_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("deleteEvent", eventName: "Ghost"), CancellationToken.None);

        Assert.Contains("Ghost", result.Error);
    }

    [Fact]
    public async Task CreateEvent_DuplicateNameAnywhereInCampaign_IsRejected()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("createEvent", newName: "Next"), CancellationToken.None);

        Assert.Contains("campaign-wide", result.Error);
    }

    [Fact]
    public async Task RemovePrereq_UnknownToken_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("removePrereq", eventName: "Next", groupIndex: 0, token: "Ghost"),
                CancellationToken.None);

        Assert.Contains("Ghost", result.Error);
    }

    [Fact]
    public async Task ApplierRejection_IsLoggedAndTheCommandStillSucceeds()
    {
        // The applyEdit round-trip is detached from the request: awaiting it would let the
        // edit's own didChange cancel this request with ContentModified. Rejections are rare
        // and logged, not surfaced.
        var applier = new CapturingApplier(result: false);

        var result = await Handler(applier)
            .Handle(Command("setEventType", eventName: "Start", value: "STORY_FLAG"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(applier.Edit);
    }

    [Fact]
    public async Task RemovePrereq_NoGroupIndex_RemovesFromAllGroups()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("removePrereq", eventName: "Next", token: "Start"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(applier.Edit);
    }

    [Fact]
    public async Task RemovePrereq_NoGroupIndex_UnknownToken_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("removePrereq", eventName: "Next", token: "Ghost"), CancellationToken.None);

        Assert.Contains("Ghost", result.Error);
    }

    // ── Node/edge happy paths ────────────────────────────────────────────────

    [Fact]
    public async Task SetEventType_AppliesMinimalEditToTheThread()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("setEventType", eventName: "Start", value: "STORY_FLAGS"), CancellationToken.None);

        Assert.True(result.Success);
        var docEdit = SingleDocEdit(applier);
        Assert.Equal(ThreadUri, docEdit.TextDocument.Uri.ToString());
        Assert.Equal("STORY_FLAGS", Assert.Single(docEdit.Edits).NewText);
    }

    [Fact]
    public async Task CreateEvent_AppliesInsertAndLabelsTheEdit()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("createEvent", newName: "Fresh") with { EventType = "STORY_TRIGGER" },
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Fresh", applier.Label);
        Assert.Contains("<Event Name=\"Fresh\">", Assert.Single(SingleDocEdit(applier).Edits).NewText);
    }

    [Fact]
    public async Task AddPrereq_NewLine_AppliesInsert()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("addPrereq", eventName: "Start", token: "Next"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("<Prereq>Next</Prereq>", Assert.Single(SingleDocEdit(applier).Edits).NewText);
    }

    [Fact]
    public async Task SetPerpetualFalse_WithoutExistingTag_ReportsNoChanges()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("setPerpetual", eventName: "Start", flag: false), CancellationToken.None);

        Assert.Contains("no changes", result.Error);
    }

    [Fact]
    public async Task CreateTacticalAttachment_CreatesEventWithManifestParam()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("createTacticalAttachment", newName: "Land_Battle",
                value: "land", file: "story_plots_tactical.xml"), CancellationToken.None);

        Assert.True(result.Success);
        var newText = Assert.Single(SingleDocEdit(applier).Edits).NewText;
        Assert.Contains("<Event_Type>STORY_LAND_TACTICAL</Event_Type>", newText);
        Assert.Contains("<Event_Param1>story_plots_tactical.xml</Event_Param1>", newText);
    }

    // ── Manifest ops ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateThread_AddsManifestEntryAndCreatesTheFile()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("createThread", file: "story_plots_r.xml", value: "story_new.xml"),
                CancellationToken.None);

        Assert.True(result.Success);
        var changes = applier.Edit!.DocumentChanges!.ToList();
        Assert.Equal(3, changes.Count);
        Assert.True(changes[0].IsCreateFile);
        Assert.EndsWith("story_new.xml", changes[0].CreateFile!.Uri.ToString());
        Assert.Contains("<Story>", Assert.Single(changes[1].TextDocumentEdit!.Edits).NewText);
        Assert.Contains("<Active_Plot>story_new.xml</Active_Plot>",
            Assert.Single(changes[2].TextDocumentEdit!.Edits).NewText);
    }

    [Fact]
    public async Task SetThreadState_Suspends_RetagsTheEntry()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("setThreadState", file: "story_plots_r.xml", value: "story_main.xml", flag: true),
                CancellationToken.None);

        Assert.True(result.Success);
        var docEdit = SingleDocEdit(applier);
        Assert.Equal(ManifestUri, docEdit.TextDocument.Uri.ToString());
        Assert.Contains("<Suspended_Plot>story_main.xml</Suspended_Plot>",
            Assert.Single(docEdit.Edits).NewText);
    }

    [Fact]
    public async Task DeleteThread_UnknownEntry_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("deleteThread", file: "story_plots_r.xml", value: "ghost.xml"),
                CancellationToken.None);

        Assert.Contains("not listed", result.Error);
    }

    [Fact]
    public async Task AttachLuaScript_AppendsEntry()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("attachLuaScript", file: "story_plots_r.xml", value: "Story_Script"),
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("<Lua_Script>Story_Script</Lua_Script>",
            Assert.Single(SingleDocEdit(applier).Edits).NewText);
    }

    // ── Campaign set ops ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddPlotManifest_InsertsStoryNameAndCreatesManifest()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("addPlotManifest", faction: "Empire", file: "story_plots_e.xml"),
                CancellationToken.None);

        Assert.True(result.Success);
        var changes = applier.Edit!.DocumentChanges!.ToList();
        Assert.Equal(3, changes.Count);
        Assert.True(changes[0].IsCreateFile);
        Assert.Contains("<Story_Mode_Plots>", Assert.Single(changes[1].TextDocumentEdit!.Edits).NewText);
        Assert.Contains("<Empire_Story_Name>story_plots_e.xml</Empire_Story_Name>",
            Assert.Single(changes[2].TextDocumentEdit!.Edits).NewText);
    }

    [Fact]
    public async Task RemovePlotManifest_UnknownManifest_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("removePlotManifest", file: "ghost.xml"), CancellationToken.None);

        Assert.Contains("not attached", result.Error);
    }

    // ── renameEvent delegation ───────────────────────────────────────────────

    [Fact]
    public async Task RenameEvent_NonStorySymbol_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("renameEvent", eventName: "Start", newName: "Renamed"), CancellationToken.None);

        Assert.Contains("not an indexed story symbol", result.Error);
    }

    [Fact]
    public async Task RenameEvent_AmbiguousName_SurfacesGuardObjection()
    {
        var sym1 = new GameSymbol("Start", GameSymbolKind.XmlObject, "StoryEvent",
            new FileOrigin(ThreadUri, 1, 13), null);
        var sym2 = new GameSymbol("Start", GameSymbolKind.XmlObject, "StoryEvent",
            new FileOrigin(DepThreadUri, 1, 13), null);
        var index = DefaultIndex() with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("Start", [sym1, sym2])
        };

        var result = await Handler(new CapturingApplier(), index: index)
            .Handle(Command("renameEvent", eventName: "Start", newName: "Renamed"), CancellationToken.None);

        Assert.Contains("2 story events", result.Error);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubModelService : IStoryModelService
    {
        private static readonly StoryCampaignModel Model = BuildModel();

        private static StoryCampaignModel BuildModel()
        {
            var thread = StoryThreadParser.Parse(ThreadText, ThreadUri);
            return new StoryCampaignModel("GC", [thread],
                new HashSet<string>(StringComparer.Ordinal),
                new StoryGraphBuilder(new EmptySchema()).Build([thread]));
        }

        public IReadOnlyList<string> GetCampaignNames()
        {
            return ["GC"];
        }

        public StoryCampaignModel? GetCampaignModel(string campaignName)
        {
            return campaignName == "GC" ? Model : null;
        }

        public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
        {
            return [];
        }

        public StoryChainScanResult GetChainResult()
        {
            return StoryChainScanResult.Empty with
            {
                Campaigns =
                [
                    new StoryCampaignChain("GC", [new StoryFactionManifest("Rebel", "story_plots_r.xml")])
                    {
                        SourceFile = "campaigns_test.xml"
                    }
                ]
            };
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return [];
        }
    }

    private sealed class StubReloadService(WorkspaceConfiguration config) : IModProjectReloadService
    {
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => config;
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHost : IGameWorkspaceHost
    {
        public IEnumerable<TrackedDocument> All => [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
        }

        public void Remove(string uri)
        {
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            doc = default!;
            return false;
        }
    }

    private sealed class StubIndexService(GameIndex index) : IGameIndexService
    {
        public GameIndex Current => index;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(Core.Assets.IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class EmptySchema : ISchemaProvider
    {
        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }
}
