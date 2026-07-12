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
        CapturingApplier applier, bool storyEditor = true, GameIndex? index = null,
        IStoryModelService? modelService = null)
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
            modelService ?? new StubModelService(),
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
        string? faction = null, IReadOnlyList<string>? tokens = null)
    {
        return new ExecuteStoryCommandParams(campaign, kind, threadUri ?? ThreadUri, eventName, newName,
            Value: value, Flag: flag, GroupIndex: groupIndex, Token: token, Tokens: tokens,
            File: file, Faction: faction);
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
    public async Task CreateEvent_WithInitialParams_WritesEventAndRewardParamTags()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("createEvent", newName: "Fresh") with
            {
                EventType = "STORY_FLAG",
                RewardType = "SET_FLAG",
                EventParams = [new StoryParamValueEditDto(0, "MyFlag")],
                RewardParams = [new StoryParamValueEditDto(0, "OtherFlag")],
            }, CancellationToken.None);

        Assert.True(result.Success);
        var newText = Assert.Single(SingleDocEdit(applier).Edits).NewText;
        Assert.Contains("<Event_Param1>MyFlag</Event_Param1>", newText);
        Assert.Contains("<Reward_Param1>OtherFlag</Reward_Param1>", newText);
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
    public async Task AddPrereqGroup_MultipleTokens_AppliesOneAndLine()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("addPrereqGroup", eventName: "Start", tokens: ["Next", "Other"]),
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("<Prereq>Next Other</Prereq>", Assert.Single(SingleDocEdit(applier).Edits).NewText);
    }

    [Fact]
    public async Task AddPrereqGroup_NoTokens_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("addPrereqGroup", eventName: "Start", tokens: []), CancellationToken.None);

        Assert.Contains("tokens", result.Error);
    }

    [Fact]
    public async Task ClearEventType_RemovesTheTriggerBlock()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("clearEventType", eventName: "Start"), CancellationToken.None);

        Assert.True(result.Success);
        var edit = Assert.Single(SingleDocEdit(applier).Edits);
        Assert.Equal("", edit.NewText); // the Event_Type line is deleted, nothing inserted
    }

    [Fact]
    public async Task ClearRewardType_WithoutReward_ReportsNoChanges()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("clearRewardType", eventName: "Start"), CancellationToken.None);

        Assert.Contains("no changes", result.Error);
    }

    [Fact]
    public async Task AddPrereqAlternatives_MultipleTokens_AppliesOneOrLinePerToken()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("addPrereqAlternatives", eventName: "Start", tokens: ["Next", "Other"]),
                CancellationToken.None);

        Assert.True(result.Success);
        var newText = Assert.Single(SingleDocEdit(applier).Edits).NewText;
        Assert.Contains("<Prereq>Next</Prereq>", newText);
        Assert.Contains("<Prereq>Other</Prereq>", newText);
        Assert.DoesNotContain("Next Other", newText);
    }

    [Fact]
    public async Task AddPrereqAlternatives_NoTokens_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("addPrereqAlternatives", eventName: "Start", tokens: []), CancellationToken.None);

        Assert.Contains("tokens", result.Error);
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
    public async Task RenameEvent_NotIndexedButInModel_RenamesViaModel()
    {
        // Symbol indexing off (default): the event isn't an indexed symbol, so rename falls back
        // to the campaign model — the freshly-created-event case. "Start" is defined in the model
        // thread and referenced by "Next"'s <Prereq>Start</Prereq>, so both spots get rewritten.
        var applier = new CapturingApplier();

        var result = await Handler(applier)
            .Handle(Command("renameEvent", eventName: "Start", newName: "Renamed"), CancellationToken.None);

        Assert.True(result.Success);
        // The model rename uses the `changes` map (not documentChanges) — the versioned form is
        // rejected by the client for untracked thread files.
        Assert.Null(applier.Edit!.DocumentChanges);
        var edits = applier.Edit!.Changes!.Single().Value.ToList();
        Assert.Equal(2, edits.Count); // the Name attribute + the prereq reference

        // APPLYING the edits must yield valid XML — a count check alone would miss an off-by-one
        // in the model's NameRange/token ranges that corrupts the file (and makes rename "not work").
        var renamed = ApplyLspEdits(ThreadText, edits);
        Assert.Contains("<Event Name=\"Renamed\">", renamed);
        Assert.Contains("<Prereq>Renamed</Prereq>", renamed);
        Assert.DoesNotContain("Start", renamed); // no leftover of the old name
    }

    /// <summary>Applies LSP TextEdits (0-based line/char ranges) to text, last-edit-first.</summary>
    private static string ApplyLspEdits(string text, IReadOnlyList<TextEdit> edits)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                lineStarts.Add(i + 1);
        int Offset(Position p) => Math.Min(lineStarts[p.Line] + p.Character, text.Length);

        var result = text;
        foreach (var edit in edits.OrderByDescending(e => Offset(e.Range.Start)))
            result = result[..Offset(edit.Range.Start)] + edit.NewText + result[Offset(edit.Range.End)..];
        return result;
    }

    [Fact]
    public async Task RenameEvent_ThreadIncludedTwice_EmitsEachEditOnce()
    {
        // The same thread referenced by two manifests must not produce duplicate (overlapping)
        // text edits — the client rejects the whole applyEdit if a document has overlapping edits.
        var applier = new CapturingApplier();

        var result = await Handler(applier, modelService: new StubModelService(duplicateThreads: true))
            .Handle(Command("renameEvent", eventName: "Start", newName: "Renamed"), CancellationToken.None);

        Assert.True(result.Success);
        var edits = applier.Edit!.Changes!.Single().Value.ToList();
        Assert.Equal(2, edits.Count); // still just the Name attribute + the one prereq reference
        var distinctRanges = edits
            .Select(e => (e.Range.Start.Line, e.Range.Start.Character, e.Range.End.Line, e.Range.End.Character))
            .Distinct().Count();
        Assert.Equal(edits.Count, distinctRanges); // no duplicate/overlapping spans
    }

    [Fact]
    public async Task RenameEvent_UnknownEvent_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("renameEvent", eventName: "Ghost", newName: "X"), CancellationToken.None);

        Assert.Contains("was not found", result.Error);
    }

    [Fact]
    public async Task RenameEvent_ToExistingName_ReturnsError()
    {
        var result = await Handler(new CapturingApplier())
            .Handle(Command("renameEvent", eventName: "Start", newName: "Next"), CancellationToken.None);

        Assert.Contains("already names an event", result.Error);
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

    private sealed class StubModelService(bool duplicateThreads = false) : IStoryModelService
    {
        private readonly StoryCampaignModel _model = BuildModel(duplicateThreads);

        private static StoryCampaignModel BuildModel(bool duplicateThreads)
        {
            // When duplicateThreads is set the SAME thread appears twice, mimicking a file
            // referenced by two faction manifests — the rename must still emit each edit once.
            var thread = StoryThreadParser.Parse(ThreadText, ThreadUri);
            IReadOnlyList<StoryThread> threads = duplicateThreads ? [thread, thread] : [thread];
            return new StoryCampaignModel("GC", threads,
                new HashSet<string>(StringComparer.Ordinal),
                new StoryGraphBuilder(new EmptySchema()).Build([thread]));
        }

        public IReadOnlyList<string> GetCampaignNames()
        {
            return ["GC"];
        }

        public StoryCampaignModel? GetCampaignModel(string campaignName)
        {
            return campaignName == "GC" ? _model : null;
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
