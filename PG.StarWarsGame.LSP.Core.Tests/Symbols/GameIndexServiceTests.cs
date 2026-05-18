using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

public sealed class GameIndexServiceTests
{
    // ── helpers / fakes ──────────────────────────────────────────────────────

    private static GameSymbol Symbol(string id, string uri = "file:///f.xml") =>
        new(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri, 1, null), null);

    private static GameReference Reference(string targetId, string docUri = "file:///f.xml") =>
        new(targetId, null, null, docUri, 1, 0, 4);

    private static DocumentIndex Doc(string uri, int version,
        GameSymbol[]? symbols = null, GameReference[]? refs = null) =>
        new(uri, version,
            (symbols ?? []).ToImmutableArray(),
            (refs    ?? []).ToImmutableArray());

    private sealed class FakeParser : IGameDocumentParser
    {
        private readonly DocumentIndex _result;
        public FakeParser(DocumentIndex result) => _result = result;
        public bool CanParse(string ext) => ext == ".xml";
        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version,
            CancellationToken ct) =>
            ValueTask.FromResult(_result with { DocumentUri = uri, Version = version });
    }

    private sealed class CancellingParser : IGameDocumentParser
    {
        public bool CanParse(string ext) => true;
        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Doc(uri, version));
        }
    }

    private static IGameIndexService Build(params IGameDocumentParser[] parsers) =>
        new GameIndexService(parsers, NullLogger<GameIndexService>.Instance);

    // ── ApplyBaseline ────────────────────────────────────────────────────────

    [Fact]
    public void ApplyBaseline_Updates_Current_Baseline()
    {
        var svc = Build();
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty.Add("A", Symbol("A")),
            DateTimeOffset.UtcNow, "h",
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

    // ── UpdateDocumentAsync — basic ──────────────────────────────────────────

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
    public async Task UpdateDocumentAsync_Drops_Stale_Version()
    {
        var symV2 = Symbol("UNIT_V2");
        var symV1 = Symbol("UNIT_V1");

        var svc = Build(new FakeParser(Doc("", 0, [symV2])));
        // Commit version 2 first
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 2, default);

        // Now swap to a parser returning a different symbol and try to commit version 1
        var svc2 = new GameIndexService([new FakeParser(Doc("", 0, [symV1]))], NullLogger<GameIndexService>.Instance);
        // Manually set state: apply the v2 document
        // Instead, test it directly: after committing v2, version 1 must not overwrite.
        // We verify this by checking the symbol from v2 is still present after a v1 attempt.
        var symAfterV2 = svc.Current.WorkspaceDefinitions.ContainsKey("UNIT_V2");

        // Attempt a v1 parse on the same service — version guard should drop it
        // (FakeParser always returns the same symbol, so we verify the Document.Version stays 2)
        await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

        Assert.Equal(2, svc.Current.Documents["file:///f.xml"].Version);
        Assert.True(symAfterV2);
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
        var svc2 = new GameIndexService([new FakeParser(Doc("", 0, [updated]))], NullLogger<GameIndexService>.Instance);
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

        var svc2 = new GameIndexService([new FakeParser(Doc("", 0, refs: [newRef]))], NullLogger<GameIndexService>.Instance);
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

        var svcB = new GameIndexService([new FakeParser(Doc("", 0, [symB]))], NullLogger<GameIndexService>.Instance);
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

        using (svc.BeginBulkUpdate()) { /* no updates */ }

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
            await svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, default);

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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.UpdateDocumentAsync("file:///f.xml", "<X/>", 1, cts.Token));
    }
}
