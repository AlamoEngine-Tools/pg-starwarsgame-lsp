// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Regression tests for find-all-references on Projectile objects.
///     Projectiles were returning zero references even though Fire_Projectile_Type
///     tags in Hardpoints.xml reference them as plain game object names.
/// </summary>
[Trait("Category", "E2E")]
public sealed class XmlProjectileReferencesSmokeTest : IClassFixture<LspServerFixture>
{
    private readonly LspServerFixture _fixture;

    public XmlProjectileReferencesSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    private static string XmlDir =>
        Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML");

    // ── find-all-references from the definition site ──────────────────────────

    [Fact(Skip =
        "Disabled, not sure what the test is supposed to be testing for - projectiles in the base game do not have hardpoints.")]
    public async Task Projectile_FindAllReferences_ExcludeDeclaration_ReturnsHardpointUsages()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Projectiles.xml"));
        var (line, col) = FindProjectileNameValuePosition(lines);
        Assert.True(line >= 0, "Could not find a <Projectile Name=\"...\"> in Projectiles.xml");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var refs = await _fixture.Client.RequestReferences(new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
            Context = new ReferenceContext { IncludeDeclaration = false }
        }, cts.Token);

        Assert.NotNull(refs);
        var locations = refs!.ToList();
        Assert.NotEmpty(locations);
        Assert.Contains(locations, l =>
            l.Uri.ToString().Contains("Hardpoints", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Skip =
        "Disabled, not sure what the test is supposed to be testing for - projectiles in the base game do not have hardpoints.")
    ]
    public async Task Projectile_FindAllReferences_IncludeDeclaration_ContainsDefinitionAndUsages()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Projectiles.xml"));
        var (line, col) = FindProjectileNameValuePosition(lines);
        Assert.True(line >= 0, "Could not find a <Projectile Name=\"...\"> in Projectiles.xml");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var refs = await _fixture.Client.RequestReferences(new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
            Context = new ReferenceContext { IncludeDeclaration = true }
        }, cts.Token);

        Assert.NotNull(refs);
        var locations = refs!.ToList();
        Assert.Contains(locations, l =>
            l.Uri.ToString().Contains("Projectiles", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(locations, l =>
            l.Uri.ToString().Contains("Hardpoints", StringComparison.OrdinalIgnoreCase));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanStarted, Task.Delay(TimeSpan.FromSeconds(120)));
        if (completed != _fixture.ScanStarted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 120 s.");
    }

    private async Task<(DocumentUri uri, string[] lines)> OpenAndWaitAsync(string filePath)
    {
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
        return (uri, lines);
    }

    /// <summary>
    ///     Scans <paramref name="lines" /> for the first <c>&lt;Projectile Name="VALUE"</c>
    ///     opening tag and returns the 0-based (line, col) of a character in the middle of VALUE.
    ///     Returns (-1, -1) when not found.
    /// </summary>
    private static (int line, int col) FindProjectileNameValuePosition(string[] lines)
    {
        const string marker = "<Projectile Name=\"";
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var tagIdx = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (tagIdx < 0) continue;
            var valueStart = tagIdx + marker.Length;
            var valueEnd = s.IndexOf('"', valueStart);
            if (valueEnd < 0) continue;
            return (i, (valueStart + valueEnd) / 2);
        }

        return (-1, -1);
    }
}