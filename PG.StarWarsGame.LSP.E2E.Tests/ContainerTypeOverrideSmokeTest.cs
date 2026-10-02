// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     An object element that is not named after its schema type still belongs to the file's
///     container type, and keeps that type's validation overrides - checked against the real schema.
/// </summary>
/// <remarks>
///     A GameObjectType file holds <c>&lt;GroundInfantry&gt;</c>, which the schema does not know as a
///     type. Its tags used to resolve through the flat fallback, which drops owner restrictions, so
///     the vanilla <c>Land_Terrain_Model_Mapping</c> was reported as a bad music-event entry and the
///     vanilla <c>Presence_Induced_Animations</c> as naming an unknown faction.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class ContainerTypeOverrideSmokeTest : IClassFixture<E2eModServerFixture>
{
    private readonly E2eModServerFixture _fixture;

    public ContainerTypeOverrideSmokeTest(E2eModServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Overrides_declared_on_the_container_type_reach_its_objectsAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run container override tests.");

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath, "Data", "Xml", "Groundinfantry.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken)
            }
        });

        var messages = (await received).Diagnostics.Select(d => d.Message).ToList();

        Assert.DoesNotContain(messages, m => m.Contains("music event", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(messages, m => m.Contains("not a known faction", StringComparison.OrdinalIgnoreCase));
        // The control: without it, a server that validated nothing would pass the two checks above.
        Assert.Contains(messages, m => m.Contains("<Damage> must be greater than 0", StringComparison.Ordinal));
    }
}