// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     The story-chain problems the diagnostics pipeline serves must come from the LIVE chain scan
///     (<see cref="IStoryModelService" />, open-buffer-first and invalidated on every document
///     version change), not from the snapshot <c>WorkspaceIndexer.PreScanMetafiles</c> takes at
///     startup - that one only ever re-runs on a <c>.pgproj</c> change, so an unresolvable
///     <c>*_Story_Name</c> typed into a campaign file was never reported until a restart.
/// </summary>
public sealed class LiveStoryChainProblemStoreTest
{
    private const string CampaignUri = "file:///w/data/xml/campaigns_test.xml";

    private static StoryChainProblem Problem(string uri, string message)
    {
        return new StoryChainProblem(
            "Campaigns_Test.xml", uri, 2, 4, 2, 20,
            StoryChainProblemKind.UnresolvedStoryName, "Story_Plots_X.xml", message,
            XmlDiagnosticSeverity.Error);
    }

    private static StoryChainScanResult ResultWith(params StoryChainProblem[] problems)
    {
        return new StoryChainScanResult(["m.xml"], [], [], problems);
    }

    [Fact]
    public void Live_problems_are_served_for_the_document_that_owns_them()
    {
        var model = new FakeStoryModelService(ResultWith(Problem(CampaignUri, "live")));
        var store = new LiveStoryChainProblemStore(() => model, new FakeLspConfigurationProvider());

        var problem = Assert.Single(store.GetForDocument(CampaignUri));
        Assert.Equal("live", problem.Message);
    }

    [Fact]
    public void Live_problems_for_other_documents_are_not_served()
    {
        var model = new FakeStoryModelService(ResultWith(Problem("file:///w/other.xml", "live")));
        var store = new LiveStoryChainProblemStore(() => model, new FakeLspConfigurationProvider());

        Assert.Empty(store.GetForDocument(CampaignUri));
    }

    [Fact]
    public void Live_scan_supersedes_the_startup_snapshot()
    {
        // The whole point: the snapshot still holds the problem the user has since fixed.
        var model = new FakeStoryModelService(ResultWith());
        var store = new LiveStoryChainProblemStore(() => model, new FakeLspConfigurationProvider());
        store.Replace([Problem(CampaignUri, "stale startup problem")]);

        Assert.Empty(store.GetForDocument(CampaignUri));
    }

    [Fact]
    public void A_new_problem_appears_without_any_reload()
    {
        var model = new FakeStoryModelService(ResultWith());
        var store = new LiveStoryChainProblemStore(() => model, new FakeLspConfigurationProvider());
        Assert.Empty(store.GetForDocument(CampaignUri));

        model.Result = ResultWith(Problem(CampaignUri, "typed just now"));

        var problem = Assert.Single(store.GetForDocument(CampaignUri));
        Assert.Equal("typed just now", problem.Message);
    }

    [Fact]
    public void The_startup_snapshot_still_answers_before_the_live_scan_can_read_anything()
    {
        // Inside the startup window the chain scan reads nothing (config/schema not published yet)
        // and returns Empty. Falling through to the snapshot keeps startup behaviour unchanged.
        var model = new FakeStoryModelService(StoryChainScanResult.Empty);
        var store = new LiveStoryChainProblemStore(() => model, new FakeLspConfigurationProvider());
        store.Replace([Problem(CampaignUri, "from startup scan")]);

        var problem = Assert.Single(store.GetForDocument(CampaignUri));
        Assert.Equal("from startup scan", problem.Message);
    }

    [Fact]
    public void Story_discovery_disabled_serves_nothing()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Story = new StoryFeatureFlags { Discovery = false } });
        var model = new FakeStoryModelService(ResultWith(Problem(CampaignUri, "live")));
        var store = new LiveStoryChainProblemStore(() => model, config);

        Assert.Empty(store.GetForDocument(CampaignUri));
    }

    [Fact]
    public void The_model_service_is_resolved_lazily()
    {
        // IStoryModelService -> IModProjectReloadService -> IWorkspaceIndexer -> this store, so
        // resolving it in the constructor would close a DI cycle.
        var resolved = false;
        _ = new LiveStoryChainProblemStore(
            () =>
            {
                resolved = true;
                return new FakeStoryModelService(StoryChainScanResult.Empty);
            },
            new FakeLspConfigurationProvider());

        Assert.False(resolved);
    }

    private sealed class FakeStoryModelService(StoryChainScanResult result) : IStoryModelService
    {
        public StoryChainScanResult Result { get; set; } = result;

        public IReadOnlyList<string> GetCampaignNames()
        {
            return [];
        }

        public IReadOnlyList<StoryModelKey> GetModelKeys()
        {
            return GetCampaignNames()
                .Select(c => new StoryModelKey(c, "Rebel")).ToList();
        }

        public StoryCampaignModel? GetCampaignModel(string campaignName, string faction)
        {
            return null;
        }

        public IReadOnlyList<StoryCampaignModel> GetModelsContaining(string canonicalUri)
        {
            return [];
        }

        public StoryChainScanResult GetChainResult()
        {
            return Result;
        }

        public IReadOnlyList<string> GetInvalidatedCampaigns()
        {
            return [];
        }
    }
}
