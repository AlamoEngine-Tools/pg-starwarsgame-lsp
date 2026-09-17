// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

[Trait("Category", "E2E")]
public sealed class AbilityTypeSmokeTest : IClassFixture<E2eModServerFixture>
{
    private readonly E2eModServerFixture _fixture;

    public AbilityTypeSmokeTest(E2eModServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnitAbility_InvalidType_EmitsDiagnosticForUnknownAbilityType()
    {
        RequireE2eWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath!, "Data", "XML", "Spaceunitsfighters.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

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

        var diags = await received;
        Assert.Contains(diags.Diagnostics,
            d => d.Message.Contains("HUN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnitAbility_TypeTag_HoverDoesNotShowGameObjectType()
    {
        RequireE2eWorkspace();
        await WaitForScanAsync();

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath!, "Data", "XML", "Spaceunitsfighters.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

        var (line, col) = FindLineContaining(lines, "<Type>HUN</Type>");
        Assert.True(line >= 0, "Could not find <Type>HUN</Type> in eaw Spaceunitsfighters.xml");

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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var hover = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, cts.Token);

        Assert.NotNull(hover);
        var content = hover.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.DoesNotContain("GameObjectType", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SpaceUnit", content, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(content);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void RequireE2eWorkspace()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run ability type smoke tests.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$EAW workspace scan did not complete within 60 s.");
    }

    private static (int line, int col) FindLineContaining(string[] lines, string needle)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var lt = lines[i].IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (lt < 0) continue;
            var tagStart = lines[i].IndexOf('<', lt);
            return (i, tagStart + 1);
        }

        return (-1, -1);
    }
}