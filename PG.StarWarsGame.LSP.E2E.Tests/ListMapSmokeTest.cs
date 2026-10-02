// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     <c>Presence_Induced_Animations</c> read as a <c>ListMap</c> through the real schema: an
///     animation state, then the units it applies to.
/// </summary>
/// <remarks>
///     Rides on <c>E2E_BAD_PRESENCE_INFANTRY</c> in the authored <c>Groundinfantry.xml</c> - see its
///     header comment. <c>E2E_TERRAIN_MAPPED_INFANTRY</c> carries the shipped shape and is the
///     control: before the type was generic its first item was reported as an unknown faction.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class ListMapSmokeTest(E2eModServerFixture fixture) : IClassFixture<E2eModServerFixture>
{
    [Fact]
    public async Task AValueThatDoesNotStartWithAnAnimationState_IsReported_TheShippedShapeIsNotAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run the ListMap test.");

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath, "Data", "Xml", "Groundinfantry.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        var received = fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, LanguageId = "xml", Version = 1, Text = text }
        });

        var diagnostics = (await received).Diagnostics.ToList();
        var presence = diagnostics.Where(d => d.Message.Contains("<Presence_Induced_Animations>", StringComparison.Ordinal)).ToList();

        var dropped = Assert.Single(presence);
        Assert.Equal(DiagnosticSeverity.Error, dropped.Severity);
        Assert.Contains("'Atention' is none", dropped.Message);
        var lines = text.Split('\n');
        Assert.Contains("Atention", lines[dropped.Range.Start.Line]);
    }
}
