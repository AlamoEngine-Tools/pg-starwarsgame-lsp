// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

[Trait("Category", "E2E")]
public sealed class AbilityTypeSmokeTest : IClassFixture<EawLspServerFixture>
{
    private readonly EawLspServerFixture _fixture;

    public AbilityTypeSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnitAbility_InvalidType_EmitsDiagnosticForUnknownAbilityType()
    {
        RequireEawWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.EawWorkspacePath!, "Data", "XML", "Spaceunitsfighters.xml");
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
        RequireEawWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.EawWorkspacePath!, "Data", "XML", "Spaceunitsfighters.xml");
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

    private static void RequireEawWorkspace()
    {
        if (LspTestEnvironment.EawWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$eaw/ workspace not found; cannot run ability type smoke tests.");
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