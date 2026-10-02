// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics.Suppression;

/// <summary>
///     The two report actions every diagnostic offers beside its suppressions: open a prefilled
///     GitHub issue, or open the same report in an editor tab for anyone without an account.
/// </summary>
public sealed class DiagnosticReportActionTest
{
    private static readonly DocumentUri Uri =
        DocumentUri.From("file:///c:/mods/mymod/data/xml/groundinfantry.xml");

    private static readonly DiagnosticId Id = DiagnosticIds.DamageNonzero;

    private static readonly string[] Lines =
    [
        "<GameObjectFiles>",
        "  <Land_Terrain_Model_Mapping>",
        "    Temperate, EI_TROOPER.ALO",
        "  </Land_Terrain_Model_Mapping>"
    ];

    private static readonly Diagnostic Diagnostic = new()
    {
        Range = new LspRange(new Position(1, 2), new Position(2, 30)),
        Severity = DiagnosticSeverity.Error,
        Message = "Not a valid music event entry",
        Code = Id.ToString()
    };

    private static List<CodeAction> Actions()
    {
        return SuppressionCodeActionBuilder
            .Build(Uri, Diagnostic, Id, SuppressionCommentFormat.Xml, Lines, [])
            .Select(a => a.CodeAction!)
            .ToList();
    }

    [Fact]
    public void OffersTheGitHubReport()
    {
        var action = Assert.Single(Actions(), a => a.Title == $"Report {Id} on GitHub");

        Assert.Equal(DiagnosticReportCommands.ReportOnGitHub, action.Command!.Name);
        Assert.False(action.IsPreferred);
    }

    [Fact]
    public void OffersTheEditorReport()
    {
        var action = Assert.Single(Actions(), a => a.Title == $"Open report for {Id} in editor");

        Assert.Equal(DiagnosticReportCommands.OpenInEditor, action.Command!.Name);
        Assert.False(action.IsPreferred);
    }

    [Fact]
    public void CarriesTheDiagnosticAndEveryLineItsRangeTouches()
    {
        // Every line, not just the first: a multi-line value is exactly the case a false positive
        // report cannot do without. The file NAME travels; its path does not.
        var report = (JObject)Actions().First(a => a.Command?.Name == DiagnosticReportCommands.ReportOnGitHub)
            .Command!.Arguments![0];

        Assert.Equal(Id.ToString(), (string?)report["id"]);
        Assert.Equal("Error", (string?)report["severity"]);
        Assert.Equal("Not a valid music event entry", (string?)report["message"]);
        Assert.Equal("groundinfantry.xml", (string?)report["fileName"]);
        Assert.Equal(1, (int?)report["startLine"]);
        Assert.Equal(["  <Land_Terrain_Model_Mapping>", "    Temperate, EI_TROOPER.ALO"],
            report["lines"]!.Select(t => (string)t!).ToArray());
    }

    [Fact]
    public void ComesAfterTheSuppressions()
    {
        // Reporting is the rarer act; the suppressions keep their place at the top.
        var titles = Actions().Select(a => a.Title).ToList();

        Assert.True(titles.IndexOf($"Suppress {Id} across the project") < titles.IndexOf($"Report {Id} on GitHub"));
    }
}