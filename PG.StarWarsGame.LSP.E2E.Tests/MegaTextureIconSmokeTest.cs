// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Guards GUI art that ships INSIDE a mega texture against a missing-texture false positive,
///     opening real workspace files.
///     <para>
///         An icon like <c>i_button_y_wing.tga</c> is not a file anywhere - it is an entry in
///         <c>data/art/textures/mt_commandbar.mtd</c>, recorded uppercase and reached only through
///         the icon catalog. The asset file index therefore could not resolve it and the editor
///         warned that the icon did not exist, while the encyclopedia preview drew it perfectly well
///         from the very same catalog. The two now agree.
///     </para>
///     <para>
///         Which mega texture is authoritative follows the WORKSPACE, so these run against whichever
///         workspace the fixture is pointed at rather than assuming a particular game.
///     </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class MegaTextureIconSmokeTest : IClassFixture<LspServerFixture>, IAsyncDisposable
{
    private readonly LspServerFixture _fixture;
    private readonly List<DocumentUri> _openedUris = [];

    public MegaTextureIconSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    private static string XmlDir => Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML");

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

    /// <summary>An FoC unit whose icon is in FoC's mega texture and not EaW's.</summary>
    [Fact]
    public async Task IconPackedIntoMegaTexture_NotFlaggedAsMissingTexture()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var diags = await OpenAndAwaitDiagnosticsAsync("Units_space_empire_executor.xml");

        AssertNotReportedMissing(diags, "i_button_EV_ExecutorStarDestroyer");
    }

    /// <summary>
    ///     Reported still warning after the first fix landed. A different document, and a name with
    ///     underscores - which is where a suffix or casing rule comes apart.
    /// </summary>
    [Fact]
    public async Task IconInBothMegaTextures_NotFlaggedAsMissingTexture()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var diags = await OpenAndAwaitDiagnosticsAsync("Spaceunitsfighters.xml");

        AssertNotReportedMissing(diags, "i_button_y_wing");
    }

    private async Task<PublishDiagnosticsParams> OpenAndAwaitDiagnosticsAsync(string fileName)
    {
        var filePath = Path.Combine(XmlDir, fileName);
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(20));
        _openedUris.Add(uri);
        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, LanguageId = "xml", Version = 1,
                Text = await File.ReadAllTextAsync(filePath)
            }
        });

        return await received;
    }

    private static void AssertNotReportedMissing(PublishDiagnosticsParams diags, string icon)
    {
        var falsePositives = diags.Diagnostics
            .Where(d => d.Message.Contains(icon, StringComparison.OrdinalIgnoreCase)
                        && d.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Message)
            .ToList();

        Assert.True(falsePositives.Count == 0,
            $"Icon {icon} was reported missing, but it ships inside this workspace's mega texture, "
            + "which is where the game and the preview both read it from. Offending diagnostics:\n"
            + string.Join("\n", falsePositives));
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }
}
