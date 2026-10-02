// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The report actions on a real diagnostic, through the real code action pipeline: the XML
///     handler has its own kind filter and feature gate, either of which could drop them.
/// </summary>
/// <remarks>
///     Rides on the deliberately invalid <c>&lt;Damage&gt;0&lt;/Damage&gt;</c> in the authored
///     <c>Groundinfantry.xml</c> - see its header comment.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class DiagnosticReportActionSmokeTest(E2eModServerFixture fixture) : IClassFixture<E2eModServerFixture>
{
    [Fact]
    public async Task ADiagnostic_OffersBothReportActions_WithItsSourceAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run the report action test.");

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath, "Data", "Xml", "Groundinfantry.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken)
            }
        });

        var damage = Assert.Single((await received).Diagnostics,
            d => d.Message.Contains("<Damage> must be greater than 0", StringComparison.Ordinal));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var actions = await fixture.Client.RequestCodeAction(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Range = damage.Range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(damage) }
        }, cts.Token);

        var commands = actions!
            .Where(a => a.IsCodeAction)
            .Select(a => a.CodeAction!.Command)
            .Where(c => c is not null)
            .ToList();

        Assert.Contains(commands, c => c!.Name == DiagnosticReportCommands.OpenInEditor);
        var github = Assert.Single(commands, c => c!.Name == DiagnosticReportCommands.ReportOnGitHub);
        var report = (JObject)github!.Arguments![0];
        Assert.Equal(damage.Code?.String, (string?)report["id"]);
        Assert.Equal("Groundinfantry.xml", (string?)report["fileName"]);
        Assert.Contains(report["lines"]!, l => ((string)l!).Contains("<Damage>0</Damage>"));
    }
}
