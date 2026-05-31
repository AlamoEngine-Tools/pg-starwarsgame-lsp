// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

[Trait("Category", "E2E")]
public sealed class TupleReferenceValidationSmokeTest : IClassFixture<EawLspServerFixture>
{
    private readonly EawLspServerFixture _fixture;

    public TupleReferenceValidationSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HardPointSfxMap_InvalidEntry_EmitsDiagnostic()
    {
        RequireEawWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.EawWorkspacePath!, "Data", "XML", "Spaceunitscapital.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);

        // Replace one valid SFXEvent_Attack_Hardpoint value with a malformed one (no comma)
        var injected = lines
            .Select(l => l.Contains("SFXEvent_Attack_Hardpoint") && l.Contains("HARD_POINT_WEAPON_LASER")
                ? l.Replace("HARD_POINT_WEAPON_LASER, Unit_HP_LASER_Calamari", "HARD_POINT_WEAPON_LASER_NO_COMMA")
                : l)
            .ToArray();

        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = string.Join(Environment.NewLine, injected)
            }
        });

        var diags = await received;
        Assert.Contains(diags.Diagnostics,
            d => d.Message.Contains("HARD_POINT_WEAPON_LASER_NO_COMMA", StringComparison.OrdinalIgnoreCase)
              || d.Message.Contains("hard point", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HardPointSfxMap_ValidEntries_EmitNoDiagnostics()
    {
        RequireEawWorkspace();

        var filePath = Path.Combine(LspTestEnvironment.EawWorkspacePath!, "Data", "XML", "Spaceunitscapital.xml");
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
        Assert.DoesNotContain(diags.Diagnostics,
            d => d.Message.Contains("hard point", StringComparison.OrdinalIgnoreCase)
              || d.Message.Contains("HardPoint", StringComparison.OrdinalIgnoreCase));
    }

    private static void RequireEawWorkspace()
    {
        if (LspTestEnvironment.EawWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$eaw/ workspace not found; cannot run tuple reference validation smoke tests.");
    }
}
