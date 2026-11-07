// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A vanilla story script's state machine findings, as the editor receives them (#252): the one
///     EaW story handler written without the message parameter.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LuaStoryHandlerFindingsSmokeTest : IClassFixture<EawLspServerFixture>
{
    private readonly EawLspServerFixture _fixture;

    public LuaStoryHandlerFindingsSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AStoryHandlerWithoutTheMessage_IsReported()
    {
        if (LspTestEnvironment.EawWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$eaw/ workspace or schema/eaw/ not found.");
        await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(180)));

        var path = Path.Combine(LspTestEnvironment.EawWorkspacePath, "Data", "Scripts", "Story",
            "Story_empire_actii_m06_land.lua");
        var uri = DocumentUri.FromFileSystemPath(path);
        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, LanguageId = "lua", Version = 1,
                Text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)
            }
        });

        var published = await _fixture.WaitForSettledDiagnosticsAsync(uri, TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(30));

        var codes = (published?.Diagnostics ?? []).Select(d => $"{d.Code?.String}@{d.Range.Start.Line + 1}").ToList();
        Assert.Contains("aetswg-011-0012@337", codes);
    }
}