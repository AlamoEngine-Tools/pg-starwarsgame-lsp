// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Core.Tests.Testing;

/// <summary>
///     The index service every suite hands its handlers: a snapshot the test chooses, replaced by
///     every apply the way the real service replaces it, each one raising
///     <c>IndexChanged</c>; and a record of what the code under test asked of it.
/// </summary>
public sealed class FakeGameIndexServiceTest
{
    private static GameIndex IndexWithBones()
    {
        return GameIndex.Empty with
        {
            ModelBones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add("hull.alo", ["Root"])
        };
    }

    [Fact]
    public void Current_IsTheSnapshotItWasBuiltWith_AndCanBeReplaced()
    {
        var first = IndexWithBones();
        var service = new FakeGameIndexService(first);

        Assert.Same(first, service.Current);

        service.Current = GameIndex.Empty;
        Assert.Same(GameIndex.Empty, service.Current);
    }

    [Fact]
    public void Default_IsTheEmptyIndex()
    {
        Assert.Same(GameIndex.Empty, new FakeGameIndexService().Current);
    }

    [Fact]
    public void CurrentReads_CountsEveryRead()
    {
        var service = new FakeGameIndexService();

        _ = service.Current;
        _ = service.Current;

        Assert.Equal(2, service.CurrentReads);
    }

    [Fact]
    public void ApplyBaseline_ReplacesTheSnapshot_AndRaisesIndexChanged()
    {
        var service = new FakeGameIndexService();
        var raised = new List<GameIndex>();
        service.IndexChanged += raised.Add;

        service.ApplyBaseline(BaselineIndex.Empty);

        Assert.Single(raised);
        Assert.Same(service.Current, raised[0]);
        Assert.Same(BaselineIndex.Empty, service.Current.Baseline);
    }

    [Fact]
    public void ApplyModelBones_IsVisibleInTheSnapshot_AndRaisesIndexChanged()
    {
        var service = new FakeGameIndexService();
        var raised = 0;
        service.IndexChanged += _ => raised++;

        service.ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add("hull.alo", ["Root"]));

