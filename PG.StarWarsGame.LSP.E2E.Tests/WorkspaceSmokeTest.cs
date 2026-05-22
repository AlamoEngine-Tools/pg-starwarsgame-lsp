// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

[Trait("Category", "E2E")]
public sealed class WorkspaceSmokeTest : IClassFixture<LspServerFixture>
{
    private readonly LspServerFixture _fixture;

    public WorkspaceSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WorkspaceScan_SfxEventsFile_HoverReturnsSfxEventType()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML", "Sfxevents.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

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

        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("SFXEvent", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceScan_HardPointsFile_HoverReturnsHardPointType()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML", "HardPoints.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

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

        // Depth-1 elements here have arbitrary names (e.g. HP_Star_Destroyer_Weapon_FL).
        // Hover only returns HardPoint type info if FileTypeRegistry maps this file via the
        // hardpointdatafiles.xml metafile — proving the workspace scan populated the registry.
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("HardPoint", content, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanStarted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanStarted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not produce diagnostics within 60 s.");
    }

    /// <summary>
    /// Returns the position of the first indented child element in an XML file.
    /// Root elements and XML declarations (col 0) are skipped.
    /// </summary>
    private static (int line, int col) FindFirstChildElementPosition(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt <= 0) continue;                     // skip root (lt==0) and blank lines
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            return (i, lt + 1);                        // col on first char of tag name
        }
        return (1, 1);
    }
}
