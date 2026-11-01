// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>
///     What a protocol test reads off the Problems view: the last list published for each file, from the
///     server's start on, and the full re-check the Re-validate Workspace command asks for.
/// </summary>
public static class ProblemsView
{
    private const string RevalidateWorkspace = "aet-eaw-edit.lsp.revalidateWorkspace";

    /// <summary>Records every publish of <paramref name="fixture" /> into <paramref name="into" />.</summary>
    public static void Record(LspServerFixture fixture, ConcurrentDictionary<string, PublishDiagnosticsParams> into)
    {
        fixture.DiagnosticsReceived += p => into[p.Uri.ToString()] = p;
    }

    /// <summary>Runs Re-validate Workspace and waits until nothing was published for a quiet spell.</summary>
    public static async Task<Dictionary<string, List<Diagnostic>>> RevalidateAsync(LspServerFixture fixture)
    {
        await ProtocolDocument.WaitForScanAsync(fixture);
        var published = new ConcurrentDictionary<string, PublishDiagnosticsParams>();
        var last = DateTime.UtcNow;

        fixture.DiagnosticsReceived += OnPublished;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await fixture.Client.ExecuteCommand(new ExecuteCommandParams { Command = RevalidateWorkspace }, cts.Token);
            var deadline = DateTime.UtcNow.AddMinutes(4);
            while (DateTime.UtcNow - last < TimeSpan.FromSeconds(5) && DateTime.UtcNow < deadline)
                await Task.Delay(250, cts.Token);
        }
        finally
        {
            fixture.DiagnosticsReceived -= OnPublished;
        }

        return published.ToDictionary(p => FileName(p.Key), p => p.Value.Diagnostics.ToList(),
            StringComparer.OrdinalIgnoreCase);

        void OnPublished(PublishDiagnosticsParams p)
        {
            published[p.Uri.ToString()] = p;
            last = DateTime.UtcNow;
        }
    }

    /// <summary>The entries with one of <paramref name="codes" />, per file name.</summary>
    public static Dictionary<string, List<Diagnostic>> WithCodes(Dictionary<string, List<Diagnostic>> problems,
        params string[] codes)
    {
        return problems
            .Select(p => (p.Key, List: p.Value.Where(d => codes.Contains(d.Code?.String)).ToList()))
            .Where(p => p.List.Count > 0)
            .ToDictionary(p => p.Key, p => p.List, StringComparer.OrdinalIgnoreCase);
    }

    private static string FileName(string uri)
    {
        return Path.GetFileName(DocumentUri.GetFileSystemPath(DocumentUri.From(uri)) ?? uri);
    }
}

/// <summary>The vanilla Empire at War folder, with every publish from the server's start on recorded.</summary>
public sealed class EawProblemsFixture : ProtocolWorkspaceFixture
{
    public EawProblemsFixture()
    {
        ProblemsView.Record(this, Published);
    }

    public ConcurrentDictionary<string, PublishDiagnosticsParams> Published { get; } = new();
    protected override string? Workspace => LspTestEnvironment.EawWorkspacePath;
}

/// <summary>PRV-36's particle files, written by the setup script.</summary>
public sealed class ParticleEditorFixture : ProtocolWorkspaceFixture
{
    protected override string Workspace => @"C:\temp\aet-prv-pe";
    protected override string SetupTest => "PRV-36";
}

/// <summary>A unit on a game model whose bones include one named with a dot (<c>Spot01.Target</c>).</summary>
public sealed class DottedBoneFixture : ProtocolWorkspaceFixture
{
    protected override string Workspace => @"C:\temp\aet-test-XML-28";
    protected override string SetupTest => "XML-28";
}

/// <summary>The 18 particle files whose emitters carry property 0x06, the same in both games.</summary>
file static class Property06
{
    public static readonly string[] Files =
    [
        "P_atst_footpuff", "P_desert_ground_puff", "P_desert_ground_puff_big", "P_electricalrow00", "P_fire_big01",
        "P_hp_stardestroyer_damage", "P_plex_missile01", "P_rb_station_damage", "P_sandstorm00", "P_sensornode",
        "P_shield_large01", "P_shield_med01", "P_shield_small01", "P_smoke_big00", "P_smoke_med00",
        "P_smoke_small00", "P_smokedeath00", "P_snowwind"
    ];

    public static string[] Sorted(IEnumerable<string> names)
    {
        return [.. names.Select(n => Path.GetFileNameWithoutExtension(n).ToLowerInvariant()).Order()];
    }
}

