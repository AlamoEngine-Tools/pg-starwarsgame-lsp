// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Server.Preview;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>
///     The protocol's XML editor and rule tests on the scratch project: XML-17 (folding, selection, matching
///     tags, linked editing), XML-18 (Base), XML-19 (formatter), XML-20 (hardpoint mode rows) and XML-21 (the
///     preview's hardpoint problems).
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class XmlEditorProtocolTest(XmlScratchFixture fixture) : IClassFixture<XmlScratchFixture>
{
    private static readonly TimeSpan Request = TimeSpan.FromSeconds(30);

    private string Xml(string file)
    {
        return Path.Combine(fixture.Root ?? throw new Exception(
            "$XunitDynamicSkip$C:\\temp\\aet-test-xml not found; run the protocol setup script."), "Data", "XML", file);
    }

    private async Task<List<FoldingRange>> Folds(ProtocolDocument document)
    {
        using var cts = new CancellationTokenSource(Request);
        var folds = await fixture.Client.RequestFoldingRange(new FoldingRangeRequestParam
        {
            TextDocument = new TextDocumentIdentifier(document.Uri)
        }, cts.Token);
        return [.. folds ?? []];
    }

    private async Task<List<Range>> Highlights(ProtocolDocument document, Position position)
    {
        using var cts = new CancellationTokenSource(Request);
        var highlights = await fixture.Client.RequestDocumentHighlight(new DocumentHighlightParams
        {
            TextDocument = new TextDocumentIdentifier(document.Uri), Position = position
        }, cts.Token);
        return [.. (highlights ?? []).Select(h => h.Range)];
    }

    // The selection ranges from the innermost outwards, as Expand Selection steps through them.
    private async Task<List<Range>> Expansion(ProtocolDocument document, Position position)
    {
        using var cts = new CancellationTokenSource(Request);
        var ranges = await fixture.Client.RequestSelectionRange(new SelectionRangeParams
        {
            TextDocument = new TextDocumentIdentifier(document.Uri),
            Positions = new Container<Position>(position)
        }, cts.Token);
        var chain = new List<Range>();
        for (var r = ranges?.FirstOrDefault(); r is not null; r = r.Parent)
            chain.Add(r.Range);
        return chain;
    }

    private static string TextOf(ProtocolDocument document, Range range)
    {
        var lines = document.Lines;
        if (range.Start.Line == range.End.Line)
            return lines[range.Start.Line][range.Start.Character..range.End.Character];
        return string.Join("\n", [lines[range.Start.Line][range.Start.Character..],
            .. lines[(range.Start.Line + 1)..range.End.Line], lines[range.End.Line][..range.End.Character]]);
    }

    [Fact]
    public async Task XML_17_FoldingSelectionMatchingTagsAndLinkedEditing_OnAMalformedFileToo()
    {
        var document = await ProtocolDocument.OpenAsync(fixture, Xml("Xml_Test.xml"),
            "<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<!-- first line\n\t     second line -->\n" +
            "\t<SpaceUnit Name=\"Scratch_A\">\n\t\t<Max_Speed>1.0</Max_Speed>\n\t</SpaceUnit>\n</SpaceUnits>\n");

        var folds = await Folds(document);
        Assert.Contains(folds, f => f is { StartLine: 1, EndLine: 6 });
        Assert.Contains(folds, f => f is { StartLine: 2, EndLine: 3 } && f.Kind == FoldingRangeKind.Comment);
        Assert.Contains(folds, f => f is { StartLine: 4, EndLine: 5 });
        Assert.DoesNotContain(folds, f => f.StartLine == 5);

        var chain = await Expansion(document, document.At(6, "Max_Speed", 1, 2));
        Assert.True(chain.Count >= 4, $"Only {chain.Count} selection ranges.");
        Assert.Equal("Max_Speed", TextOf(document, chain[0]));
        Assert.Equal("<Max_Speed>1.0</Max_Speed>", TextOf(document, chain[1]));
        Assert.Equal((4, 6), (chain[2].Start.Line, chain[2].End.Line));
        Assert.Equal((1, 7), (chain[3].Start.Line, chain[3].End.Line));

        var pair = await Highlights(document, document.At(5, "SpaceUnit", 1, 2));
        Assert.Equal([4, 6], pair.Select(r => r.Start.Line).Order());
        Assert.Empty(await Highlights(document, document.At(6, "1.0", 1, 1)));

        await document.ReplaceAsync("<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n" +
                                    "\t<SpaceUnit Name=\"Scratch_B\">\n\t\t<Max_Speed>1.0</Max_Speed>\n\t</SpaceUnit>\n" +
                                    "</SpaceUnits>\n");
        folds = await Folds(document);
        Assert.DoesNotContain(folds, f => f.StartLine == 2);
        Assert.Contains(folds, f => f is { StartLine: 3, EndLine: 4 });
        Assert.Contains(folds, f => f is { StartLine: 1, EndLine: 5 });
        Assert.Single(document.WithCode("aetswg-012-3002"), d => d.Range.Start.Line == 2);

        Assert.Equal([2], (await Highlights(document, document.At(3, "SpaceUnit", 1, 2))).Select(r => r.Start.Line));
        Assert.Equal([3, 5],
            (await Highlights(document, document.At(4, "SpaceUnit", 1, 2))).Select(r => r.Start.Line).Order());

        using var cts = new CancellationTokenSource(Request);
        var linked = await fixture.Client.RequestLinkedEditingRange(new LinkedEditingRangeParams
        {
            TextDocument = new TextDocumentIdentifier(document.Uri), Position = document.At(4, "SpaceUnit", 1, 2)
        }, cts.Token);
        Assert.NotNull(linked);
        Assert.Equal([3, 5], linked.Ranges.Select(r => r.Start.Line).Order());
        Assert.All(linked.Ranges, r => Assert.Equal("SpaceUnit", TextOf(document, r)));
        document.Close();
    }

    [Fact]
    public async Task XML_18_Base_IsAnOrdinaryElement()
    {
        var document = await ProtocolDocument.OpenAsync(fixture, Xml("Xml_Test.xml"),
            "<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_A\">\n\t\t<Base>0.0</Base>\n" +
            "\t\t<Max_Speed>1.0</Max_Speed>\n\t</SpaceUnit>\n</SpaceUnits>\n");

        var names = await Highlights(document, document.At(4, "Base", 1, 1));
        Assert.Equal(2, names.Count);
        Assert.All(names, r => Assert.Equal("Base", TextOf(document, r)));
        var chain = await Expansion(document, document.At(4, "Base", 1, 1));
        Assert.Equal("Base", TextOf(document, chain[0]));
        Assert.Equal("<Base>0.0</Base>", TextOf(document, chain[1]));
        Assert.Empty(document.WithCode("aetswg-012-"));
        Assert.Empty(document.WithCode("aetswg-015-"));
        document.Close();
    }

    private async Task<List<TextEdit>> Format(ProtocolDocument document, Range? range = null)
    {
        using var cts = new CancellationTokenSource(Request);
        var options = new FormattingOptions { TabSize = 4, InsertSpaces = false };
        var edits = range is null
            ? await fixture.Client.RequestDocumentFormatting(new DocumentFormattingParams
            {
                TextDocument = new TextDocumentIdentifier(document.Uri), Options = options
            }, cts.Token)
            : await fixture.Client.RequestDocumentRangeFormatting(new DocumentRangeFormattingParams
            {
                TextDocument = new TextDocumentIdentifier(document.Uri), Options = options, Range = range
            }, cts.Token);
        return [.. edits ?? []];
    }

    [Fact]
    public async Task XML_19_TheFormatterReindentsOnly_AndRefusesAFileThatIsNotWellFormed()
    {
        const string messy = "<?xml version=\"1.0\"?>\n<SpaceUnits>\n<SpaceUnit Name=\"Scratch_Format\">\n" +
                             "      <Max_Speed>  1.0  </Max_Speed>\n<!-- keep -->\n" +
                             "        <Max_Speed>1.<!-- joined -->5</Max_Speed>   \n\n\n   </SpaceUnit>\n</SpaceUnits>\n";
        const string formatted = "<?xml version=\"1.0\"?>\n<SpaceUnits>\n\t<SpaceUnit Name=\"Scratch_Format\">\n" +
                                 "\t\t<Max_Speed>  1.0  </Max_Speed>\n\t\t<!-- keep -->\n" +
                                 "\t\t<Max_Speed>1.<!-- joined -->5</Max_Speed>\n\n\t</SpaceUnit>\n</SpaceUnits>\n";
        var document = await ProtocolDocument.OpenAsync(fixture, Xml("Xml_Test.xml"), messy);

        var once = ProtocolDocument.Apply(messy, await Format(document));
        Assert.Equal(formatted, once);
        await document.ReplaceAsync(once);
        Assert.Empty(await Format(document) is var second && second.Count > 0
            ? ProtocolDocument.Apply(once, second) == once ? [] : second
            : second);

        await document.ReplaceAsync(messy);
        var selection = new Range(new Position(2, 0), new Position(3, document.Lines[3].Length));
        var partly = ProtocolDocument.Apply(messy, await Format(document, selection)).Split('\n');
        Assert.Equal("\t<SpaceUnit Name=\"Scratch_Format\">", partly[2]);
        Assert.Equal("\t\t<Max_Speed>  1.0  </Max_Speed>", partly[3]);
        Assert.Equal(messy.Split('\n')[4..], partly[4..]);

        await document.ReplaceAsync(formatted.Replace("\t\t<Max_Speed>  1.0  </Max_Speed>",
            "\t\t<Max_Speed>1.0</Max_Sped>"));
        int before;
        lock (fixture.MessagesShown) before = fixture.MessagesShown.Count;
        Assert.Empty(await Format(document));
        await Task.Delay(500);
        lock (fixture.MessagesShown)
            Assert.Contains(fixture.MessagesShown.Skip(before),
                m => m.Message == "Formatting refused: Not well-formed XML at line 4, column 19");
        document.Close();
    }

    [Fact]
    public async Task XML_20_HardpointModeRows_AgainstTheAttachingUnitsAbilitySlots()
    {
        var hardpoints = await ProtocolDocument.OpenAsync(fixture, Xml("Scratch_Hardpoints.xml"));

        Diagnostic On(int line, string code)
        {
            return Assert.Single(hardpoints.WithCode(code), d => d.Range.Start.Line == line - 1);
        }

        Assert.Equal("Hardpoint 'HP_Scratch_Salvo' sets 'Full_Salvo_Weapon_Delay_Multiplier', which never applies " +
                     "here. 'Scratch_Frigate' has FULL_SALVO in slot 3 of unit abilities DEFEND, TURBO, FULL_SALVO; " +
                     "the game reads the row for the first two slots only.", On(5, "aetswg-007-0031").Message);
        var rocketOnly = On(10, "aetswg-007-0032");
        Assert.Equal(DiagnosticSeverity.Error, rocketOnly.Severity);
        Assert.Equal("Hardpoint 'HP_Scratch_Rocket_Only' never fires on 'Scratch_Frigate'. " +
                     "Fire_When_In_Normal_Attack_Mode is off, so it fires only while a ROCKET_ATTACK ability is " +
                     "active - and 'Scratch_Frigate' has none.", rocketOnly.Message);
        Assert.Equal("Hardpoint 'HP_Scratch_Rocket_Row' sets 'Fire_When_In_Rocket_Attack_Mode', which never applies " +
                     "here. 'Scratch_Frigate' has unit abilities DEFEND, TURBO, FULL_SALVO; the game reads the row " +
                     "only while it has an active ROCKET_ATTACK ability.", On(14, "aetswg-007-0031").Message);
        Assert.Equal("Hardpoint 'HP_Scratch_Dead' never fires. Fire_When_In_Rocket_Attack_Mode and " +
                     "Fire_When_In_Normal_Attack_Mode are both off, so no attack mode lets it fire.",
            On(20, "aetswg-007-0032").Message);
        Assert.Single(hardpoints.WithCode("aetswg-007-0032"), d => d.Message.Contains("HP_Scratch_Dead"));

        var units = await ProtocolDocument.OpenAsync(fixture, Xml("Scratch_Units.xml"));
        var onList = units.Diagnostics.Where(d => d.Range.Start.Line == 3 &&
                                                  d.Code?.String is "aetswg-007-0031" or "aetswg-007-0032")
            .Select(d => (d.Code!.Value.String, TextOf(units, d.Range))).OrderBy(x => x.Item2).ToList();
        Assert.Equal(
        [
            ("aetswg-007-0032", "HP_Scratch_Dead"), ("aetswg-007-0032", "HP_Scratch_Rocket_Only"),
            ("aetswg-007-0031", "HP_Scratch_Rocket_Row"), ("aetswg-007-0031", "HP_Scratch_Salvo")
        ], onList);

        // Unsaved, as the editor holds it: the open hardpoint file must follow without being touched.
        var original = units.Text;
        Assert.True(await hardpoints.RepublishedAfterAsync(
                () => units.ReplaceLineAsync(10, units.Lines[9].Replace("TURBO", "ROCKET_ATTACK")),
                TimeSpan.FromSeconds(30)),
            "The open Scratch_Hardpoints.xml was not diagnosed again after the unit file changed.");
        Assert.Empty(hardpoints.WithCode("aetswg-007-0032").Where(d => d.Range.Start.Line == 9));
        Assert.Empty(hardpoints.WithCode("aetswg-007-0031").Where(d => d.Range.Start.Line == 13));
        Assert.Contains("has FULL_SALVO in slot 3 of unit abilities DEFEND, ROCKET_ATTACK, FULL_SALVO",
            On(5, "aetswg-007-0031").Message, StringComparison.Ordinal);
        On(20, "aetswg-007-0032");

        var lines = units.Lines;
        (lines[6], lines[12]) = (lines[6].Replace("DEFEND", "FULL_SALVO"), lines[12].Replace("FULL_SALVO", "DEFEND"));
        Assert.True(await hardpoints.RepublishedAfterAsync(() => units.ReplaceAsync(string.Join("\n", lines)),
            TimeSpan.FromSeconds(30)));
        Assert.Empty(hardpoints.WithCode("aetswg-007-0031"));
        Assert.Equal([19], hardpoints.WithCode("aetswg-007-0032").Select(d => d.Range.Start.Line));

        await units.ReplaceAsync(original);
        units.Close();
        hardpoints.Close();
    }

    [Fact]
    public async Task XML_21_AHardpointWithoutAnAttachmentBone_IsTwoErrorsInThePreview()
    {
        await ProtocolDocument.WaitForScanAsync(fixture);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await fixture.Client.SendRequest(new GetPreviewSceneParams { ObjectId = "Scratch_Bone_Test" },
            cts.Token);
        var problems = result.Scene.Problems;

        var targetable = Assert.Single(problems, p => p.DiagnosticId.ToString() == "aetswg-014-0019");
        Assert.Equal("error", targetable.Severity);
        Assert.Equal("Hardpoint 'HP_Scratch_NoBone_Targetable' is targetable but names no Attachment_Bone. The game " +
                     "places it at the map origin and draws its reticle in the screen corner. It cannot be hit, so " +
                     "the unit cannot be destroyed.", targetable.Message);
        var fireBone = Assert.Single(problems, p => p.DiagnosticId.ToString() == "aetswg-014-0009");
        Assert.Equal("error", fireBone.Severity);
        Assert.Equal("Hardpoint 'HP_Scratch_NoBone_FireBone' names no Attachment_Bone. The game places it at " +
                     "Fire_Bone_A 'MuzzleA_00'; a Model_To_Attach is never attached.", fireBone.Message);
        Assert.DoesNotContain(problems, p => p.Message.Contains("screen root", StringComparison.OrdinalIgnoreCase));
    }
}
