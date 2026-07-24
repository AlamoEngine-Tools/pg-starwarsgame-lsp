// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke: renaming a workspace-file reference (a campaign <c>*_Story_Name</c> plot value)
///     offers only the base-name (stem) sub-range and produces a WorkspaceEdit that renames the
///     file on disk plus rewrites the referencing text.
/// </summary>
[Trait("Category", "E2E")]
public sealed class WorkspaceFileRenameSmokeTest : IClassFixture<EawLspServerFixture>
{
    private const string CampaignRel = "Data/Xml/Campaigns_Alpha.xml";
    private const string Value = "Story_Plots_Campaign_Empire.xml";
    private const string Stem = "Story_Plots_Campaign_Empire";

    private readonly EawLspServerFixture _fixture;

    public WorkspaceFileRenameSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PrepareRename_OnPlotReference_SpansStemOnly_NotExtension()
    {
        var (uri, line, col) = await OpenAndLocateAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var prepared = await _fixture.Client.PrepareRename(new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col)
        }, cts.Token);

        Assert.NotNull(prepared);
        Assert.NotNull(prepared!.Range);
        // The editable range is the stem, not the ".xml" extension.
        Assert.Equal(Stem.Length, prepared.Range!.End.Character - prepared.Range.Start.Character);
    }

    [Fact]
    public async Task Rename_OnPlotReference_RenamesFileAndRewritesReference()
    {
        var (uri, line, col) = await OpenAndLocateAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var edit = await _fixture.Client.RequestRename(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
            NewName = "Story_Plots_Campaign_Empire_Renamed"
        }, cts.Token);

        Assert.NotNull(edit);
        Assert.NotNull(edit!.DocumentChanges);
        var changes = edit.DocumentChanges!.ToList();

        var rename = Assert.Single(changes.Where(c => c.IsRenameFile).Select(c => c.RenameFile!));
        Assert.Contains("story_plots_campaign_empire.xml", rename.OldUri.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Story_Plots_Campaign_Empire_Renamed.xml", rename.NewUri.ToString(),
            StringComparison.OrdinalIgnoreCase);

        // At least the reference we invoked on must be rewritten.
        var textEdits = changes.Where(c => c.IsTextDocumentEdit).SelectMany(c => c.TextDocumentEdit!.Edits).ToList();
        Assert.Contains(textEdits, e => e.NewText == "Story_Plots_Campaign_Empire_Renamed");
    }

    private async Task<(DocumentUri Uri, int Line, int Col)> OpenAndLocateAsync()
    {
        if (LspTestEnvironment.EawWorkspacePath is null)
            throw new Exception("$XunitDynamicSkip$EAW workspace not configured.");
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(180)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 180 s.");

        var path = Path.Combine(LspTestEnvironment.EawWorkspacePath, CampaignRel);
        var uri = DocumentUri.FromFileSystemPath(path);
        var lines = await File.ReadAllLinesAsync(path);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = uri, LanguageId = "xml", Version = 1, Text = string.Join(Environment.NewLine, lines) }
        });
        await Task.Delay(300);

        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf("<Empire_Story_Name>" + Value, StringComparison.Ordinal);
            if (idx >= 0)
                return (uri, i, idx + "<Empire_Story_Name>".Length + 2); // cursor inside the stem
        }

        throw new Exception($"$XunitDynamicSkip$Could not find the plot reference in {CampaignRel}.");
    }
}
