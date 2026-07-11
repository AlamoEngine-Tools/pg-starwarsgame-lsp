// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Smoke tests for the story-dialog language service against the vanilla foc/ workspace.
///     Requires the workspace's .pgproj to declare <c>directories.storyDialog</c> (the registry
///     scope); skipped otherwise so machines with an un-migrated local fixture stay green.
/// </summary>
[Trait("Category", "E2E")]
public sealed class StoryDialogSmokeTest : IClassFixture<LspServerFixture>, IAsyncDisposable
{
    private readonly List<DocumentUri> _openedUris = [];
    private readonly LspServerFixture _fixture;

    public StoryDialogSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var uri in _openedUris)
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri }
            });

        if (_openedUris.Count > 0)
            await Task.Delay(500);
    }

    private static string DialogDir =>
        Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "Scripts", "Story");

    [Fact]
    public async Task DialogScript_VanillaTypo_GetsUnknownCommandDiagnostic()
    {
        RequireStoryDialogWorkspace();
        await WaitForScanAsync();

        // Dialog_tutorial_07.txt ships with a real typo: "TEXTCOLOR255 255 255 176".
        var filePath = Path.Combine(DialogDir, "Dialog_tutorial_07.txt");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenAsync(filePath);

        var diags = await received;
        Assert.Contains(diags.Diagnostics!, d =>
            d.Message.Contains("TEXTCOLOR255", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CleanDialogScript_PublishesDiagnosticsOnOpen()
    {
        RequireStoryDialogWorkspace();
        await WaitForScanAsync();

        var filePath = Path.Combine(DialogDir, "Dialog_tutorial_01.txt");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenAsync(filePath);

        var diags = await received;
        Assert.Equal(uri.ToString(), diags.Uri.ToString(), ignoreCase: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task OpenAsync(string filePath)
    {
        var uri = DocumentUri.FromFileSystemPath(filePath);
        _openedUris.Add(uri);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "plaintext",
                Version = 1,
                Text = await File.ReadAllTextAsync(filePath)
            }
        });

        await Task.Delay(200);
    }

    private static void RequireStoryDialogWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");

        var pgproj = Directory.EnumerateFiles(LspTestEnvironment.WorkspacePath, "*.pgproj").FirstOrDefault();
        if (pgproj is null || !File.ReadAllText(pgproj).Contains("storyDialog", StringComparison.OrdinalIgnoreCase))
            throw new Exception(
                "$XunitDynamicSkip$The workspace .pgproj declares no directories.storyDialog scope.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }
}
