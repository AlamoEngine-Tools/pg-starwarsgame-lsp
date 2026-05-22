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
        RequireSchema();

        var filePath = Path.Combine(_fixture.TestDataDirectory, "units.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = text
            }
        });

        // Position on line 4 (0-based): "    <" — cursor at char 5, right after '<'
        var result = await _fixture.Client.RequestCompletion(
            new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(4, 5),
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
        RequireSchema();

        var filePath = Path.Combine(_fixture.TestDataDirectory, "units.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = text
            }
        });

        // Line 3 (0-based): "    <Text_ID>NAME_TEST_UNIT</Text_ID>" — char 5 = 'T' in Text_ID
        var result = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(3, 5)
            }, CancellationToken.None);

        Assert.NotNull(result);
        var content = result.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("Text_ID", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagnostics_AfterDidOpen_PublishesDiagnosticsForUri()
    {
        RequireSchema();

        var filePath = Path.Combine(_fixture.TestDataDirectory, "units.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath);

        var diagnosticsReceived = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = text
            }
        });

        var diags = await diagnosticsReceived;
        Assert.Equal(uri, diags.Uri);
    }

    [Fact]
    public async Task WorkspaceScan_WithConfiguredWorkspacePath_ProducesDiagnosticsNotifications()
    {
        if (LspTestEnvironment.WorkspacePath is null)
            throw new Exception("$XunitDynamicSkip$" + "Set LSP_WORKSPACE_PATH to run this test.");

        // The workspace scan runs in the background after initialization.
        // Wait for any publishDiagnostics notification as evidence the scan produced output.
        var received = await WaitForAnyDiagnosticsAsync(TimeSpan.FromSeconds(90));
        Assert.True(received, "Workspace scan did not produce any publishDiagnostics within timeout.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void RequireSchema()
    {
        if (LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$" + "Set LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private Task<PublishDiagnosticsParams> WaitForDiagnosticsAsync(DocumentUri uri, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = _fixture.Client.Register(r => r.OnPublishDiagnostics(p =>
        {
            if (p.Uri == uri)
                tcs.TrySetResult(p);
        }));
        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() =>
        {
            tcs.TrySetCanceled(cts.Token);
            subscription.Dispose();
        });
        return tcs.Task;
    }

    private Task<bool> WaitForAnyDiagnosticsAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = _fixture.Client.Register(r => r.OnPublishDiagnostics(_ => { tcs.TrySetResult(true); }));
        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() =>
        {
            tcs.TrySetResult(false);
            subscription.Dispose();
        });
        return tcs.Task;
    }
}