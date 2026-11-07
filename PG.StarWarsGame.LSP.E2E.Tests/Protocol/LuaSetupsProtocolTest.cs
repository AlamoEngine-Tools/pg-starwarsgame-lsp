// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>Helpers the Lua protocol tests share across their servers.</summary>
internal static class LuaProtocol
{
    public static readonly TimeSpan Analyzer = TimeSpan.FromSeconds(90);

    public static string Workspace(ProtocolWorkspaceFixture fixture)
    {
        return fixture.Root ??
               throw new Exception("$XunitDynamicSkip$The test's workspace is not there; run the setup script.");
    }

    public static void RequireAnalyzer()
    {
        if (ProtocolWorkspaceFixture.Analyzer is null)
            throw new Exception(
                "$XunitDynamicSkip$No emmylua_ls: set AET_EMMYLUA_LS, or stage the bundled executables.");
    }

    /// <summary>Whether the server log of this test run holds a line containing <paramref name="text" />.</summary>
    public static bool Logged(string text, DateTime since)
    {
        foreach (var log in Directory.EnumerateFiles(AppContext.BaseDirectory, "aetswg-*.log")
                     .Where(f => File.GetLastWriteTime(f) >= since.AddMinutes(-1)))
        {
            using var stream =
                new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
                if (line.Contains(text, StringComparison.Ordinal))
                    return true;
        }

        return false;
    }

    /// <summary>Replaces the quoted argument on a line with nothing and completes between the quotes.</summary>
    public static async Task<List<CompletionItem>> CompleteInEmptied(ProtocolDocument document, int line, string value)
    {
        var original = document.Text;
        var position = document.At(line, $"\"{value}\"", 1, 1);
        await document.ReplaceLineAsync(line, document.Lines[line - 1].Replace($"\"{value}\"", "\"\""));
        var items = await document.CompleteAsync(position);
        await document.ReplaceAsync(original);
        return items;
    }
}

/// <summary>LUA-25 on its schema copy, whose Find_Player still reads ---@xmlref.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class LuaXmlrefProtocolTest(EawXmlrefSchemaFixture fixture) : IClassFixture<EawXmlrefSchemaFixture>
{
    [Fact]
    public async Task LUA_25_TheRetiredXmlrefTag_IsNoLongerRead()
    {
        if (!Directory.Exists(@"C:\temp\aet-test-lua25\schema\eaw"))
            throw new Exception("$XunitDynamicSkip$The LUA-25 schema copy is not there; run the setup script.");
        var path = ProtocolDocument.Find(LuaProtocol.Workspace(fixture),
            @"Data\Scripts\Miscellaneous\AetTest_Players.lua");
        var players = await ProtocolDocument.OpenAsync(fixture, path, null, "lua");
        await Task.Delay(TimeSpan.FromSeconds(3));
        await players.UntilAsync(_ => true, TimeSpan.FromSeconds(1));

        Assert.Empty(players.WithCode("aetswg-001-0021"));
        Assert.DoesNotContain("Faction", await players.HoverAsync(players.At(6, "Empire", 1, 2)),
            StringComparison.Ordinal);
        Assert.DoesNotContain(await LuaProtocol.CompleteInEmptied(players, 6, "Empire"),
            i => i.Label == "Empire" || i.Label == "Rebel");
        players.Close();
    }
}

/// <summary>LUA-25's second half, LUA-31's counterpart: the same script on the repository's schema.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class LuaAetrefProtocolTest(EawLuaFixture fixture) : IClassFixture<EawLuaFixture>
{
    [Fact]
    public async Task LUA_25_TheAetrefTag_IsRead()
    {
        var path = ProtocolDocument.Find(LuaProtocol.Workspace(fixture),
            @"Data\Scripts\Miscellaneous\AetTest_Players.lua");
        var players = await ProtocolDocument.OpenAsync(fixture, path, null, "lua");

        var unresolved = Assert.Single(players.WithCode("aetswg-001-0021"));
        Assert.Equal(4, unresolved.Range.Start.Line);
        Assert.Contains("Faction", await players.HoverAsync(players.At(6, "Empire", 1, 2)), StringComparison.Ordinal);
        Assert.Contains(await LuaProtocol.CompleteInEmptied(players, 6, "Empire"), i => i.Label == "Empire");
        players.Close();
    }
}

