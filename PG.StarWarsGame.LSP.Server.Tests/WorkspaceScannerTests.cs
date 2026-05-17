using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class WorkspaceScannerTests
{
    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeParser : IGameDocumentParser
    {
        private readonly string _ext;
        public FakeParser(string ext = ".xml") => _ext = ext;
        public bool CanParse(string ext) => ext.Equals(_ext, StringComparison.OrdinalIgnoreCase);
        public ValueTask<DocumentIndex> ParseAsync(string uri, string text, int version, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        private readonly object _lock = new();
        public readonly List<(string Uri, int Version)> Calls = [];
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged { add { } remove { } }
        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock) Calls.Add((uri, version));
            return Task.CompletedTask;
        }
        public void RemoveDocument(string uri) { }
        public void ApplyBaseline(BaselineIndex baseline) { }
    }

    private static string Root(string sub) =>
        Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);

    private static WorkspaceScanner Build(MockFileSystem fs, FakeIndexService svc,
        params IGameDocumentParser[] parsers) =>
        new(fs, parsers, svc, NullLogger<WorkspaceScanner>.Instance, workDone: null);

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanAsync_ParseableFiles_AreIndexedWithVersionZero()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.xml")] = new("<Root/>"),
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        Assert.Equal(2, svc.Calls.Count);
        Assert.All(svc.Calls, c => Assert.Equal(0, c.Version));
    }

    [Fact]
    public async Task ScanAsync_UnparseableExtension_Skipped()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
            [Path.Combine(root, "b.lua")] = new("-- lua"),
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser(".xml")).ScanAsync([root], CancellationToken.None);

        Assert.Single(svc.Calls);
        Assert.EndsWith(".xml", svc.Calls[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_MultipleWorkspaceFolders_AllFilesIndexed()
    {
        var root1 = Root("ws1");
        var root2 = Root("ws2");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root1, "a.xml")] = new("<Root/>"),
            [Path.Combine(root2, "b.xml")] = new("<Root/>"),
        });
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root1, root2], CancellationToken.None);

        Assert.Equal(2, svc.Calls.Count);
    }

    [Fact]
    public async Task ScanAsync_PreCancelledToken_ThrowsAndDoesNotIndex()
    {
        var root = Root("ws");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "a.xml")] = new("<Root/>"),
        });
        var svc = new FakeIndexService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Build(fs, svc, new FakeParser()).ScanAsync([root], cts.Token));

        Assert.Empty(svc.Calls);
    }

    [Fact]
    public async Task ScanAsync_EmptyFolder_IndexesNothing()
    {
        var root = Root("ws");
        var fs = new MockFileSystem();
        fs.AddDirectory(root);
        var svc = new FakeIndexService();

        await Build(fs, svc, new FakeParser()).ScanAsync([root], CancellationToken.None);

        Assert.Empty(svc.Calls);
    }
}
