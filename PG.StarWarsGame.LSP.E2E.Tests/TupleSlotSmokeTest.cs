// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The slots of <c>Land_Terrain_Model_Mapping</c>, through the real schema and baseline: each item
///     is checked as what its slot says, and a model item offers its preview.
/// </summary>
/// <remarks>
///     Rides on <c>E2E_BAD_TERRAIN_INFANTRY</c> in the authored <c>Groundinfantry.xml</c> - see its
///     header comment. The valid mapping above it is the control for the model lookup: the baseline
///     must resolve its models, or every one of them would warn.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class TupleSlotSmokeTest(E2eModServerFixture fixture) : IClassFixture<E2eModServerFixture>
{
    [Fact]
    public async Task EachItem_IsCheckedAsItsSlot_AndAModelItemOffersItsPreviewAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run the tuple slot test.");

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath, "Data", "Xml", "Groundinfantry.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        var received = fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, LanguageId = "xml", Version = 1, Text = text }
        });

        var messages = (await received).Diagnostics.Select(d => d.Message).ToList();

        Assert.Contains(messages, m => m.Contains("'Not_A_Terrain' is not a MapEnvironment value", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("'E2E_NO_SUCH_MODEL.ALO' was not found", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("EI_TROOPER", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(messages, m => m.Contains("'Arctic'", StringComparison.OrdinalIgnoreCase));

        var lines = text.Split('\n');
        var line = Array.FindIndex(lines, l => l.Contains("Not_A_Terrain, EI_TROOPER.ALO", StringComparison.Ordinal));
        var character = lines[line].IndexOf("EI_TROOPER.ALO", StringComparison.Ordinal) + 3;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var actions = await fixture.Client.RequestCodeAction(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, character, line, character),
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>() }
        }, cts.Token);

        var preview = Assert.Single(actions!.Where(a => a.IsCodeAction).Select(a => a.CodeAction!),
            a => a.Command?.Name == "aet-eaw-edit.lsp.previewModel");
        Assert.Equal("Preview model EI_TROOPER.ALO", preview.Title);
        Assert.Null(preview.Disabled);
    }
}
