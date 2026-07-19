// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
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
using PG.StarWarsGame.LSP.Xml;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

/// <summary>
///     Shared test doubles and sample story data for the story command executor and its batch/
///     validate handlers. The dependencies are wired against one <see cref="MockFileSystem" /> so the
///     document text source reads the same files the handlers resolve.
/// </summary>
internal static class StoryCommandTestFixtures
{
    public const string ThreadText =
        "<Story>\n" +
        "\t<Event Name=\"Start\">\n" +
        "\t\t<Event_Type>STORY_ELAPSED</Event_Type>\n" +
        "\t</Event>\n" +
        "\t<Event Name=\"Next\">\n" +
        "\t\t<Event_Type>STORY_TRIGGER</Event_Type>\n" +
        "\t\t<Prereq>Start</Prereq>\n" +
        "\t</Event>\n" +
        "</Story>\n";

    public const string ManifestText =
        "<Story_Mode_Plots>\n" +
        "\t<Active_Plot>story_main.xml</Active_Plot>\n" +
        "</Story_Mode_Plots>\n";

    public const string CampaignSetText =
        "<Campaigns>\n" +
        "\t<Campaign Name=\"GC\">\n" +
        "\t\t<Rebel_Story_Name>story_plots_r.xml</Rebel_Story_Name>\n" +
        "\t</Campaign>\n" +
        "</Campaigns>\n";

    public static readonly string XmlDir = Path.Combine(Rooted("ws"), "data", "xml");
    public static readonly string DepXmlDir = Path.Combine(Rooted("dep"), "data", "xml");

    public static readonly string ThreadUri;
    public static readonly string DepThreadUri;
    public static readonly string ManifestUri;

    static StoryCommandTestFixtures()
    {
        var fh = new FileHelper(new MockFileSystem());
        ThreadUri = fh.NormalizeUri(Path.Combine(XmlDir, "story_main.xml"));
        DepThreadUri = fh.NormalizeUri(Path.Combine(DepXmlDir, "story_dep.xml"));
        ManifestUri = fh.NormalizeUri(Path.Combine(XmlDir, "story_plots_r.xml"));
    }

    private static string Rooted(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    public static MockFileSystem NewFileSystem()
    {
        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(XmlDir, "story_main.xml")] = new(ThreadText),
            [Path.Combine(DepXmlDir, "story_dep.xml")] = new(ThreadText),
            [Path.Combine(XmlDir, "story_plots_r.xml")] = new(ManifestText),
            [Path.Combine(XmlDir, "campaigns_test.xml")] = new(CampaignSetText)
        });
    }

    public static FileHelper FileHelperFor(MockFileSystem fs)
    {
        return new FileHelper(fs);
    }

    public static DocumentTextSource TextSource(FileHelper fileHelper)
    {
        return new DocumentTextSource(new FakeHost(), fileHelper, NullLogger<DocumentTextSource>.Instance);
    }

    public static ILspConfigurationProvider Config(bool storyEditor = true, bool storyEditing = true)
    {
        return FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
        {
            Tools = new ToolsFeatureFlags { StoryEditor = storyEditor, StoryEditing = storyEditing },
            Story = new StoryFeatureFlags { Discovery = true }
        });
    }

    public static StubReloadService Reload()
    {
        return new StubReloadService(WorkspaceConfiguration.Empty with
        {
            XmlDirectories = [DepXmlDir, XmlDir]
        });
    }

    public static GameIndex DefaultIndex()
    {
        var documents = ImmutableDictionary<string, DocumentIndex>.Empty
            .Add(ThreadUri, new DocumentIndex(ThreadUri, 1, [], [], LayerRank: 1, LayerName: "Mod"))
            .Add(DepThreadUri, new DocumentIndex(DepThreadUri, 1, [], [], LayerRank: 0, LayerName: "Core Dependency"));
        return GameIndex.Empty with { Documents = documents };
    }

    public static StoryCommandDto Cmd(string kind, string? threadUri = null, string? eventName = null,
        string? newName = null, string? value = null, bool? flag = null, int? groupIndex = null,
        string? token = null, string? file = null, string? faction = null,
        IReadOnlyList<string>? tokens = null, string? eventType = null)
    {
        return new StoryCommandDto(kind, threadUri ?? ThreadUri, eventName, newName,
            eventType, Value: value, Flag: flag, GroupIndex: groupIndex, Token: token,
            Tokens: tokens, File: file, Faction: faction);
    }

    // ── test doubles ─────────────────────────────────────────────────────────

    public sealed class CapturingApplier(bool result = true) : IWorkspaceEditApplier
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

    /// <summary>Emits one error diagnostic at each occurrence of <c>Marker</c> in the text.</summary>
    public sealed class MarkerCollector(string marker) : IXmlDiagnosticsCollector
    {
        public IReadOnlyList<Diagnostic> Collect(string uri, string text, GameIndex index)
        {
            var results = new List<Diagnostic>();
            var lines = text.Split('\n');
            for (var line = 0; line < lines.Length; line++)
            {
                var col = lines[line].IndexOf(marker, StringComparison.Ordinal);
                if (col < 0) continue;
                results.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"marker '{marker}'",
                    Range = new Range(
                        line, col, line, col + marker.Length)
                });
            }

            return results;
        }
    }

    public sealed class StubModelService(bool duplicateThreads = false) : IStoryModelService
    {
        private readonly StoryCampaignModel _model = BuildModel(duplicateThreads);

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
                ],
                // Enough for StoryCampaignAssembler to resolve GC → story_plots_r.xml → story_main.xml.
                Manifests = [new StoryManifestContents("story_plots_r.xml", ["story_main.xml"], [], [])]
            };
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return [];
        }

        private static StoryCampaignModel BuildModel(bool duplicateThreads)
        {
            var thread = StoryThreadParser.Parse(ThreadText, ThreadUri);
            IReadOnlyList<StoryThread> threads = duplicateThreads ? [thread, thread] : [thread];
            return new StoryCampaignModel("GC", threads,
                new HashSet<string>(StringComparer.Ordinal),
                new StoryGraphBuilder(new StoryTestSchema()).Build([thread]));
        }
    }

    public sealed class StubReloadService(WorkspaceConfiguration config) : IModProjectReloadService
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

    public sealed class FakeHost : IGameWorkspaceHost
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

    public sealed class StubIndexService(GameIndex index) : IGameIndexService
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

        public void ApplyLocalisation(ILocalisationIndex localisation)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex assetFiles)
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

    public sealed class StoryTestSchema : ISchemaProvider
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