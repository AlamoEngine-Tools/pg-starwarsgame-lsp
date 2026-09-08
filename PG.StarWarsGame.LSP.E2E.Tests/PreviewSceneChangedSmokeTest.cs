// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     An edit to the tree reaches an open preview.
/// </summary>
/// <remarks>
///     <para>
///         The unit tests prove <c>PreviewSceneChangeNotifier</c> sends on
///         <c>IndexChanged</c> with a fake index. This proves the other half - that the
///         notification is wired into the real server and actually ARRIVES at a client.
///     </para>
///     <para>
///         That half is not a formality. <c>didChangeWatchedFiles</c> was implemented, registered
///         and completely dead for months: not one notification ever arrived, and every unit test
///         around it passed the whole time. A push nobody has watched land is not a working push.
///     </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class PreviewSceneChangedSmokeTest(EawLspServerFixture fixture)
    : IClassFixture<EawLspServerFixture>
{
    [Fact]
    public async Task EditingAnXmlDocument_PushesPreviewSceneChanged()
    {
        RequireEawWorkspace();
        await WaitForScanAsync();

        var filePath = Path.Combine(
            LspTestEnvironment.EawWorkspacePath!, "Data", "XML", "Spaceunitsfighters.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath);

        // Armed BEFORE the edit. The notification is debounced by 100 ms, not delayed by a round
        // trip, so subscribing afterwards is a race this test would lose intermittently.
        var arrived = fixture.WaitForPreviewSceneChangedAsync(TimeSpan.FromSeconds(20));

        fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = text
            }
        });

        Assert.True(await arrived,
            "aet/previewSceneChanged never arrived. The notifier is registered and unit-tested, so "
            + "a failure here means it is not reaching the client - which is the whole point of "
            + "this test.");
    }

    /// <summary>
    ///     The control for the test above.
    /// </summary>
    /// <remarks>
    ///     Without it, that test would pass just as happily if the server pushed this notification
    ///     continuously, or if background indexing kept raising <c>IndexChanged</c> after the scan
    ///     reported itself complete - and it would be proving nothing about the edit at all. A
    ///     settled workspace has to be silent for "an edit causes a push" to mean anything.
    /// </remarks>
    [Fact]
    public async Task WithNoEdit_NothingIsPushed()
    {
        RequireEawWorkspace();
        await WaitForScanAsync();

        // Long enough to clear the notifier's 100 ms debounce many times over.
        Assert.False(await fixture.WaitForPreviewSceneChangedAsync(TimeSpan.FromSeconds(3)),
            "aet/previewSceneChanged arrived with nothing edited, so the test above proves "
            + "nothing about editing.");
    }

    private static void RequireEawWorkspace()
    {
        if (LspTestEnvironment.EawWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$eaw/ workspace not found; cannot run the preview refresh smoke test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(
            fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));

        if (completed != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }
}
