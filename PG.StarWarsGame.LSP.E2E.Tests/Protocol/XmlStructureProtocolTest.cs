// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>
///     The 0.5.0 protocol's XML structure tests, XML-01 to XML-13, run against the scratch project: each
///     method is the test of that id, its assertions the protocol's expected outcomes.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class XmlStructureProtocolTest(XmlScratchFixture fixture) : IClassFixture<XmlScratchFixture>
{
    private const string Clean = "<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n" +
                                 "\t\t<Max_Speed>1.0</Max_Speed>\n\t</SpaceUnit>\n</SpaceUnits>\n";

    private string Xml(string file)
    {
        return Path.Combine(fixture.Root ?? throw Skip(), "Data", "XML", file);
    }

    private static Exception Skip()
    {
        return new Exception("$XunitDynamicSkip$C:\\temp\\aet-test-xml not found; run the protocol setup script.");
    }

    private Task<ProtocolDocument> OpenTestFile(string text)
    {
        return ProtocolDocument.OpenAsync(fixture, Xml("Xml_Test.xml"), text);
    }

    // Every finding of the structure groups: the game's (012-3xxx) and the strictness group (015).
    private static List<Diagnostic> Structure(ProtocolDocument document)
    {
        return [.. document.WithCode("aetswg-012-3"), .. document.WithCode("aetswg-015-")];
    }

    private static Diagnostic One(ProtocolDocument document, string code, int line)
    {
        var structure = Structure(document);
        var found = Assert.Single(structure);
        Assert.Equal(code, found.Code?.String);
        Assert.Equal(line - 1, found.Range.Start.Line);
        return found;
    }

    private static async Task<CodeAction> Preferred(ProtocolDocument document, Diagnostic diagnostic, string title)
    {
        var actions = await document.CodeActionsAsync(diagnostic);
        var action = Assert.Single(actions, a => a.Title == title);
        Assert.True(action.IsPreferred, $"'{title}' is not the preferred fix.");
        return action;
    }

    private static async Task NoRepairOffered(ProtocolDocument document, Diagnostic diagnostic)
    {
        var titles = (await document.CodeActionsAsync(diagnostic)).Select(a => a.Title).ToList();
        Assert.All(titles, t => Assert.True(t.StartsWith("Suppress ", StringComparison.Ordinal) ||
                                            t.StartsWith("Report ", StringComparison.Ordinal) ||
                                            t.StartsWith("Open report ", StringComparison.Ordinal),
            $"Unexpected action '{t}'"));
    }

    [Fact]
    public async Task XML_01_ARegistryFileIsNotReportedAsReadByNothing()
    {
        foreach (var registry in new[] { "Gameobjectfiles.xml", "Hardpointdatafiles.xml" })
        {
            var document = await ProtocolDocument.OpenAsync(fixture, Xml(registry));
            Assert.Empty(document.WithCode("aetswg-006-0017"));
            document.Close();
        }

        var unlisted = await ProtocolDocument.OpenAsync(fixture, Xml("Unlisted_Test.xml"));
        var warning = Assert.Single(unlisted.WithCode("aetswg-006-0017"));
        Assert.Equal(1, warning.Range.Start.Line);
        Assert.Equal("No registry names 'Unlisted_Test.xml', so the game never reads it.", warning.Message);
        unlisted.Close();

        var clean = await ProtocolDocument.OpenAsync(fixture, Xml("Xml_Test.xml"));
        Assert.Empty(Structure(clean));
        Assert.Empty(clean.WithCode("aetswg-006-0017"));
        clean.Close();
    }

    [Fact]
    public async Task XML_02_AMissingDeclarationIsAnError_WithItsRepair()
    {
        var document = await OpenTestFile(Clean[(Clean.IndexOf('\n') + 1)..]);
        var missing = One(document, "aetswg-012-3001", 1);
        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
        Assert.Equal(0, missing.Range.Start.Character);
        Assert.Equal("No XML declaration: The game requires '<?xml ...?>' before the root element and drops the " +
                     "whole file", missing.Message);

        var titles = (await document.CodeActionsAsync(missing)).Select(a => a.Title).ToList();
        Assert.Equal(
        [
            "Insert the XML declaration", "Fix all repairable XML structure problems in this file",
            "Suppress aetswg-012-3001 for this line", "Suppress aetswg-012-3001 in this file",
            "Suppress aetswg-012-3001 across the project", "Report aetswg-012-3001 on GitHub",
            "Open report for aetswg-012-3001 in editor"
        ], titles);

        await document.ApplyAsync(await Preferred(document, missing, "Insert the XML declaration"));
        Assert.Equal("<?xml version=\"1.0\"?>", document.Lines[0]);
        Assert.Equal("<SpaceUnits>", document.Lines[1]);
        Assert.Empty(Structure(document));

        await document.ReplaceAsync("<!-- header -->\n" + Clean);
        var behind = One(document, "aetswg-012-3001", 1);
        Assert.Equal(0, behind.Range.Start.Character);
        await document.ApplyAsync(await Preferred(document, behind, "Move the XML declaration to the top"));
        Assert.Equal(["<?xml version=\"1.0\"?>", "<!-- header -->", "<SpaceUnits>"], document.Lines[..3]);
        Assert.Single(document.Lines, l => l.Contains("<?xml", StringComparison.Ordinal));
        Assert.Empty(document.WithCode("aetswg-012-3001"));
        Assert.Empty(document.WithCode("aetswg-015-0003"));
        document.Close();
    }

    [Fact]
    public async Task XML_03_AMismatchedEndTag_IsRenamedRemovedOrClosed()
    {
        var document = await OpenTestFile(Clean.Replace("1.0</Max_Speed>", "1.0</Max_Sped>"));
        var typo = One(document, "aetswg-012-3002", 4);
        Assert.Equal("End tag </Max_Sped> does not match <Max_Speed>: The game compares them byte for byte and " +
                     "drops the whole file", typo.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, typo, "Rename the end tag to </Max_Speed>"));
        Assert.Equal("\t\t<Max_Speed>1.0</Max_Speed>", document.Lines[3]);
        Assert.Empty(Structure(document));

        await document.ReplaceAsync(Clean.Replace("1.0</Max_Speed>", "1.0</Max_Speed></Max_Speed>"));
        var extra = One(document, "aetswg-012-3002", 4);
        Assert.Equal("End tag </Max_Speed> does not match <SpaceUnit>: The game compares them byte for byte and " +
                     "drops the whole file", extra.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, extra, "Remove the end tag"));
        Assert.Equal("\t\t<Max_Speed>1.0</Max_Speed>", document.Lines[3]);
        Assert.Empty(Structure(document));

        await document.ReplaceAsync(Clean.Replace("1.0</Max_Speed>", "1.0"));
        var open = One(document, "aetswg-012-3002", 4);
        Assert.Equal("<Max_Speed> is never closed: The game reads </SpaceUnit> where it expects </Max_Speed> and " +
                     "drops the whole file", open.Message.TrimEnd('.'));
        await document.ApplyAsync(Assert.Single(await document.CodeActionsAsync(open),
            a => a.Title == "Close <Max_Speed>"));
        Assert.Equal("\t\t<Max_Speed>1.0</Max_Speed>", document.Lines[3]);
        Assert.Empty(Structure(document));
        document.Close();
    }

    [Fact]
    public async Task XML_04_AnElementLeftOpen_IsClosedWhereTheFileShowsItEnds()
    {
        var document = await OpenTestFile("<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n" +
                                          "\t\t<Max_Speed>1.0</Max_Speed>\n\t<SpaceUnit Name=\"Scratch_B\">\n" +
                                          "\t\t<Max_Speed>2.0</Max_Speed>\n\t</SpaceUnit>\n</SpaceUnits>\n");
        var d = One(document, "aetswg-012-3002", 3);
        Assert.Equal("<SpaceUnit> is never closed: The game reads </SpaceUnits> where it expects </SpaceUnit> and " +
                     "drops the whole file", d.Message.TrimEnd('.'));
        await document.ApplyAsync(
            Assert.Single(await document.CodeActionsAsync(d), a => a.Title == "Close <SpaceUnit>"));
        Assert.Equal(["\t\t<Max_Speed>1.0</Max_Speed>", "\t</SpaceUnit>", "\t<SpaceUnit Name=\"Scratch_B\">"],
            document.Lines[3..6]);
        Assert.Empty(Structure(document));

        await document.ReplaceAsync("<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n" +
                                    "\t\t<Max_Speed>1.0</Max_Speed>\n\t</SpaceUnit>\n\t<SpaceUnit Name=\"Scratch_B\">\n" +
                                    "\t<Max_Speed>2.0</Max_Speed>\n</SpaceUnits>\n");
        var e = One(document, "aetswg-012-3002", 6);
        await document.ApplyAsync(
            Assert.Single(await document.CodeActionsAsync(e), a => a.Title == "Close <SpaceUnit>"));
        Assert.Equal(["\t<Max_Speed>2.0</Max_Speed>", "\t</SpaceUnit>", "</SpaceUnits>"], document.Lines[6..9]);
        Assert.Empty(Structure(document));

        await document.ReplaceAsync("<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n" +
                                    "\t\t<Max_Speed>1.0</Max_Speed>\n\t</SpaceUnit>\n");
        var f = One(document, "aetswg-012-3009", 2);
        Assert.Equal("<SpaceUnits> is never closed: The game drops the whole file", f.Message.TrimEnd('.'));
        await document.ApplyAsync(Assert.Single(await document.CodeActionsAsync(f),
            a => a.Title == "Close <SpaceUnits>"));
        Assert.Equal("</SpaceUnits>", document.Lines.Last(l => l.Length > 0));
        Assert.Empty(Structure(document));
        document.Close();
    }

    [Fact]
    public async Task XML_05_AnEndTagInAnotherCaseOrOutsideAnyElement()
    {
        var document = await OpenTestFile(Clean.Replace("</Max_Speed>", "</max_speed>"));
        var casing = One(document, "aetswg-012-3003", 4);
        Assert.Equal(DiagnosticSeverity.Error, casing.Severity);
        Assert.Equal("End tag </max_speed> does not match <Max_Speed>: The game compares them byte for byte and " +
                     "drops the whole file", casing.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, casing, "Rename the end tag to </Max_Speed>"));
        Assert.Empty(Structure(document));

        await document.ReplaceAsync(Clean + "</SpaceUnits>\n");
        var outside = One(document, "aetswg-012-3004", 7);
        Assert.Equal("End tag outside any element: The game drops the whole file", outside.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, outside, "Remove the end tag"));
        Assert.Equal("", document.Lines[6]);
        Assert.Empty(Structure(document));
        document.Close();
    }

    [Fact]
    public async Task XML_06_ARootWithoutChildren_NoRoot_ASecondRoot_HaveNoRepair()
    {
        var document = await OpenTestFile("<?xml version=\"1.0\"?>\n<SpaceUnits>\n</SpaceUnits>\n");
        var empty = One(document, "aetswg-012-3005", 2);
        Assert.Equal("Root element without child elements: The game drops the whole file", empty.Message.TrimEnd('.'));
        await NoRepairOffered(document, empty);

        await document.ReplaceAsync("<?xml version=\"1.0\"?>\n");
        var none = Assert.Single(Structure(document));
        Assert.Equal("aetswg-012-3005", none.Code?.String);
        Assert.Equal("No root element: The game drops the whole file", none.Message.TrimEnd('.'));

        await document.ReplaceAsync(Clean + Clean[(Clean.IndexOf('\n') + 1)..].Replace("Scratch_A", "Scratch_B")
            .Replace("1.0", "2.0"));
        var second = One(document, "aetswg-012-3006", 7);
        Assert.Equal("Second root element: The game reads one root per file and drops the whole file",
            second.Message.TrimEnd('.'));
        await NoRepairOffered(document, second);
        document.Close();
    }

    [Fact]
    public async Task XML_07_SingleQuotesAreRepaired_AMissingEqualsIsNot()
    {
        var document = await OpenTestFile(Clean.Replace("Name=\"Scratch_A\"", "Name='Scratch_A'"));
        var quotes = One(document, "aetswg-012-3007", 3);
        Assert.Equal("Attribute Name on <SpaceUnit> uses single quotes: The game requires double quotes and drops " +
                     "the whole file", quotes.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, quotes, "Use double quotes"));
        Assert.Equal("\t<SpaceUnit Name=\"Scratch_A\">", document.Lines[2]);
        Assert.Empty(Structure(document));

        await document.ReplaceLineAsync(3, "\t<SpaceUnit Name='Scratch_\"A'>");
        var inner = One(document, "aetswg-012-3007", 3);
        Assert.DoesNotContain(await document.CodeActionsAsync(inner), a => a.Title == "Use double quotes");

        await document.ReplaceLineAsync(3, "\t<SpaceUnit Name>");
        var noEquals = One(document, "aetswg-012-3007", 3);
        Assert.Equal("Attribute Name on <SpaceUnit> has no '=': The game drops the whole file",
            noEquals.Message.TrimEnd('.'));
        await NoRepairOffered(document, noEquals);
        document.Close();
    }

    [Fact]
    public async Task XML_08_DoubleHyphensAreSeparated_AnUnclosedCommentIsNotRepaired()
    {
        var lines = Clean.Split('\n').ToList();
        lines.Insert(2, "\t<!-- a -- b -->");
        var document = await OpenTestFile(string.Join("\n", lines));
        var hyphens = One(document, "aetswg-012-3008", 3);
        Assert.Equal("'--' inside a comment: The game drops the whole file", hyphens.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, hyphens, "Separate the hyphens"));
        Assert.Equal("\t<!-- a - - b -->", document.Lines[2]);
        Assert.Empty(Structure(document));

        await document.ReplaceLineAsync(3, "\t<!-- never closed");
        var unclosed = One(document, "aetswg-012-3008", 3);
        Assert.Equal("Comment is never closed: The game drops the whole file", unclosed.Message.TrimEnd('.'));
        await NoRepairOffered(document, unclosed);
        document.Close();
    }

    [Fact]
    public async Task XML_09_XmlTheGameReadsButStrictToolsReject_IsAWarning()
    {
        var document = await OpenTestFile(Clean.Replace("<?xml version=\"1.0\"?>", "<?xml version=\"1.0\">"));
        var declaration = One(document, "aetswg-015-0001", 1);
        Assert.Equal(DiagnosticSeverity.Warning, declaration.Severity);
        Assert.Equal("XML declaration must end in '?>': The game reads it, standard XML tools reject the file",
            declaration.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, declaration, "End the declaration with '?>'"));
        Assert.Equal("<?xml version=\"1.0\"?>", document.Lines[0]);
        Assert.Empty(Structure(document));

        await document.ReplaceLineAsync(3, "\t<SpaceUnit Name=\"Scratch_Tom&Jerry\">");
        var ampersand = One(document, "aetswg-015-0002", 3);
        Assert.Equal(35, ampersand.Range.Start.Character);
        Assert.Equal("'&' that starts no entity: The game reads it as a plain character, standard XML tools reject " +
                     "the file", ampersand.Message.TrimEnd('.'));
        await NoRepairOffered(document, ampersand);

        int before;
        lock (fixture.MessagesShown) before = fixture.MessagesShown.Count;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var edits = await fixture.Client.RequestDocumentFormatting(new DocumentFormattingParams
            {
                TextDocument = new TextDocumentIdentifier(document.Uri),
                Options = new FormattingOptions { TabSize = 4, InsertSpaces = false }
            }, cts.Token);
            Assert.True(edits is null || !edits.Any(), "Format Document changed a file with a stray '&'.");
        }

        await Task.Delay(500);
        lock (fixture.MessagesShown)
            Assert.Contains(fixture.MessagesShown.Skip(before),
                m => m.Message == "Formatting refused: Not well-formed XML at line 3, column 36");

        await document.ReplaceLineAsync(1, "<?xml version=\"1.0\">");
        One(document, "aetswg-015-0001", 1);

        await document.ReplaceAsync(Clean + "trailing\n");
        var trailing = One(document, "aetswg-015-0003", 7);
        Assert.StartsWith("Invalid XML (Data at the root level is invalid", trailing.Message, StringComparison.Ordinal);
        await NoRepairOffered(document, trailing);
        document.Close();
    }

    [Fact]
    public async Task XML_10_TextAfterAChildElement_IsRemovedByItsRepair()
    {
        var document = await OpenTestFile(Clean.Replace("1.0</Max_Speed>", "1.0</Max_Speed>``"));
        var text = One(document, "aetswg-015-0004", 4);
        Assert.Equal("Text after a child element: The game skips it; values must come before the first child",
            text.Message.TrimEnd('.'));
        await document.ApplyAsync(await Preferred(document, text, "Remove the text the game skips"));
        Assert.Equal("\t\t<Max_Speed>1.0</Max_Speed>", document.Lines[3]);
        Assert.Empty(Structure(document));
        document.Close();
    }

    [Fact]
    public async Task XML_11_FixAll_RepeatsUntilNothingRepairableIsLeft()
    {
        const string original =
            "<?xml version=\"1.0\">\n<SpaceUnits>\n\t<!-- aetswg:suppress-file aetswg-015-0004 -->\n" +
            "\t<SpaceUnit Name=\"Scratch_Fix_All\">\n\t\t<Max_Speed>1.<!-- joined -->5</Max_Speed> stray\n" +
            "\t</SpaceUnit>\n\t<!-- a -- b -->\n</SpaceUnits>\n";
        var document = await OpenTestFile(original);
        var hyphens = One(document, "aetswg-012-3008", 7);
        var titles = (await document.CodeActionsAsync(hyphens)).Select(a => a.Title).ToList();
        Assert.Contains("Separate the hyphens", titles);
        var fixAll = Assert.Single(await document.CodeActionsAsync(hyphens),
            a => a.Title == "Fix all repairable XML structure problems in this file");

        await document.ApplyAsync(fixAll);
        Assert.Equal("<?xml version=\"1.0\"?>", document.Lines[0]);
        Assert.DoesNotContain("stray", document.Lines[4], StringComparison.Ordinal);
        Assert.Contains("<!-- joined -->", document.Lines[4], StringComparison.Ordinal);
        Assert.Equal("\t<!-- a - - b -->", document.Lines[6]);
        Assert.Empty(Structure(document));
        document.Close();
    }

    [Fact]
    public async Task XML_12_ACommentInsideAValue_IsReadAsTheGameReadsIt_AndOffByDefault()
    {
        var document = await OpenTestFile(Clean.Replace("1.0</Max_Speed>", "1.<!-- joined -->5</Max_Speed>"));
        Assert.Empty(document.WithCode("aetswg-015-0006"));
        Assert.DoesNotContain(document.Diagnostics, d => d.Range.Start.Line == 3);
        document.Close();
    }

    [Fact]
    public async Task XML_13_InFileDirectives_OnStructuralFindings()
    {
        string Text(string directive, string endTag = "</Max_Speed>")
        {
            return $"<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<!-- aetswg:suppress-file {directive} -->\n" +
                   $"\t<SpaceUnit Name=\"Scratch_A\">\n\t\t<Max_Speed>1.0{endTag}``\n\t</SpaceUnit>\n</SpaceUnits>\ntrailing";
        }

        var document = await OpenTestFile(Text("aetswg-006-0008"));
        Assert.Single(document.WithCode("aetswg-015-0004"), d => d.Range.Start.Line == 4);
        Assert.Single(document.WithCode("aetswg-015-0003"), d => d.Range.Start.Line == 7);
        Assert.Empty(document.WithCode("aetswg-013-"));

        await document.ReplaceAsync(Text("aetswg-015-0004"));
        Assert.Empty(document.WithCode("aetswg-015-0004"));
        Assert.Single(document.WithCode("aetswg-015-0003"));

        await document.ReplaceAsync(Text("aetswg-015-*"));
        Assert.Empty(document.WithCode("aetswg-015-"));

        await document.ReplaceAsync(Text("aetswg-015-*", "</Max_Sped>"));
        Assert.Single(document.WithCode("aetswg-012-3002"));

        await document.ReplaceAsync("<?xml version=\"1.0\">\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n" +
                                    "\t\t<Max_Speed>1.0</Max_Speed>\n\t</SpaceUnit>\n</SpaceUnits>\n");
        var declaration = One(document, "aetswg-015-0001", 1);
        var actions = await document.CodeActionsAsync(declaration);
        Assert.DoesNotContain(actions, a => a.Title == "Suppress aetswg-015-0001 for this line");
        await document.ApplyAsync(Assert.Single(actions, a => a.Title == "Suppress aetswg-015-0001 in this file"));
        Assert.Equal("<SpaceUnits>", document.Lines[1]);
        Assert.Contains("aetswg-015-0001", document.Lines[2], StringComparison.Ordinal);
        Assert.Empty(document.WithCode("aetswg-015-0001"));
        Assert.Empty(document.WithCode("aetswg-012-3001"));
        document.Close();
    }
}