        Assert.Equal(1, raised);
        Assert.Equal(["Root"], service.Current.ModelBones["hull.alo"]);
    }

    [Fact]
    public void ApplyLocalisation_RaisesBothEvents()
    {
        var service = new FakeGameIndexService();
        var index = 0;
        var localisation = 0;
        service.IndexChanged += _ => index++;
        service.LocalisationChanged += _ => localisation++;

        service.ApplyLocalisation(GameIndex.Empty.Localisation);

        Assert.Equal(1, index);
        Assert.Equal(1, localisation);
    }

    [Fact]
    public void ApplyWorkspaceDynamicEnumValues_RaisesDynamicEnumChanged_AndIsRecorded()
    {
        var service = new FakeGameIndexService();
        var raised = 0;
        service.DynamicEnumChanged += _ => raised++;
        var values = ImmutableDictionary<string, ImmutableArray<string>>.Empty.Add("Damage_Type", ["Laser"]);

        service.ApplyWorkspaceDynamicEnumValues(values);

        Assert.Equal(1, raised);
        Assert.Equal([values], service.DynamicEnumApplications);
        Assert.Equal(["Laser"], service.Current.WorkspaceDynamicEnumValues["Damage_Type"]);
    }

    [Fact]
    public async Task UpdateDocumentAsync_OpenDocumentAsync_And_RemoveDocument_AreRecordedApart()
    {
        var service = new FakeGameIndexService();

        await service.UpdateDocumentAsync("file:///a.xml", "<a/>", 2, CancellationToken.None);
        await service.OpenDocumentAsync("file:///b.xml", "<b/>", 1, CancellationToken.None);
        service.RemoveDocument("file:///a.xml");

        Assert.Equal([("file:///a.xml", "<a/>", 2)], service.Updates);
        Assert.Equal([("file:///b.xml", "<b/>", 1)], service.Opens);
        Assert.Equal(["file:///a.xml"], service.Removals);
    }

    [Fact]
    public void BeginBulkUpdate_IsCounted()
    {
        var service = new FakeGameIndexService();

        service.BeginBulkUpdate().Dispose();
        service.BeginBulkUpdate().Dispose();

        Assert.Equal(2, service.BulkUpdates);
    }

    [Fact]
    public void InjectDocument_PutsItInTheSnapshot_AndRemoveDocument_TakesItOut()
    {
        var service = new FakeGameIndexService();
        var doc = new DocumentIndex("file:///a.xml", 1, [], []);

        service.InjectDocument(doc);
        Assert.Same(doc, service.Current.Documents["file:///a.xml"]);
        Assert.Equal([doc], service.Injected);

        service.RemoveDocument("file:///a.xml");
        Assert.Empty(service.Current.Documents);
    }

    [Fact]
    public async Task UpdateDocumentAsync_LeavesASymbollessDocumentOfThatVersion_WithoutRaising()
    {
        // The workspace indexer decides what is already indexed from the snapshot; the fake has no
        // parser, so the document it leaves carries the version and nothing else.
        var service = new FakeGameIndexService();
        var raised = 0;
        service.IndexChanged += _ => raised++;

        await service.UpdateDocumentAsync("file:///a.xml", "<a/>", 3, CancellationToken.None);

        var doc = service.Current.Documents["file:///a.xml"];
        Assert.Equal(3, doc.Version);
        Assert.Empty(doc.Symbols);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task UpdateDocumentAsync_HonoursACancelledToken()
    {
        var service = new FakeGameIndexService();
        var cancelled = new CancellationToken(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.UpdateDocumentAsync("file:///a.xml", "<a/>", 1, cancelled));
        Assert.Empty(service.Updates);
    }

    [Fact]
    public async Task UpdateDocumentAsync_RecordsFromManyThreadsAtOnce()
    {
        // The workspace indexer updates in parallel; a fake that lost writes under contention
        // would fail its tests at random.
        var service = new FakeGameIndexService();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            service.UpdateDocumentAsync($"file:///{i}.xml", "<a/>", i, CancellationToken.None))));

        Assert.Equal(200, service.Updates.Count);
    }

    [Fact]
    public void ApplyAssetFiles_IsCounted()
    {
        var service = new FakeGameIndexService();

        service.ApplyAssetFiles(GameIndex.Empty.AssetFiles);

        Assert.Equal(1, service.AssetApplications);
    }

    [Fact]
    public void Raise_FiresIndexChangedWithTheGivenSnapshot_WithoutReplacingCurrent()
    {
        var service = new FakeGameIndexService();
        var other = IndexWithBones();
        GameIndex? seen = null;
        service.IndexChanged += i => seen = i;

        service.Raise(other);

        Assert.Same(other, seen);
        Assert.Same(GameIndex.Empty, service.Current);
    }

    [Fact]
    public void RaiseLocalisation_And_RaiseDynamicEnumChanged_FireTheirEventsOnly()
    {
        var service = new FakeGameIndexService();
        var index = 0;
        var localisation = 0;
        var dynamicEnum = 0;
        service.IndexChanged += _ => index++;
        service.LocalisationChanged += _ => localisation++;
        service.DynamicEnumChanged += _ => dynamicEnum++;

        service.RaiseLocalisation(GameIndex.Empty.Localisation);
        service.RaiseDynamicEnumChanged(GameIndex.Empty);

        Assert.Equal(0, index);
        Assert.Equal(1, localisation);
        Assert.Equal(1, dynamicEnum);
    }

    [Fact]
    public void BeginBulkUpdate_HoldsIndexChangedUntilDisposed_ThenRaisesOnce()
    {
        var service = new FakeGameIndexService();
        var raised = 0;
        service.IndexChanged += _ => raised++;

        using (service.BeginBulkUpdate())
        {
            service.ApplyBaseline(BaselineIndex.Empty);
            service.ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>>.Empty);
            Assert.Equal(0, raised);
        }

        Assert.Equal(1, raised);
    }

    [Fact]
    public void BeginBulkUpdate_WithNoChange_RaisesNothing()
    {
        var service = new FakeGameIndexService();
        var raised = 0;
        service.IndexChanged += _ => raised++;

        service.BeginBulkUpdate().Dispose();

        Assert.Equal(0, raised);
    }
}