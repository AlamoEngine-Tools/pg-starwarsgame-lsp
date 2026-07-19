// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class StoryGraphDiagnosticsServiceTest
{
    private const string ThreadUri = "file:///ws/data/xml/story_a.xml";

    private static StoryGraphDiagnosticsService Build(bool flagOn)
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Story = new StoryFeatureFlags { GraphDiagnostics = flagOn } });
        return new StoryGraphDiagnosticsService(new StubModelService(), new EmptySchema(), config);
    }

    [Fact]
    public void GetForDocument_FlagOn_SurfacesModelDiagnostics()
    {
        var diagnostics = Build(true).GetForDocument(ThreadUri);

        // The stub campaign contains a dangling prereq in this document.
        Assert.Contains(diagnostics, d => d.Message.Contains("Ghost"));
    }

    [Fact]
    public void GetForDocument_FlagOff_ReturnsNothing()
    {
        Assert.Empty(Build(false).GetForDocument(ThreadUri));
    }

    [Fact]
    public void GetForDocument_SameFindingFromTwoCampaigns_IsDeduplicated()
    {
        var diagnostics = Build(true).GetForDocument(ThreadUri);

        Assert.Single(diagnostics, d => d.Message.Contains("Ghost"));
    }

    private sealed class StubModelService : IStoryModelService
    {
        // Two campaigns share the same thread - findings must not double up.
        public IReadOnlyList<string> GetCampaignNames()
        {
            return ["GC_One", "GC_Two"];
        }

        public StoryCampaignModel? GetCampaignModel(string campaignName)
        {
            var thread = StoryThreadParser.Parse(
                "<Story><Event Name=\"B\"><Prereq>Ghost</Prereq></Event></Story>", ThreadUri);
            return new StoryCampaignModel(campaignName, [thread],
                new HashSet<string>(StringComparer.Ordinal),
                new StoryGraphBuilder(new EmptySchema()).Build([thread]));
        }

        public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
        {
            return GetCampaignNames().Select(GetCampaignModel).Where(m => m is not null).ToList()!;
        }

        public StoryChainScanResult GetChainResult()
        {
            return StoryChainScanResult.Empty;
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return [];
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