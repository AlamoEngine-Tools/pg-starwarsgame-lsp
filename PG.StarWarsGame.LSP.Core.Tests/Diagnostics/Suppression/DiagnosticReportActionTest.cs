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

    private static string[] ReportedLines(string[] lines, Diagnostic diagnostic)
    {
        var report = (JObject)SuppressionCodeActionBuilder
            .Build(Uri, diagnostic, Id, SuppressionCommentFormat.Xml, lines, [])
            .Select(a => a.CodeAction!)
            .First(a => a.Command?.Name == DiagnosticReportCommands.ReportOnGitHub)
            .Command!.Arguments![0];
        return report["lines"]!.Select(t => (string)t!).ToArray();
    }

    [Fact]
    public void AVeryLongRange_KeepsItsStartAndEnd_AndSaysWhatWasLeftOut()
    {
        // A whole-object diagnostic can span hundreds of lines, which no prefilled issue survives.
        // The start shows where it begins and the end where the value closes; the middle goes.
        var lines = Enumerable.Range(0, 100).Select(i => $"line {i}").ToArray();
        var diagnostic = Diagnostic with { Range = new LspRange(new Position(0, 0), new Position(99, 3)) };

        var reported = ReportedLines(lines, diagnostic);

        Assert.Equal(41, reported.Length);
        Assert.Equal("line 0", reported[0]);
        Assert.Equal("line 19", reported[19]);
        Assert.Equal("... 60 lines omitted ...", reported[20]);
        Assert.Equal("line 80", reported[21]);
        Assert.Equal("line 99", reported[40]);
    }

    [Fact]
    public void AVeryLongLine_IsCut()
    {
        var lines = new[] { new string('x', 1000) };
        var diagnostic = Diagnostic with { Range = new LspRange(new Position(0, 0), new Position(0, 5)) };

        var line = Assert.Single(ReportedLines(lines, diagnostic));

        Assert.Equal(new string('x', 400) + "...", line);
    }

    [Fact]
    public void ARangeOfFortyLines_IsSentWhole()
    {
        var lines = Enumerable.Range(0, 40).Select(i => $"line {i}").ToArray();
        var diagnostic = Diagnostic with { Range = new LspRange(new Position(0, 0), new Position(39, 3)) };

        Assert.Equal(lines, ReportedLines(lines, diagnostic));
    }

    [Fact]
    public void ComesAfterTheSuppressions()
    {
        // Reporting is the rarer act; the suppressions keep their place at the top.
        var titles = Actions().Select(a => a.Title).ToList();

        Assert.True(titles.IndexOf($"Suppress {Id} across the project") < titles.IndexOf($"Report {Id} on GitHub"));
    }
}