/// <summary>PRV-13, PRV-28 and PRV-29 on vanilla Empire at War.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class EawPreviewProtocolTest(EawProblemsFixture fixture) : IClassFixture<EawProblemsFixture>
{
    private string Xml(string relative)
    {
        return ProtocolDocument.Find(ProjectProtocol.Required(fixture.Root!), Path.Combine("Data", "XML", relative));
    }

    [Fact]
    public async Task PRV_13_EditorDataTheGameReadsPast_IsInformationOnReValidate()
    {
        await ProtocolDocument.WaitForScanAsync(fixture);
        Assert.DoesNotContain(fixture.Published,
            p => p.Key.EndsWith(".alo", StringComparison.OrdinalIgnoreCase) && p.Value.Diagnostics.Any());

        var problems = await ProblemsView.RevalidateAsync(fixture);
        var readPast = ProblemsView.WithCodes(problems, "aetswg-014-0023");
        var smoke = Assert.Single(readPast["P_smoke_med00.alo"]);
        Assert.Equal("Emitter 0 - property 0x06, which the game reads past", smoke.Message);
        Assert.Equal(DiagnosticSeverity.Information, smoke.Severity);
        Assert.Equal(0, smoke.Range.Start.Line);

        Assert.Equal(Property06.Sorted(Property06.Files), Property06.Sorted(readPast.Keys));
        Assert.Equal(31, readPast.Values.Sum(l => l.Count));
        Assert.All(readPast.Values.SelectMany(l => l), d => Assert.Equal(DiagnosticSeverity.Information, d.Severity));
        Assert.Empty(ProblemsView.WithCodes(problems, "aetswg-014-0022"));
    }

    [Fact]
    public async Task PRV_28_EachDynamicEnumValue_CarriesAReferencesLens()
    {
        var document = await ProtocolDocument.OpenAsync(fixture, Xml(Path.Combine("Enum", "Movementclasstype.xml")));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var lenses = await fixture.Client.RequestCodeLens(
            new CodeLensParams { TextDocument = new TextDocumentIdentifier(document.Uri) }, cts.Token);
        var titles = new Dictionary<int, string>();
        foreach (var lens in lenses ?? [])
        {
            var resolved = lens.Command is null ? await fixture.Client.ResolveCodeLens(lens, cts.Token) : lens;
            if (resolved.Command?.Title is { } title && Regex.IsMatch(title, @"^\d+ references?$"))
                titles[resolved.Range.Start.Line] = title;
        }

        Assert.True(titles.ContainsKey(3) && titles.ContainsKey(16), string.Join("; ", titles));
        var infantry = int.Parse(titles[3].Split(' ')[0]);
        Assert.True(infantry > 0, titles[3]);
        Assert.Equal(infantry == 1 ? "1 reference" : $"{infantry} references", titles[3]);
        document.Close();
    }

    [Fact]
    public async Task PRV_29_ADeathAnimTypeIsLookedUpByTheEnginesFileName()
    {
        var document = await ProtocolDocument.OpenAsync(fixture, Xml("Groundinfantry.xml"));
        const int line = 485;
        Assert.Contains("<Specific_Death_Anim_Type>Crushed</Specific_Death_Anim_Type>", document.Lines[line]);
        var original = document.Text;

        await document.ReplaceLineAsync(line + 1, document.Lines[line].Replace("Crushed", "Choke_Die"));
        Assert.Contains(">Choke_Die<", document.Lines[line]);
        Assert.DoesNotContain(document.WithCode("aetswg-007-0030"), d => d.Range.Start.Line == line);

        await document.ReplaceLineAsync(line + 1, document.Lines[line].Replace("Choke_Die", "Choke"));
        Assert.Contains(">Choke<", document.Lines[line]);
        Assert.True(document.Diagnostics.Any(d => d.Code?.String == "aetswg-007-0030" && d.Range.Start.Line == line),
            ProtocolDocument.Near(document, line));
        var choke = Assert.Single(document.WithCode("aetswg-007-0030"), d => d.Range.Start.Line == line);
        Assert.Equal(DiagnosticSeverity.Warning, choke.Severity);
        // The clips come from Stormtrooper's Land_Model_Anim_Override_Name, named as that tag writes it.
        Assert.Equal("'EI_TROOPER.ALo' has no Choke animation, so 'Stormtrooper_Crush_Death_Clone' plays no death " +
                     "animation.", choke.Message);

        await document.ReplaceAsync(original);
        Assert.DoesNotContain(document.WithCode("aetswg-007-0030"), d => d.Range.Start.Line == line);
        document.Close();
    }
}

