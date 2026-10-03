// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Two game objects whose names share the engine's CRC-32 name hash, through the real server - the
///     PG.Commons hash the host registers, the index and the diagnostics pipeline.
/// </summary>
/// <remarks>
///     Rides on <c>E2E_CRC_BXO9XL</c> and <c>E2E_CRC_CDATBA</c> in the authored
///     <c>Groundinfantry.xml</c>, a real colliding pair (0xC492BBC0). Every other object in that file
///     is the control: none of them may be warned.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class ObjectNameCrcCollisionSmokeTest(E2eModServerFixture fixture) : IClassFixture<E2eModServerFixture>
{
    [Fact]
    public async Task BothObjectsOfACollidingPair_AreWarned_AndNothingElseIsAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run the name hash test.");

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath, "Data", "Xml", "Groundinfantry.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        var received = fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, LanguageId = "xml", Version = 1, Text = text }
        });

        var collisions = (await received).Diagnostics
            .Where(d => d.Message.Contains("same name hash", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, collisions.Count);
        Assert.All(collisions, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Contains(collisions, d => d.Message.StartsWith("'E2E_CRC_BXO9XL'", StringComparison.Ordinal)
                                         && d.Message.Contains("'E2E_CRC_CDATBA' (0xC492BBC0)"));
        Assert.Contains(collisions, d => d.Message.StartsWith("'E2E_CRC_CDATBA'", StringComparison.Ordinal));
    }
}
