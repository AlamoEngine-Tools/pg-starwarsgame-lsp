// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

[Trait("Category", "E2E")]
public sealed class XmlSmokeTest : IClassFixture<LspServerFixture>
{
    private readonly LspServerFixture _fixture;

    public XmlSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ServerInitialized_ClientHandshakeCompletes_NoException()
    {
        Assert.NotNull(_fixture.Client);
    }

    [Fact]
    public async Task Completion_AfterDidOpen_ReturnsItems()
    {
        RequireWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML", "CIN_SpaceUnitsFrigates.xml");
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

        await Task.Delay(200);

        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await _fixture.Client.RequestCompletion(
            new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col),
                Context = new CompletionContext
                {
                    TriggerKind = CompletionTriggerKind.TriggerCharacter,
                    TriggerCharacter = "<"
                }
            }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public async Task Hover_OverKnownTag_ReturnsMarkdownContent()
    {
        RequireWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML", "CIN_SpaceUnitsFrigates.xml");
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

        await Task.Delay(200);

        var (line, col) = FindFirstGrandchildElementPosition(lines);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, cts.Token);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("Cinematic_Object_Only", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagnostics_AfterDidOpen_PublishesDiagnosticsForUri()
    {
        RequireWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML", "CIN_SpaceUnitsFrigates.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

        var diagnosticsReceived = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

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

        var diags = await diagnosticsReceived;
        Assert.Equal(uri, diags.Uri);
    }

    [Fact]
    public async Task WorkspaceScan_WithConfiguredWorkspacePath_ProducesDiagnosticsNotifications()
    {
        RequireWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML", "CIN_SpaceUnitsFrigates.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(30));

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 3,
                Text = string.Join(Environment.NewLine, lines)
            }
        });

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private Task<PublishDiagnosticsParams> WaitForDiagnosticsAsync(DocumentUri uri, TimeSpan timeout)
    {
        return _fixture.WaitForDiagnosticsAsync(uri, timeout);
    }

    /// <summary>
    ///     Returns the position of the first grandchild element - the first field tag
    ///     inside the first type container - so hover and completion tests hit a known tag.
    /// </summary>
    private static (int line, int col) FindFirstGrandchildElementPosition(string[] lines)
    {
        var firstChildLine = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt <= 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            firstChildLine = i;
            break;
        }

        if (firstChildLine < 0) return (1, 1);

        for (var i = firstChildLine + 1; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt < 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            return (i, lt + 1);
        }

        return (1, 1);
    }
}