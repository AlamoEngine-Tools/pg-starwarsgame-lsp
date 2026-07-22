// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E pin for the feature-flag wire contract: a client-supplied <c>features</c> node in
///     <c>initializationOptions</c> must selectively disable only the flags it names - everything
///     else keeps the server's all-true default. <see cref="FeatureFlagsServerFixture" /> turns off
///     <c>xml.completion</c>, <c>xml.diagnostics</c>, and <c>tools.localisation</c>; go-to-definition
///     (left untouched) must still work, proving this isn't an all-or-nothing kill switch.
/// </summary>
[Trait("Category", "E2E")]
public sealed class FeatureFlagSmokeTest : IClassFixture<FeatureFlagsServerFixture>
{
    private const string CorvettesXmlRel = "Data/Xml/Spaceunitscorvettes.xml";

    private readonly FeatureFlagsServerFixture _fixture;

    public FeatureFlagSmokeTest(FeatureFlagsServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Completion_XmlCompletionFlagOff_ReturnsEmpty()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var (uri, lines) = await OpenCorvettesAsync();
        try
        {
            var (line, col) = FindFirstGrandchildElementPosition(lines);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
                }, cts.Token);

            Assert.NotNull(result);
            Assert.Empty(result.Items);
        }
        finally
        {
            CloseDocument(uri);
        }
    }

    [Fact]
    public async Task GoToDefinition_XmlGoToDefinitionFlagLeftOn_StillResolvesWorkspaceDefinition()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var (uri, lines) = await OpenCorvettesAsync();
        try
        {
            var (line, col) = FindXmlTagBodyValuePosition(lines, "SFXEvent_Select", "Unit_Select_Tartan");
            Assert.True(line >= 0, "Could not find <SFXEvent_Select> Unit_Select_Tartan in Spaceunitscorvettes.xml");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _fixture.Client.RequestDefinition(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = uri },
                    Position = new Position(line, col)
                }, cts.Token);

            Assert.NotNull(result);
            var locations = result!
                .Select(l => l.IsLocationLink ? l.LocationLink!.TargetUri : l.Location!.Uri).ToList();
            Assert.NotEmpty(locations);
            Assert.Contains(locations, u =>
                u.ToString().Contains("Sfxeventsunitscorvettes", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CloseDocument(uri);
        }
    }

    [Fact]
    public async Task Diagnostics_XmlDiagnosticsFlagOff_NonePublishedAfterOpen()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var (uri, lines) = await OpenCorvettesAsync(false);
        var diagnosticsReceived = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(3));
        try
        {
            _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = uri, LanguageId = "xml", Version = 1,
                    Text = string.Join(Environment.NewLine, lines)
                }
            });

            await Assert.ThrowsAsync<TaskCanceledException>(() => diagnosticsReceived);
        }
        finally
        {
            CloseDocument(uri);
        }
    }

    [Fact]
    public async Task GetLocalisationProjects_ToolsLocalisationFlagOff_ReturnsEmptyProjects()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await _fixture.Client.SendRequest(new GetLocalisationProjectsParams(), cts.Token);

        Assert.Empty(result.Projects);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(DocumentUri uri, string[] lines)> OpenCorvettesAsync(bool alreadyOpened = true)
    {
        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var path = Path.Combine(workspace, CorvettesXmlRel);
        var uri = DocumentUri.FromFileSystemPath(path);
        var lines = await File.ReadAllLinesAsync(path);

        if (!alreadyOpened) return (uri, lines);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, LanguageId = "xml", Version = 1,
                Text = string.Join(Environment.NewLine, lines)
            }
        });
        await Task.Delay(300);
        return (uri, lines);
    }

    private void CloseDocument(DocumentUri uri)
    {
        _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }
        });
    }

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

    private static (int line, int col) FindXmlTagBodyValuePosition(
        string[] lines, string tagName, string value)
    {
        var tagOpen = $"<{tagName}>";
        for (var i = 0; i < lines.Length; i++)
        {
            var tagIdx = lines[i].IndexOf(tagOpen, StringComparison.OrdinalIgnoreCase);
            if (tagIdx < 0) continue;
            var searchFrom = tagIdx + tagOpen.Length;
            var valueIdx = lines[i].IndexOf(value, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (valueIdx < 0) continue;
            return (i, valueIdx);
        }

        return (-1, -1);
    }

    private static void RequireEawWorkspace()
    {
        if (LspTestEnvironment.EawWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$eaw/ workspace or schema/eaw/ not found - cannot run feature-flag smoke tests.");
    }

    private async Task WaitForFullScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(180)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 180 s.");
    }
}