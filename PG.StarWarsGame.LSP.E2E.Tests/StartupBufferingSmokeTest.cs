// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

// Reproduces the live editor ordering that the other smoke tests miss: a document is opened
// (didOpen) WHILE the startup pipeline is still running, so the StartupGate buffers it and must
// drain it when it opens. If draining a buffered thunk hangs or throws, the gate never opens —
// $/workspaceScanComplete never fires and the workspace host stays empty (hover dead).
[Trait("Category", "E2E")]
public sealed class StartupBufferingSmokeTest : IClassFixture<LspServerFixture>
{
    private readonly LspServerFixture _fixture;

    public StartupBufferingSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OpenDuringStartup_GateOpens_AndHoverWorks()
    {
        if (LspTestEnvironment.WorkspacePath is null)
            throw new Exception("$XunitDynamicSkip$Workspace not available.");

        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath, "Data", "XML", "HardPoints.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

        // Open FIRST — before waiting for the scan — so the didOpen races the pipeline and is
        // most likely buffered by the still-closed gate.
        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = string.Join(Environment.NewLine, lines)
            }
        });

        // If the gate hangs draining the buffered open, this notification never arrives.
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.True(completed == _fixture.ScanCompleted,
            "$/workspaceScanComplete never arrived — the StartupGate likely hung or threw while draining the buffered didOpen.");

        // Give the drain a moment to apply the buffered open to the workspace host.
        await Task.Delay(500);

        var (line, col) = FindFirstChildElementPosition(lines);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, cts.Token);

        Assert.NotNull(result);
        var content = result!.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("HardPoint", content, StringComparison.OrdinalIgnoreCase);
    }

    private static (int line, int col) FindFirstChildElementPosition(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt <= 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            return (i, lt + 1);
        }

        return (1, 1);
    }
}