/// <summary>XML-26 and PRV-31 on vanilla Forces of Corruption.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class FocPreviewProtocolTest(FocVanillaFixture fixture) : IClassFixture<FocVanillaFixture>
{
    [Fact]
    public async Task XML_26_AValueEndingInANonBreakingSpace_IsWarnedAndFixed()
    {
        var path = ProtocolDocument.Find(ProjectProtocol.Required(fixture.Root!),
            Path.Combine("Data", "XML", "Groundstructures.xml"));
        var document = await ProtocolDocument.OpenAsync(fixture, path);
        const int line = 3204;
        Assert.Contains("EB_CIN_Basewall-mid_D.alo\u00A0</Land_Model_Name>", document.Lines[line]);

        var warning = Assert.Single(document.WithCode("aetswg-015-0005"), d => d.Range.Start.Line == line);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("Value ends in U+00A0: The game trims only space, tab, CR and LF, so the character is part of " +
                     "the value it reads", warning.Message);
        Assert.Equal(1, warning.Range.End.Character - warning.Range.Start.Character);
        Assert.Equal('\u00A0', document.Lines[line][warning.Range.Start.Character]);

        var fix = Assert.Single(await document.CodeActionsAsync(warning),
            a => a.Title == "Remove U+00A0 from the end of the value");
        Assert.True(fix.IsPreferred, "The fix is not the preferred one.");
        await document.ApplyAsync(fix);
        Assert.Contains("EB_CIN_Basewall-mid_D.alo</Land_Model_Name>", document.Lines[line]);
        Assert.DoesNotContain(document.WithCode("aetswg-015-0005"), d => d.Range.Start.Line == line);
        document.Close();
    }

    [Fact]
    public async Task PRV_31_DataTheGamesReaderHasNoCaseFor_IsAWarningOnReValidate()
    {
        var problems = await ProblemsView.RevalidateAsync(fixture);
        var skipped = ProblemsView.WithCodes(problems, "aetswg-014-0022");
        Assert.Equal([
                "Emitter 0 - property 0x49 not read by the game, skipped",
                "Emitter 1 - property 0x49 not read by the game, skipped"
            ],
            skipped["P_tactical_corrupt.alo"].Select(d => d.Message).Order());
        Assert.Equal("Emitter 0 - property 0x49 not read by the game, skipped",
            Assert.Single(skipped["P_icon_corrupt.alo"]).Message);
        Assert.All(skipped.Values.SelectMany(l => l), d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Equal(["p_icon_corrupt", "p_tactical_corrupt"], Property06.Sorted(skipped.Keys));

        var readPast = ProblemsView.WithCodes(problems, "aetswg-014-0023");
        Assert.Equal(Property06.Sorted(Property06.Files), Property06.Sorted(readPast.Keys));
        Assert.All(readPast.Values.SelectMany(l => l), d => Assert.Equal(DiagnosticSeverity.Information, d.Severity));
    }
}

/// <summary>PRV-36's Problems view half on the setup script's particle files.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class ParticleEditorProtocolTest(ParticleEditorFixture fixture) : IClassFixture<ParticleEditorFixture>
{
    [Fact]
    public async Task PRV_36_TheLinkGroupIsReadPast_AnUnknownChunkSkipped_AForeignListChunkRefused()
    {
        var problems = await ProblemsView.RevalidateAsync(fixture);

        var linked = problems["P_prv_linked.alo"];
        var note = Assert.Single(linked, d => d.Code?.String == "aetswg-014-0023");
        Assert.Equal("Emitter 0 - link group 3, Particle Editor data the game does not read", note.Message);
        Assert.Equal(DiagnosticSeverity.Information, note.Severity);
        var skipped = Assert.Single(linked, d => d.Code?.String == "aetswg-014-0022");
        Assert.Equal("Emitter 0 - chunk 0x7F not read by the game, skipped", skipped.Message);
        Assert.Equal(DiagnosticSeverity.Warning, skipped.Severity);
        Assert.Equal(2, linked.Count);

        var broken = Assert.Single(problems["P_prv_broken.alo"]);
        Assert.Equal("aetswg-014-0004", broken.Code?.String);
        Assert.Equal(DiagnosticSeverity.Error, broken.Severity);
        Assert.Equal("Malformed particle file: Malformed ALO file: the emitter list holds unexpected chunk 0x7F.",
            broken.Message);
    }
}

/// <summary>XML-28 on a hardpoint attached to a game model's dotted bone.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class DottedBoneProtocolTest(DottedBoneFixture fixture) : IClassFixture<DottedBoneFixture>
{
    [Fact]
    public async Task XML_28_ABoneNameWithADot_ResolvesInAHardpointCheck()
    {
        var path = ProtocolDocument.Find(ProjectProtocol.Required(fixture.Root!),
            Path.Combine("Data", "XML", "Scratch_Bone_Hardpoints.xml"));
        var document = await ProtocolDocument.OpenAsync(fixture, path);
        const int line = 4;
        Assert.Contains("<Attachment_Bone>Spot01.Target</Attachment_Bone>", document.Lines[line]);
        var original = document.Text;
        Assert.DoesNotContain(document.WithCode("aetswg-007-0004"), d => d.Range.Start.Line == line);

        await document.ReplaceLineAsync(line + 1, document.Lines[line].Replace("Spot01.Target", "Spot01_Target"));
        Assert.Contains(">Spot01_Target<", document.Lines[line]);
        Assert.True(await document.UntilAsync(
            all => all.Any(d => d.Code?.String == "aetswg-007-0004" && d.Range.Start.Line == line),
            TimeSpan.FromSeconds(60)), ProtocolDocument.Near(document, line));
        var missing = Assert.Single(document.WithCode("aetswg-007-0004"), d => d.Range.Start.Line == line);
        Assert.StartsWith("<Attachment_Bone> names bone 'Spot01_Target', which does not exist on 'Ev_bike.alo', the " +
                          "model of 'Scratch_Dotted_Bone' which attaches it", missing.Message);

        await document.ReplaceAsync(original);
        Assert.DoesNotContain(document.WithCode("aetswg-007-0004"), d => d.Range.Start.Line == line);
        document.Close();
    }
}