/// <summary>LUA-31: no analyzer anywhere - this server's own Lua support remains.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class LuaWithoutAnalyzerProtocolTest(EawWithoutAnalyzerFixture fixture)
    : IClassFixture<EawWithoutAnalyzerFixture>
{
    private readonly DateTime _started = DateTime.Now;

    [Fact]
    public async Task LUA_31_WithoutTheAnalyzer_CompletionHoverNavigationRemain()
    {
        var path = ProtocolDocument.Find(LuaProtocol.Workspace(fixture), @"Data\Scripts\Gameobject\Interdictor.lua");
        var interdictor = await ProtocolDocument.OpenAsync(fixture, path, null, "lua");

        Assert.True(LuaProtocol.Logged("Lua analyzer not started: emmylua_ls not found (set AET_EMMYLUA_LS, or place " +
                                       "it in the server's emmylua folder)", _started),
            "The server log does not say the analyzer was not started.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var lenses = await fixture.Client.RequestCodeLens(new CodeLensParams
        {
            TextDocument = new TextDocumentIdentifier(interdictor.Uri)
        }, cts.Token);
        Assert.Empty(lenses ?? []);

        Assert.Equal(string.Empty, await interdictor.HoverAsync(interdictor.At(111, "Under_Missile_Attack", 1, 2)));
        var engine = await interdictor.HoverAsync(interdictor.At(112, "FindDeadlyEnemy", 1, 2));
        Assert.Contains("FindDeadlyEnemy", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("---", engine, StringComparison.Ordinal);

        var definition = await fixture.Client.RequestDefinition(new DefinitionParams
        {
            TextDocument = new TextDocumentIdentifier(interdictor.Uri),
            Position = interdictor.At(94, "Under_Missile_Attack", 1, 2)
        }, cts.Token);
        var target = Assert.Single(definition ?? []);
        Assert.Equal(110,
            target.IsLocation ? target.Location!.Range.Start.Line : target.LocationLink!.TargetRange.Start.Line);

        var items = await LuaProtocol.CompleteInEmptied(interdictor, 72, "Marauder_Missile_Cruiser");
        Assert.Contains(items, i => i.Detail == "GameObjectType");
        interdictor.Close();
    }
}

/// <summary>LUA-32: a project whose declared scripts folder does not exist yet.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class LuaMissingScriptsFolderProtocolTest(Lua32Fixture fixture) : IClassFixture<Lua32Fixture>
{
    private readonly DateTime _started = DateTime.Now;

    [Fact]
    public async Task LUA_32_TheAnalyzerWorkspace_KeepsTheCaseOnDisk()
    {
        LuaProtocol.RequireAnalyzer();
        var root = LuaProtocol.Workspace(fixture);
        await ProtocolDocument.WaitForScanAsync(fixture);
        Assert.False(Directory.Exists(Path.Combine(root, "data")), "The data folder exists before the test makes it.");

        var script = Path.Combine(root, "data", "scripts", "gameobject", "aettest.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        const string text = "ScriptPoolCount = 0\n\nfunction Aet_Test()\n    local aet_unused = 1\nend\n";
        await File.WriteAllTextAsync(script, text);
        try
        {
            var document = await ProtocolDocument.OpenAsync(fixture, script, text, "lua");
            Assert.True(await document.UntilAsync(ds => ds.Any(d => d.Code?.String == "aetswg-016-0001" &&
                                                                    d.Range.Start.Line == 3), LuaProtocol.Analyzer),
                "No aetswg-016-0001 on aet_unused: " +
                string.Join("; ", document.Diagnostics.Select(d => d.Code?.String)));
            Assert.True(LuaProtocol.Logged(@"for C:\temp\AetTest-LUA32\data\scripts", _started),
                @"The log has no 'Lua analyzer started ... for C:\temp\AetTest-LUA32\data\scripts'.");
            document.Close();
        }
        finally
        {
            File.Delete(script);
        }
    }
}

/// <summary>LUA-33: a mod's own PGStateMachine decides whether the state machine rules apply.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class LuaOwnStateMachineProtocolTest(Lua33Fixture fixture) : IClassFixture<Lua33Fixture>
{
    private const string WithContract = "StateMachine = {}\n\nfunction Define_State(name, handler)\nend\n\n" +
                                        "function Set_Next_State(name)\n    StateMachine.next = name\nend\n\n" +
                                        "function Process_States()\n    local a, b, c = OnEnter, OnUpdate, OnExit\nend\n";

    [Fact]
    public async Task LUA_33_TheStateMachineRules_FollowTheModsOwnLibrary()
    {
        LuaProtocol.RequireAnalyzer();
        var root = LuaProtocol.Workspace(fixture);
        var machinePath = ProtocolDocument.Find(root, @"Data\Scripts\GameObject\AetMachine.lua");
        var libraryPath = ProtocolDocument.Find(root, @"Data\Scripts\Library\PGStateMachine.lua");
        var stripped = await File.ReadAllTextAsync(libraryPath);
        var machine = await ProtocolDocument.OpenAsync(fixture, machinePath, null, "lua");
        Assert.True(await machine.UntilAsync(ds =>
                ds.Any(d => d.Code?.String == "aetswg-011-0016" && d.Range.Start.Line == 0) &&
                ds.Any(d => d.Code?.String == "aetswg-011-0017" && d.Range.Start.Line == 0),
            LuaProtocol.Analyzer), "No aetswg-011-0016 and -0017 at line 1 with the stripped library.");
        Assert.Empty(machine.WithCode("aetswg-011-0008"));
        Assert.DoesNotContain("initial state", await machine.HoverAsync(machine.At(4, "\"State_Init\"", 1, 2)),
            StringComparison.Ordinal);

        var library = await ProtocolDocument.OpenAsync(fixture, libraryPath, null, "lua");
        try
        {
            await library.ReplaceAsync(WithContract);
            await library.SaveAsync();
            var original = machine.Text;
            await machine.ReplaceLineAsync(9, machine.Lines[8] + " ");
            await machine.ReplaceAsync(original);
            Assert.True(
                await machine.UntilAsync(ds => ds.Any(d => d.Code?.String == "aetswg-011-0008"), LuaProtocol.Analyzer),
                "No aetswg-011-0008 once the library has the contract: " +
                string.Join("; ", machine.Diagnostics.Select(d => $"{d.Code?.String}@{d.Range.Start.Line + 1}")));
            var unregistered = Assert.Single(machine.WithCode("aetswg-011-0008"));
            Assert.Equal(7, unregistered.Range.Start.Line);
            Assert.Equal("State 'State_Nowhere' is not registered: Set_Next_State ignores it and the state machine " +
                         "stays where it is.", unregistered.Message);
            Assert.Contains("initial state State_Init - handler State_Init",
                await machine.HoverAsync(machine.At(4, "\"State_Init\"", 1, 2)), StringComparison.Ordinal);
            Assert.Single(machine.WithCode("aetswg-011-0016"), d => d.Range.Start.Line == 0);
            Assert.Single(machine.WithCode("aetswg-011-0017"), d => d.Range.Start.Line == 0);
        }
        finally
        {
            await File.WriteAllTextAsync(libraryPath, stripped);
            library.Close();
            machine.Close();
        }
    }
}