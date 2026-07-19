// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Regression coverage for #45 ("I need to open another file to get the annotations next to
///     tags"). Reproduces the reported ordering: VS Code restores its open editors immediately, so
///     the document is opened while the startup pipeline is still running and the first inlay-hint
///     request is answered against an empty index. Once the scan completes the server must be able
///     to serve hints for that same document with no further user interaction - no edit, and above
///     all no second file opened.
///     <para>
///         This owns its own fixture on purpose: <c>IClassFixture</c> gives one server per class, and
///         only the first test against a fresh server genuinely races startup.
///     </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class StartupInlayHintSmokeTest : IClassFixture<EawLspServerFixture>
{
    private const string SourceRel = "Data/Xml/Spaceunitscorvettes.xml";

    private readonly EawLspServerFixture _fixture;

    public StartupInlayHintSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DocumentOpenedDuringStartup_ServesInlayHintsOnceScanCompletes()
    {
        if (LspTestEnvironment.EawWorkspacePath is null)
            throw new Exception("$XunitDynamicSkip$Set the EaW workspace to run this test.");

        var path = Path.Combine(LspTestEnvironment.EawWorkspacePath, SourceRel);
        var text = await File.ReadAllTextAsync(path);
        var uri = DocumentUri.FromFileSystemPath(path);
        var fullRange = new Range(new Position(0, 0), new Position(text.Split('\n').Length, 0));

        // Open FIRST, without waiting - the didOpen races the still-running pipeline.
        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = uri, LanguageId = "xml", Version = 1, Text = text }
        });

        try
        {
            var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(90)));
            if (completed != _fixture.ScanCompleted)
                throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 90 s.");

            // Give the gate's buffered didOpen a moment to reach the workspace host.
            await Task.Delay(1500);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var hints = await _fixture.Client.RequestInlayHints(new InlayHintParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri }, Range = fullRange
            }, cts.Token);

            Assert.NotNull(hints);
            Assert.NotEmpty(hints!);
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri }
            });
        }
    }
}
