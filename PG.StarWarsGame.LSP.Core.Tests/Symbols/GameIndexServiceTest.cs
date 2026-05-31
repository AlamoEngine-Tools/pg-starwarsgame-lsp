// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class GameIndexServiceTest
{
    // ── helpers / fakes ──────────────────────────────────────────────────────

    private static GameSymbol Symbol(string id, string uri = "file:///f.xml")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri, 1, null), null);
    }

    private static GameReference Reference(string targetId, string docUri = "file:///f.xml")
    {
        return new GameReference(targetId, null, null, docUri, 1, 0, 4);
    }

    private static DocumentGroupMembership GroupMembership(string groupKey, string memberUri = "file:///f.xml")
    {
        return new DocumentGroupMembership(
            new GroupMembership(groupKey, "SFXEvent", new FileOrigin(memberUri, 1, null)),
            2, 4, groupKey.Length);
    }

    private static DocumentIndex Doc(string uri, int version,
        GameSymbol[]? symbols = null, GameReference[]? refs = null,
        DocumentGroupMembership[]? groupMemberships = null)
    {
        return new DocumentIndex(uri, version,
            (symbols ?? []).ToImmutableArray(),
            (refs ?? []).ToImmutableArray(),
            GroupMemberships: (groupMemberships ?? []).ToImmutableArray());
    }

    private static IGameIndexService Build(params IGameDocumentParser[] parsers)
    {
        return new GameIndexService(new FileHelper(new MockFileSystem()), parsers,
            NullLogger<GameIndexService>.Instance);
    }

    // ── ApplyBaseline ────────────────────────────────────────────────────────

    [Fact]
    public void ApplyBaseline_Updates_Current_Baseline()
    {
        var svc = Build();
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty.Add("A", Symbol("A")),
            DateTimeOffset.UtcNow, "h",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);

        svc.ApplyBaseline(baseline);

        Assert.Equal(baseline, svc.Current.Baseline);
    }

    [Fact]
    public void ApplyBaseline_Fires_IndexChanged()
    {
        var svc = Build();
        GameIndex? fired = null;
        svc.IndexChanged += idx => fired = idx;

        svc.ApplyBaseline(BaselineIndex.Empty);

        Assert.NotNull(fired);
    }

    // ── URI normalization — canonical form at the index boundary ────────────

    [Fact]
    public async Task UpdateDocumentAsync_MixedCaseUri_DocumentKeyIsCanonicalLowercase()
    {
        // The index must store the canonical (lowercase file:///) key regardless of
        // what case the LSP client or scanner sends.
        var svc = Build(new FakeParser(Doc("", 0)));

        await svc.UpdateDocumentAsync("file:///C:/Data/Units.xml", "<X/>", 1, default);

        Assert.True(svc.Current.Documents.ContainsKey("file:///c:/data/units.xml"));
        Assert.False(svc.Current.Documents.ContainsKey("file:///C:/Data/Units.xml"));
    }

    [Fact]
    public async Task UpdateDocumentAsync_SameFileDifferentUriCase_SingleDocument()
    {
        // An LSP sync event (mixed case) followed by a scanner re-index (canonical) must
        // not create two entries for the same file.
        var svc = Build(new FakeParser(Doc("", 0)));

        await svc.UpdateDocumentAsync("file:///C:/Data/Units.xml", "<X/>", 1, default);
        await svc.UpdateDocumentAsync("file:///c:/data/units.xml", "<Y/>", 2, default);

        Assert.Single(svc.Current.Documents);
    }

    [Fact]
    public async Task RemoveDocument_MixedCaseUri_RemovesCanonicalEntry()
    {
        var sym = Symbol("UNIT_A");
        var svc = Build(new FakeParser(Doc("", 0, [sym])));
        await svc.UpdateDocumentAsync("file:///c:/data/units.xml", "<X/>", 1, default);

        svc.RemoveDocument("file:///C:/DATA/UNITS.XML");

        Assert.False(svc.Current.Documents.ContainsKey("file:///c:/data/units.xml"));
        Assert.False(svc.Current.WorkspaceDefinitions.ContainsKey("UNIT_A"));
    }

    // ── UpdateDocumentAsync — basic ──────────────────────────────────────────

    [Fact]
    public async Task UpdateDocumentAsync_BarePathAndFileUri_TreatedAsSameDocument()
    {
        var sym = Symbol("UNIT_A");
        var svc = Build(new FakeParser(Doc("", 0, [sym])));

        await svc.UpdateDocumentAsync(@"d:\data\units.xml", "<X/>", 1, default);
        await svc.UpdateDocumentAsync("file:///d:/data/units.xml", "<X/>", 2, default);

        Assert.Single(svc.Current.Documents);
        Assert.Single(svc.Current.WorkspaceDefinitions["UNIT_A"]);
    }

    [Fact]
    public async Task UpdateDocumentAsync_Populates_WorkspaceDefinitions()
    {
        var sym = Symbol("UNIT_A");
        var svc = Build(new FakeParser(Doc("", 0, [sym])));

        await svc.UpdateDocumentAsync("file:///units.xml", "<X/>", 1, default);

        Assert.True(svc.Current.WorkspaceDefinitions.ContainsKey("UNIT_A"));
    }

    [Fact]
    public async Task UpdateDocumentAsync_Populates_WorkspaceReferences()
    {
        var reference = Reference("TARGET");
        var svc = Build(new FakeParser(Doc("", 0, refs: [reference])));

        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.True(svc.Current.WorkspaceReferences.ContainsKey("TARGET"));
    }

    [Fact]
    public async Task UpdateDocumentAsync_Populates_WorkspaceGroupMemberships()
    {
        var gm = GroupMembership("MY_GROUP");
        var svc = Build(new FakeParser(Doc("", 0, groupMemberships: [gm])));

        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.True(svc.Current.WorkspaceGroupMemberships.ContainsKey("MY_GROUP"));
        Assert.Single(svc.Current.WorkspaceGroupMemberships["MY_GROUP"]);
        Assert.Equal("MY_GROUP", svc.Current.WorkspaceGroupMemberships["MY_GROUP"][0].GroupKey);
    }

    [Fact]
    public async Task RemoveDocument_Strips_WorkspaceGroupMemberships()
    {
        var gm = GroupMembership("MY_GROUP");
        var svc = Build(new FakeParser(Doc("", 0, groupMemberships: [gm])));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        svc.RemoveDocument("file:///f.xml");

        Assert.Empty(svc.Current.WorkspaceGroupMemberships);
    }

    [Fact]
    public async Task UpdateDocumentAsync_TwoDocsSameGroupKey_MembersAreAggregated()
    {
        var gm1 = GroupMembership("SHARED_GROUP", "file:///a.xml");
        var gm2 = GroupMembership("SHARED_GROUP", "file:///b.xml");
        var parserA = new FakeParser(Doc("", 0, groupMemberships: [gm1]));
        var parserB = new FakeParser(Doc("", 0, groupMemberships: [gm2]));
        var svc = Build(new MultiParser(parserA, parserB));

        await svc.UpdateDocumentAsync("file:///a.xml", "<X/>", 1, default);
        await svc.UpdateDocumentAsync("file:///b.xml", "<X/>", 1, default);

        Assert.Equal(2, svc.Current.WorkspaceGroupMemberships["SHARED_GROUP"].Length);
    }

    [Fact]
    public async Task UpdateDocumentAsync_Fires_IndexChanged()
    {
        var svc = Build(new FakeParser(Doc("", 0)));
        GameIndex? fired = null;
        svc.IndexChanged += idx => fired = idx;

        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.NotNull(fired);
    }

    [Fact]
    public async Task UpdateDocumentAsync_Is_NoOp_When_No_Parser_Matches()
    {
        var svc = Build(new FakeParser(Doc("", 0, [Symbol("A")])));
        svc.IndexChanged += _ => throw new Exception("should not fire");

        // .lua extension — FakeParser only handles .xml
        await svc.UpdateDocumentAsync("file:///script.lua", "fn()", 1, default);

        Assert.Empty(svc.Current.WorkspaceDefinitions);
    }

    // ── UpdateDocumentAsync — stale-version guard ────────────────────────────

    [Fact]
    public async Task UpdateDocumentAsync_Drops_Strictly_Older_Version()
    {
        var svc = Build(new FakeParser(Doc("", 0, [Symbol("UNIT_V2")])));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 2, default);

        // A strictly older version must not overwrite a newer committed version.
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.Equal(2, svc.Current.Documents["file:///f.xml"].Version);
    }

    [Fact]
    public async Task UpdateDocumentAsync_Same_Version_Fires_IndexChanged()
    {
        // Re-opening the same file at the same version (e.g. DidOpen after a scan at v0,
        // then DidOpen again at v1) must still fire IndexChanged so diagnostics are published.
        var svc = Build(new FakeParser(Doc("", 0)));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        var fired = false;
        svc.IndexChanged += _ => fired = true;
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.True(fired);
    }

    // ── UpdateDocumentAsync — document replacement ───────────────────────────

    [Fact]
    public async Task UpdateDocumentAsync_Replaces_Old_Symbols_With_New_Ones()
    {
        var old = Symbol("OLD_UNIT");
        var updated = Symbol("NEW_UNIT");

        // First parse: returns OLD_UNIT
        var svc = Build(new FakeParser(Doc("", 0, [old])));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.True(svc.Current.WorkspaceDefinitions.ContainsKey("OLD_UNIT"));

        // Second parse of same URI: FakeParser still returns OLD_UNIT but with version 2;
        // swap the parser to return NEW_UNIT
        var svc2 = new GameIndexService(new FileHelper(new MockFileSystem()),
            [new FakeParser(Doc("", 0, [updated]))], NullLogger<GameIndexService>.Instance);
        // Apply the first document manually then update
        await svc2.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);
        await svc2.UpdateDocumentAsync("file:///f.xml", "<Y/>", 2, default);

        Assert.False(svc2.Current.WorkspaceDefinitions.ContainsKey("OLD_UNIT"));
        Assert.True(svc2.Current.WorkspaceDefinitions.ContainsKey("NEW_UNIT"));
    }

    [Fact]
    public async Task UpdateDocumentAsync_Strips_Old_References_On_Replace()
    {
        var oldRef = Reference("OLD_TARGET");
        var newRef = Reference("NEW_TARGET");

        var svc = Build(new FakeParser(Doc("", 0, refs: [oldRef])));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.True(svc.Current.WorkspaceReferences.ContainsKey("OLD_TARGET"));

        var svc2 = new GameIndexService(new FileHelper(new MockFileSystem()),
            [new FakeParser(Doc("", 0, refs: [newRef]))], NullLogger<GameIndexService>.Instance);
        await svc2.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);
        await svc2.UpdateDocumentAsync("file:///f.xml", "<Y/>", 2, default);

        Assert.False(svc2.Current.WorkspaceReferences.ContainsKey("OLD_TARGET"));
        Assert.True(svc2.Current.WorkspaceReferences.ContainsKey("NEW_TARGET"));
    }

    // ── RemoveDocument ───────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveDocument_Removes_Document_And_Its_Symbols()
    {
        var sym = Symbol("UNIT_A");
        var svc = Build(new FakeParser(Doc("", 0, [sym])));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        svc.RemoveDocument("file:///f.xml");

        Assert.False(svc.Current.Documents.ContainsKey("file:///f.xml"));
        Assert.False(svc.Current.WorkspaceDefinitions.ContainsKey("UNIT_A"));
    }

    [Fact]
    public async Task RemoveDocument_Removes_References()
    {
        var reference = Reference("TARGET");
        var svc = Build(new FakeParser(Doc("", 0, refs: [reference])));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        svc.RemoveDocument("file:///f.xml");

        Assert.False(svc.Current.WorkspaceReferences.ContainsKey("TARGET"));
    }

    [Fact]
    public async Task RemoveDocument_Fires_IndexChanged()
    {
        var svc = Build(new FakeParser(Doc("", 0)));
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        var fired = false;
        svc.IndexChanged += _ => fired = true;
        svc.RemoveDocument("file:///f.xml");

        Assert.True(fired);
    }

    [Fact]
    public void RemoveDocument_Unknown_Uri_Is_NoOp()
    {
        var svc = Build();
        svc.IndexChanged += _ => throw new Exception("should not fire");

        svc.RemoveDocument("file:///never-added.xml"); // must not throw or fire event
    }

    // ── Multi-document / symbol sharing ─────────────────────────────────────

    [Fact]
    public async Task RemoveDocument_Preserves_Same_Symbol_Id_From_Other_Document()
    {
        // Two documents each define the same ID (duplicate — flagged as error in diagnostics,
        // but the index must still track both and survive individual removal correctly).
        var symA = Symbol("DUP", "file:///a.xml");
        var symB = Symbol("DUP", "file:///b.xml");

        var svcA = Build(new FakeParser(Doc("", 0, [symA])));
        await svcA.UpdateDocumentAsync("file:///a.xml", "", 1, default);

        var svcB = new GameIndexService(new FileHelper(new MockFileSystem()),
            [new FakeParser(Doc("", 0, [symB]))], NullLogger<GameIndexService>.Instance);
        await svcB.UpdateDocumentAsync("file:///a.xml", "", 1, default);
        await svcB.UpdateDocumentAsync("file:///b.xml", "", 1, default);

        svcB.RemoveDocument("file:///a.xml");

        // b.xml's contribution must survive
        Assert.True(svcB.Current.WorkspaceDefinitions.ContainsKey("DUP"));
        Assert.Single(svcB.Current.WorkspaceDefinitions["DUP"]);
    }

    // ── BeginBulkUpdate ──────────────────────────────────────────────────────

    [Fact]
    public async Task BeginBulkUpdate_Suppresses_IndexChanged_During_Scope()
    {
        var svc = Build(new FakeParser(Doc("", 0)));
        var fired = 0;
        svc.IndexChanged += _ => fired++;

        using (svc.BeginBulkUpdate())
        {
            await svc.UpdateDocumentAsync("file:///a.xml", "<X/>", 1, default);
            await svc.UpdateDocumentAsync("file:///b.xml", "<X/>", 1, default);
            Assert.Equal(0, fired); // still suppressed mid-scope
        }

        Assert.Equal(1, fired); // exactly one fire on dispose
    }

    [Fact]
    public async Task BeginBulkUpdate_Fires_IndexChanged_Exactly_Once_For_Many_Updates()
    {
        var svc = Build(new FakeParser(Doc("", 0)));
        var fireCount = 0;
        svc.IndexChanged += _ => fireCount++;

        using (svc.BeginBulkUpdate())
        {
            for (var i = 0; i < 10; i++)
                await svc.UpdateDocumentAsync($"file:///{i}.xml", "<X/>", 1, default);
        }

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void BeginBulkUpdate_No_Updates_Does_Not_Fire_IndexChanged()
    {
        var svc = Build();
        var fired = false;
        svc.IndexChanged += _ => fired = true;

        using (svc.BeginBulkUpdate())
        {
            /* no updates */
        }

        Assert.False(fired);
    }

    [Fact]
    public async Task BeginBulkUpdate_Final_IndexChanged_Reflects_All_Updates()
    {
        var sym = Symbol("UNIT_A");
        var svc = Build(new FakeParser(Doc("", 0, [sym])));
        GameIndex? captured = null;
        svc.IndexChanged += idx => captured = idx;

        using (svc.BeginBulkUpdate())
        {
            await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);
        }

        Assert.NotNull(captured);
        Assert.True(captured!.WorkspaceDefinitions.ContainsKey("UNIT_A"));
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDocumentAsync_Respects_Cancellation()
    {
        var svc = Build(new CancellingParser());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, cts.Token));
    }

    [Fact]
    public async Task UpdateDocumentAsync_SecondEditForSameUri_CancelsInflightFirstParse()
    {
        using var firstParseStarted = new SemaphoreSlim(0, 1);
        var firstParseCancelled = false;

        var svc = Build(new DelegateParser(async (uri, _, version, ct) =>
        {
            if (version == 1)
            {
                firstParseStarted.Release();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    firstParseCancelled = true;
                    throw;
                }
            }

            return Doc(uri, version);
        }));

        var firstTask = svc.UpdateDocumentAsync("file:///f.xml", "v1", 1, default);

        // Wait for the first parse to actually be in-flight
        Assert.True(await firstParseStarted.WaitAsync(TimeSpan.FromSeconds(2)));

        // A newer edit for the same URI — should cancel the first parse
        await svc.UpdateDocumentAsync("file:///f.xml", "v2", 2, default);

        // The first UpdateDocumentAsync must complete without throwing
        await firstTask;

        Assert.True(firstParseCancelled);
        Assert.Equal(2, svc.Current.Documents["file:///f.xml"].Version);
    }

    [Fact]
    public async Task UpdateDocumentAsync_NewEditForDifferentUri_DoesNotCancelInflightParse()
    {
        using var aStarted = new SemaphoreSlim(0, 1);
        using var aRelease = new SemaphoreSlim(0, 1);
        var aCancelled = false;

        var svc = Build(new DelegateParser(async (uri, _, version, ct) =>
        {
            if (uri.Contains("a.xml"))
            {
                aStarted.Release();
                try
                {
                    await aRelease.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    aCancelled = true;
                    throw;
                }
            }

            return Doc(uri, version);
        }));

        var aTask = svc.UpdateDocumentAsync("file:///a.xml", "v1", 1, default);
        Assert.True(await aStarted.WaitAsync(TimeSpan.FromSeconds(2)));

        // Edit a different URI — must not cancel a.xml's in-flight parse
        await svc.UpdateDocumentAsync("file:///b.xml", "v1", 1, default);

        aRelease.Release();
        await aTask;

        Assert.False(aCancelled);
    }

    private sealed class FakeParser : IGameDocumentParser
    {
        private readonly DocumentIndex _result;

        public FakeParser(DocumentIndex result)
        {
            _result = result;
        }

        public bool CanParse(string ext)
        {
            return ext == ".xml";
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version,
            CancellationToken ct)
        {
            return ValueTask.FromResult(_result with { DocumentUri = uri, Version = version });
        }
    }

    private sealed class MultiParser : IGameDocumentParser
    {
        private readonly FakeParser _a;
        private readonly FakeParser _b;

        public MultiParser(FakeParser a, FakeParser b)
        {
            _a = a;
            _b = b;
        }

        public bool CanParse(string ext)
        {
            return true;
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version, CancellationToken ct)
        {
            return uri.Contains("/a.")
                ? _a.ParseAsync(uri, text, version, ct)
                : _b.ParseAsync(uri, text, version, ct);
        }
    }

    private sealed class CancellingParser : IGameDocumentParser
    {
        public bool CanParse(string ext)
        {
            return true;
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Doc(uri, version));
        }
    }

    private sealed class DelegateParser : IGameDocumentParser
    {
        private readonly Func<string, string, int, CancellationToken, ValueTask<DocumentIndex>> _fn;

        public DelegateParser(Func<string, string, int, CancellationToken, ValueTask<DocumentIndex>> fn)
        {
            _fn = fn;
        }

        public bool CanParse(string ext)
        {
            return true;
        }

        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version,
            CancellationToken ct)
        {
            return _fn(uri, text, version, ct);
        }
    }
}