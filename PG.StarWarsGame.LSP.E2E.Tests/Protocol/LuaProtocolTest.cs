// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>
///     The protocol's Lua tests on the vanilla Empire at War folder with the real analyzer: LUA-19 to LUA-24.
///     The analyzer's findings arrive after this server's own, so a test waits for the one it expects.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class LuaProtocolTest(EawLuaFixture fixture) : IClassFixture<EawLuaFixture>
{
    private static readonly TimeSpan Analyzer = TimeSpan.FromSeconds(90);

    private string Eaw(string relative)
    {
        var root = fixture.Root ?? throw new Exception("$XunitDynamicSkip$eaw/ not found.");
        return ProtocolDocument.Find(root, relative);
    }

    private static void RequireAnalyzer()
    {
        if (ProtocolWorkspaceFixture.Analyzer is null)
            throw new Exception(
                "$XunitDynamicSkip$No emmylua_ls: set AET_EMMYLUA_LS, or stage the bundled executables.");
    }

    private Task<ProtocolDocument> OpenLua(string relative, string? text = null)
    {
        return ProtocolDocument.OpenAsync(fixture, Eaw(relative), text, "lua");
    }

    private static Func<IReadOnlyList<Diagnostic>, bool> Has(string code, int line)
    {
        return ds => ds.Any(d => d.Code?.String == code && d.Range.Start.Line == line - 1);
    }

    private static Diagnostic On(ProtocolDocument document, string code, int line)
    {
        return Assert.Single(document.WithCode(code), d => d.Range.Start.Line == line - 1);
    }

    [Fact]
    public async Task LUA_19_ReusedScriptState_HostGlobalAtDefinitionTime_AndAGlobalCarriedOver()
    {
        var raid = await OpenLua(@"Data\Scripts\Ai\Raidplan.lua");
        Assert.True(await raid.UntilAsync(ds => Has("aetswg-011-0014", 60)(ds) && Has("aetswg-011-0014", 61)(ds),
            Analyzer), "No aetswg-011-0014 on lines 60 and 61 of Raidplan.lua.");
        foreach (var line in new[] { 60, 61 })
            Assert.Equal("'PlayerObject' is read in Definitions: The game sets it only after Base_Definitions " +
                         "returns, so it is nil here.", On(raid, "aetswg-011-0014", line).Message);
        raid.Close();

        var pooled = await OpenLua(@"Data\Scripts\Gameobject\AetTest_Pooled.lua");
        Assert.True(await pooled.UntilAsync(ds => Has("aetswg-011-0014", 9)(ds) && Has("aetswg-011-0015", 14)(ds),
            Analyzer), "AetTest_Pooled.lua: " + string.Join("; ", pooled.Diagnostics.Select(d => d.Code?.String)));
        Assert.Equal("'Object' is read in 'Aet_Setup', which runs at definition time: The game sets it only after " +
                     "Base_Definitions returns, so it is nil here.", On(pooled, "aetswg-011-0014", 9).Message);
        var carried = On(pooled, "aetswg-011-0015", 14);
        Assert.Equal(DiagnosticSeverity.Warning, carried.Severity);
        Assert.Equal("'aet_hits' is set at run time and read before it is set, and Definitions does not set it " +
                     "again: The game reuses this script's state, so its next instance reads the value left here.",
            carried.Message);

        var original = pooled.Text;
        var lines = pooled.Lines.ToList();
        lines.InsertRange(14, ["    elseif message == OnUpdate then", "        aet_hits = 0"]);
        await pooled.ReplaceAsync(string.Join("\n", lines));
        Assert.Empty(pooled.WithCode("aetswg-011-0015"));

        await pooled.ReplaceAsync("ScriptPoolCount = 0\n" + original);
        Assert.Empty(pooled.WithCode("aetswg-011-0015"));
        Assert.Single(pooled.WithCode("aetswg-011-0014"));
        pooled.Close();
    }

    [Fact]
    public async Task LUA_20_ReusedScriptState_WithoutFlushG_AndWithoutGetEventReset()
    {
        var script = await OpenLua(@"Data\Scripts\Miscellaneous\AetTest_NoFlush.lua");
        Assert.True(await script.UntilAsync(ds => Has("aetswg-011-0016", 1)(ds) && Has("aetswg-011-0017", 1)(ds),
            Analyzer), "AetTest_NoFlush.lua: " + string.Join("; ", script.Diagnostics.Select(d => d.Code?.String)));
        var flush = On(script, "aetswg-011-0016", 1);
        Assert.Equal(0, flush.Range.Start.Character);
        Assert.Equal("This script's state is reused for its next instance, and nothing in it defines Flush_G: Every " +
                     "global, tables included, carries over.", flush.Message);
        Assert.Equal("This script's state is reused for its next instance, and nothing that runs on release calls " +
                     "GetEvent.Reset(): Task force events left in the queue reach the next instance.",
            On(script, "aetswg-011-0017", 1).Message);
        Assert.Empty(script.WithCode("aetswg-011-0015"));

        var original = script.Text;
        await script.ReplaceAsync("require(\"PGBaseDefinitions\")\n" + original);
        Assert.Empty(script.WithCode("aetswg-011-0016"));
        Assert.Empty(script.WithCode("aetswg-011-0017"));

        await script.ReplaceAsync("ScriptPoolCount = 0\n" + original);
        Assert.Empty(script.WithCode("aetswg-011-0016"));
        Assert.Empty(script.WithCode("aetswg-011-0017"));
        script.Close();
    }

    [Fact]
    public async Task LUA_21_Lua51OnlySyntax_IsAnError()
    {
        var script = await OpenLua(@"Data\Scripts\Miscellaneous\AetTest_Lua51.lua");
        Assert.True(await script.UntilAsync(ds => ds.Count(d => d.Code?.String == "aetswg-012-3011") >= 5, Analyzer),
            "AetTest_Lua51.lua: " + string.Join("; ",
                script.Diagnostics.Select(d => $"{d.Code?.String}@{d.Range.Start.Line + 1}")));
        var errors = script.WithCode("aetswg-012-3011").OrderBy(d => d.Range.Start.Line).ToList();
        Assert.Equal(5, errors.Count);
        Assert.All(errors, e => Assert.Equal(DiagnosticSeverity.Error, e.Severity));
        Assert.Equal([4, 5, 10, 14, 18], errors.Select(e => e.Range.Start.Line + 1));
        Assert.Equal("The length operator '#' is Lua 5.1: The game runs Lua 5.0 and does not load this script; use " +
                     "table.getn for a table, string.len for a string.", errors[0].Message);
        Assert.Equal("The modulo operator '%' is Lua 5.1: The game runs Lua 5.0 and does not load this script.",
            errors[1].Message);
        Assert.Equal("'...' as a value is Lua 5.1: The game runs Lua 5.0 and does not load this script; a Lua 5.0 " +
                     "vararg function reads its extra arguments from the table 'arg'.", errors[2].Message);
        Assert.Equal((14, 16), (errors[3].Range.Start.Line + 1, errors[3].Range.End.Line + 1));
        Assert.Equal("A long string with '=' levels is Lua 5.1: The game runs Lua 5.0 and does not load this " +
                     "script; Lua 5.0 nests [[ ]] instead.", errors[3].Message);
        Assert.Equal((18, 21), (errors[4].Range.Start.Line + 1, errors[4].Range.End.Line + 1));
        Assert.Equal("A long comment with '=' levels is Lua 5.1: Lua 5.0 reads '--[=[' as a line comment, so the " +
                     "game takes the lines after the first as code; Lua 5.0 nests --[[ ]] instead.", errors[4].Message);
        script.Close();
    }

    [Fact]
    public async Task LUA_22_LibraryDocumentation_AndALibraryCallStillNeedsItsRequire()
    {
        RequireAnalyzer();
        const string notRequired = "Global 'Define_State' is defined in 'Pgstatemachine' which is not required.";
        var script = await OpenLua(@"Data\Scripts\Miscellaneous\AetTest_NoRequire.lua");
        Assert.True(await script.UntilAsync(ds => ds.Any(d => d.Range.Start.Line == 3 &&
                                                              d.Message.Contains("which is not required")), Analyzer),
            "No 'not required' warning on line 4 of AetTest_NoRequire.lua: " +
            string.Join("; ", script.Diagnostics.Select(d => d.Message)));
        var warning = Assert.Single(script.Diagnostics, d => d.Message.Contains("which is not required"));
        Assert.Equal(notRequired, warning.Message);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Null(warning.Code);

        var lines = script.Lines.ToList();
        lines.Insert(1, "require(\"PGStateMachine\")");
        await script.ReplaceAsync(string.Join("\n", lines));
        Assert.True(await script.UntilAsync(ds => !ds.Any(d => d.Message.Contains("which is not required")), Analyzer));
        script.Close();

        var interdictor = await OpenLua(@"Data\Scripts\Gameobject\Interdictor.lua");
        var hover = await interdictor.HoverAsync(interdictor.At(47, "Define_State", 1, 3));
        foreach (var part in new[]
                 {
                     "function Define_State(state, function_value)",
                     "Defines a new state in the state machine table. The first state defined becomes the initial " +
                     "state. Defining an existing state again replaces its function and keeps its position in the " +
                     "order.",
                     "state string - This is the tag used to identify the state.",
                     "function_value function - The function that will process all messages associated with the given state"
                 })
            Assert.True(hover.Contains(part, StringComparison.Ordinal), $"Hover lacks '{part}':\n{hover}");
        interdictor.Close();

        // Last, so the hover's outcome is recorded whatever the libraries show.
        var onDefinitions = new List<string>();
        foreach (var library in new[] { "Pgbasedefinitions.lua", "Pgstatemachine.lua", "Pgaicommands.lua" })
        {
            var file = await OpenLua(@"Data\Scripts\Library\" + library);
            await Task.Delay(TimeSpan.FromSeconds(5));
            await file.UntilAsync(_ => true, TimeSpan.FromSeconds(1));
            onDefinitions.AddRange(file.Diagnostics
                .Where(d => d.Message.StartsWith("Global 'Definitions' is defined in", StringComparison.Ordinal))
                .Select(d => $"{library}:{d.Range.Start.Line + 1}: {d.Message}"));
            file.Close();
        }

        Assert.Empty(onDefinitions);
    }

    [Fact]
    public async Task LUA_23_LuaScript_IsCheckedAgainstTheScriptTheGameLoads()
    {
        var units = await ProtocolDocument.OpenAsync(fixture, Eaw(@"Data\Xml\Groundinfantry.xml"));
        Assert.Equal("\t\t<Lua_Script>SpeederBike</Lua_Script>", units.Lines[12]);
        var original = units.Text;

        List<Diagnostic> OnLine13()
        {
            return
            [
                .. units.Diagnostics.Where(d => d.Range.Start.Line == 12 &&
                                                d.Code?.String?.StartsWith("aetswg-004-00", StringComparison.Ordinal) ==
                                                true)
            ];
        }

        Assert.Empty(OnLine13());

        async Task<Diagnostic> With(string value, string code)
        {
            await units.ReplaceLineAsync(13, $"\t\t<Lua_Script>{value}</Lua_Script>");
            return Assert.Single(OnLine13(), d => d.Code?.String == code);
        }

        var missing = await With("AetNoSuchScript", "aetswg-004-0016");
        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
        Assert.Equal("Lua script 'AetNoSuchScript' was not found: No 'AetNoSuchScript.lua' or 'AetNoSuchScript.lc' " +
                     "in any script directory. The object runs without a script.", missing.Message);
        Assert.Equal(
            "Lua script 'GameObject\\SpeederBike' contains a backslash. The game reads the name inside a Lua " +
            "string, where a backslash is an escape, so the script never loads. '/' separates directories.",
            (await With("GameObject\\SpeederBike", "aetswg-004-0017")).Message);
        Assert.Equal("Lua script '../GameObject/SpeederBike' climbs out of the script directory with '..'. That " +
                     "resolves for a loose file on Windows only, never inside a MEG archive.",
            (await With("../GameObject/SpeederBike", "aetswg-004-0018")).Message);
        Assert.Equal("Lua script 'SpeederBike.lua' carries its extension. The game appends '.lua' itself and looks " +
                     "for 'SpeederBike.lua.lua'.", (await With("SpeederBike.lua", "aetswg-004-0019")).Message);
        var library = await With("PGBase", "aetswg-004-0020");
        Assert.Equal(DiagnosticSeverity.Warning, library.Severity);
        Assert.Equal("Lua script 'PGBase' loads from 'Data/Scripts/Library/PGBase.lua', outside " +
                     "Data/Scripts/GameObject.", library.Message);

        await units.ReplaceAsync(original);
        units.Close();
    }

    [Fact]
    public async Task LUA_24_AWindows1252VanillaScript_IsAnalyzed()
    {
        RequireAnalyzer();
        var story = await OpenLua(@"Data\Scripts\Story\Story_rebel_acti_m01_space.lua",
            await File.ReadAllTextAsync(Eaw(@"Data\Scripts\Story\Story_rebel_acti_m01_space.lua"),
                System.Text.Encoding.Latin1));
        Assert.Equal("function Story_Handle_Esc()", story.Lines[1634].TrimEnd());

        // The outline, as Ctrl+Shift+O asks for it; the analyzer answers once it has indexed the file.
        List<string> names = [];
        for (var attempt = 0; attempt < 30 && !names.Contains("Story_Handle_Esc"); attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var symbols = await fixture.Client.RequestDocumentSymbol(new DocumentSymbolParams
            {
                TextDocument = new TextDocumentIdentifier(story.Uri)
            }, cts.Token);
            names =
            [
                .. (symbols ?? []).Select(s => s.IsDocumentSymbol ? s.DocumentSymbol!.Name : s.SymbolInformation!.Name)
            ];
            if (!names.Contains("Story_Handle_Esc")) await Task.Delay(2000);
        }

        Assert.Contains("Story_Handle_Esc", names);

        using var lensCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var lenses = await fixture.Client.RequestCodeLens(new CodeLensParams
        {
            TextDocument = new TextDocumentIdentifier(story.Uri)
        }, lensCts.Token);
        var lens = Assert.Single(lenses ?? [], l => l.Range.Start.Line == 1634);
        var resolved = await fixture.Client.ResolveCodeLens(lens, lensCts.Token);
        Assert.Matches(@"^\d+ usages?$", resolved.Command?.Title ?? "");

        var hover = await story.HoverAsync(story.At(1635, "Story_Handle_Esc", 1, 2));
        Assert.Contains("Story_Handle_Esc", hover, StringComparison.Ordinal);
        story.Close();
    }
}