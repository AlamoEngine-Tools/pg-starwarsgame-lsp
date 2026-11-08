// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Assets.Serialization;
using PG.StarWarsGame.LSP.Server.Status;
using PG.StarWarsGame.LSP.Server.Variants;
using OmniSharpWindow = OmniSharp.Extensions.LanguageServer.Protocol.Models.MessageType;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>Helpers the project protocol tests share.</summary>
internal static class ProjectProtocol
{
    public static string Required(string path)
    {
        return Directory.Exists(path) || File.Exists(path)
            ? path
            : throw new Exception($"$XunitDynamicSkip${path} not found; run the protocol setup script.");
    }

    /// <summary>The newest line of this run's server logs that contains <paramref name="text" />, or null.</summary>
    public static string? LastLogged(string text)
    {
        string? found = null;
        foreach (var log in Directory.EnumerateFiles(AppContext.BaseDirectory, "aetswg-*.log")
                     .OrderBy(File.GetLastWriteTime))
        {
            using var stream =
                new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
                if (line.Contains(text, StringComparison.Ordinal))
                    found = line;
        }

        return found;
    }

    public static async Task<ServerStatusDto> StatusAsync(LspServerFixture fixture)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        return await fixture.Client.SendRequest(new GetServerStatusParams { Extended = true }, cts.Token);
    }

    public static async Task<string> EffectiveXmlAsync(LspServerFixture fixture, string id, string type)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await fixture.Client.SendRequest(new GetEffectiveObjectParams { ObjectId = id, TypeName = type },
            cts.Token);
        Assert.True(result.Found, $"{id} ({type}) not found.");
        return result.Xml;
    }

    public static void Watched(LspServerFixture fixture, string path, FileChangeType change)
    {
        fixture.Client.DidChangeWatchedFiles(new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(new FileEvent
                { Uri = DocumentUri.FromFileSystemPath(path), Type = change })
        });
    }

    /// <summary>The "(N from MEG archives)" count of an asset catalogue log line, or -1 without one.</summary>
    public static int FromArchives(string? catalogLine)
    {
        var match = Regex.Match(catalogLine ?? "", @"\((\d+) from MEG archives\)");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }
}

/// <summary>PRJ-24, PRJ-25, XML-29 and XML-30 on the setup script's copy of the load-order mod.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class LoadOrderProtocolTest(LoadOrderCopyFixture fixture) : IClassFixture<LoadOrderCopyFixture>
{
    private string Xml(string file)
    {
        return Path.Combine(ProjectProtocol.Required(fixture.Root ?? @"C:\temp\aet-test-load-order"), "Data", "XML",
            file);
    }

    private static List<string> Unresolved(ProtocolDocument document)
    {
        return [.. document.WithCode("aetswg-001-0021").Select(d => d.Message)];
    }

    [Fact]
    public async Task PRJ_24_OnlyWhatTheGameLoadsResolves()
    {
        var appended = await ProtocolDocument.OpenAsync(fixture, Xml("E2E_LoadOrder_Appended.xml"));
        var notUsed = appended.WithCode("aetswg-010-0006");
        var xwing = Assert.Single(notUsed, d => appended.Lines[d.Range.Start.Line].Contains("Name=\"X-Wing\""));
        Assert.Equal(4, xwing.Range.Start.Line);
        Assert.Equal("'X-Wing' is not used: The first definition wins, and gameobjectfiles.xml loads the one in " +
                     "SPACEUNITSFIGHTERS.XML first.", xwing.Message);
        Assert.Single(notUsed, d => appended.Lines[d.Range.Start.Line].Contains("E2E_LoadOrder_Twice") &&
                                    appended.Lines[d.Range.Start.Line + 1].Contains(">2<"));
        Assert.Equal(2, notUsed.Count);

        var unresolved = Unresolved(appended);
        foreach (var name in new[] { "Scout_Trooper", "Executor_Super_Star_Destroyer", "Bossk" })
            Assert.Contains(unresolved, m => m.Contains($"'{name}'", StringComparison.Ordinal));
        Assert.DoesNotContain(unresolved, m => m.Contains("'X-Wing'") || m.Contains("E2E_LoadOrder_Copy_Trooper"));

        var probe = appended.Lines.Select((l, i) => (l, i))
            .First(x => x.l.Contains("X-Wing, E2E_LoadOrder_Copy_Trooper")).i + 1;
        var hover = await appended.HoverAsync(appended.At(probe, "X-Wing", 1, 2));
        Assert.False(string.IsNullOrEmpty(hover), "No hover on X-Wing in the probe.");
        Assert.DoesNotContain("E2E_LoadOrder_Appended.xml", hover, StringComparison.OrdinalIgnoreCase);
        appended.Close();

        var second = await ProtocolDocument.OpenAsync(fixture, Xml("E2E_Targeting_B.xml"));
        var merged = Assert.Single(second.WithCode("aetswg-010-0007"));
        Assert.Equal(DiagnosticSeverity.Information, merged.Severity);
        Assert.Equal("'E2E_LoadOrder_Set' is merged into the definition in E2E_Targeting_A.xml: Every definition of " +
                     "one name in targetingprioritysetfiles.xml is parsed into one set.", merged.Message);
        second.Close();

        var set = await ProjectProtocol.EffectiveXmlAsync(fixture, "E2E_LoadOrder_Set", "TargetingPrioritySet");
        Assert.Contains("<Attack_Priorities>Infantry, 1.0, Vehicle, 2.0</Attack_Priorities>", set,
            StringComparison.Ordinal);
        Assert.Contains("<Category_Exclusions>Bomber</Category_Exclusions>", set, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PRJ_25_EditingARegistry_OrCreatingOrDeletingAListedFile_ChecksTheLoadOrderAgain()
    {
        var registry = Xml("GameObjectFiles.xml");
        var original = await File.ReadAllTextAsync(registry);
        var appended = await ProtocolDocument.OpenAsync(fixture, Xml("E2E_LoadOrder_Appended.xml"));
        var copy = Xml("SpaceUnitsFighters.xml");

        bool XwingNotUsed(IReadOnlyList<Diagnostic> ds, string keptIn)
        {
            return ds.Any(d => d.Code?.String == "aetswg-010-0006" && d.Message.StartsWith("'X-Wing'") &&
                               d.Message.Contains(keptIn, StringComparison.Ordinal));
        }

        bool Unresolves(IReadOnlyList<Diagnostic> ds, string name)
        {
            return ds.Any(d => d.Code?.String == "aetswg-001-0021" && d.Message.Contains($"'{name}'"));
        }

        var within = TimeSpan.FromSeconds(90);
        Assert.True(XwingNotUsed(appended.Diagnostics, "SPACEUNITSFIGHTERS.XML"),
            "Before the edit X-Wing is not warned: " + string.Join("; ", appended.Diagnostics.Select(d => d.Message)));
        var unlisted = original.Replace("\t<File>SpaceUnitsFighters.xml</File>\r\n", "")
            .Replace("\t<File>SpaceUnitsFighters.xml</File>\n", "");
        Assert.NotEqual(original, unlisted);
        try
        {
            await File.WriteAllTextAsync(registry, unlisted);
            ProjectProtocol.Watched(fixture, registry, FileChangeType.Changed);
            Assert.True(await appended.UntilAsync(ds => !ds.Any(d => d.Code?.String == "aetswg-010-0006" &&
                                                                     d.Message.StartsWith("'X-Wing'")) &&
                                                        !Unresolves(ds, "X-Wing"), within),
                "With the fighters' file unlisted, X-Wing still shows its warning or is unresolved: " +
                string.Join("; ", appended.Diagnostics.Select(d => d.Message)));

            await File.WriteAllTextAsync(registry, original);
            ProjectProtocol.Watched(fixture, registry, FileChangeType.Changed);
            Assert.True(await appended.UntilAsync(ds => XwingNotUsed(ds, "SPACEUNITSFIGHTERS.XML"), within),
                "The warning on X-Wing did not come back after the registry was restored.");

            await File.WriteAllTextAsync(copy,
                "<?xml version=\"1.0\"?>\n<SpaceUnits><SpaceUnit Name=\"X-Wing\"/></SpaceUnits>\n");
            ProjectProtocol.Watched(fixture, copy, FileChangeType.Created);
            Assert.True(await appended.UntilAsync(ds => XwingNotUsed(ds, "SpaceUnitsFighters.xml") &&
                                                        Unresolves(ds, "A-Wing"), within),
                "With the mod's own SpaceUnitsFighters.xml: " +
                string.Join("; ", appended.Diagnostics.Select(d => d.Message)));

            File.Delete(copy);
            ProjectProtocol.Watched(fixture, copy, FileChangeType.Deleted);
            Assert.True(await appended.UntilAsync(ds => XwingNotUsed(ds, "SPACEUNITSFIGHTERS.XML") &&
                                                        !Unresolves(ds, "A-Wing"), within),
                "After the delete: " + string.Join("; ", appended.Diagnostics.Select(d => d.Message)));
        }
        finally
        {
            await File.WriteAllTextAsync(registry, original);
            File.Delete(copy);
            appended.Close();
        }
    }

    [Fact]
    public async Task XML_29_AListTagRepeatedInOneObject_KeepsBothEntries()
    {
        await ProtocolDocument.WaitForScanAsync(fixture);
        var repeat = await ProjectProtocol.EffectiveXmlAsync(fixture, "E2E_LoadOrder_Repeat", "GameObjectType");
        // A list tag that may repeat keeps each occurrence as an entry of its own, in file order.
        var xwing = repeat.IndexOf("<Company_Units>X-Wing</Company_Units>", StringComparison.Ordinal);
        var awing = repeat.IndexOf("<Company_Units>A-Wing</Company_Units>", StringComparison.Ordinal);
        Assert.True(xwing >= 0 && awing > xwing &&
                    repeat.Contains("<Max_Speed>2</Max_Speed>", StringComparison.Ordinal) &&
                    !repeat.Contains("<Max_Speed>1</Max_Speed>", StringComparison.Ordinal), repeat);
    }

    [Fact]
    public async Task XML_30_RetiredDuplicateIdsSilenceNothing_TheOverrideCommentDoesNothing()
    {
        var path = Xml("E2E_LoadOrder_Appended.xml");
        var original = await File.ReadAllTextAsync(path);
        var document = await ProtocolDocument.OpenAsync(fixture, path, original);
        var line = document.Lines.Select((l, i) => (l, i)).First(x => x.l.Contains("<SpaceUnit Name=\"X-Wing\">")).i;

        async Task<bool> WarnedWith(string comment)
        {
            var lines = document.Lines.ToList();
            lines.Insert(line, "\t" + comment);
            await document.ReplaceAsync(string.Join("\n", lines));
            var warned = document.WithCode("aetswg-010-0006").Any(d => d.Message.StartsWith("'X-Wing'"));
            await document.ReplaceAsync(original);
            return warned;
        }

        Assert.True(await WarnedWith("<!-- aetswg:suppress aetswg-010-0001, aetswg-010-0002 -->"));
        Assert.True(await WarnedWith("<!-- <Override Name=\"X-Wing\"/> -->"));
        Assert.False(await WarnedWith("<!-- aetswg:suppress aetswg-010-0006 -->"));
        Assert.Empty(document.WithCode("aetswg-010-0001"));
        Assert.Empty(document.WithCode("aetswg-010-0002"));
        document.Close();
    }
}

/// <summary>The project tests that start their own servers: PRJ-17, PRJ-26, PRJ-27, SET-29 and SET-30.</summary>
[Trait("Category", "E2E")]
[Trait("Category", "Protocol")]
public sealed class ProjectStartsProtocolTest
{
    [Fact]
    public async Task PRJ_17_PackedAssetsComeFromTheArchivesTheGameLoads()
    {
        ProtocolWorkspaceFixture.RunSetup("PRJ-17");
        const string root = @"C:\temp\aet-test-PRJ-meg";
        ProjectProtocol.Required(Path.Combine(root, "Data", "ScratchScripts.meg"));
        var units = ProtocolDocument.Find(root, @"Data\Xml\ScratchUnits.xml");
        var megaFiles = Path.Combine(root, "Data", "MegaFiles.xml");

        async Task<(bool Missing, string? Catalog)> Start()
        {
            await using var server = await AdHocWorkspaceFixture.StartAsync(root);
            var document = await ProtocolDocument.OpenAsync(server, units);
            await Task.Delay(TimeSpan.FromSeconds(2));
            var missing = document.WithCode("aetswg-004-0016").Any(d => d.Range.Start.Line == 3);
            if (missing)
                Assert.Equal("Lua script 'ScratchSwap' was not found: No 'ScratchSwap.lua' or 'ScratchSwap.lc' in " +
                             "any script directory. The object runs without a script.",
                    document.WithCode("aetswg-004-0016").Single().Message);
            return (missing, ProjectProtocol.LastLogged("Asset catalog:"));
        }

        try
        {
            var listed = await Start();
            Assert.False(listed.Missing, "ScratchSwap is unresolved with the mod's archive listed.");
            var packed = ProjectProtocol.FromArchives(listed.Catalog);
            Assert.True(packed > 0, listed.Catalog);

            await File.WriteAllTextAsync(megaFiles, (await File.ReadAllTextAsync(megaFiles))
                .Replace("ScratchScripts.meg", "NotThere.meg"));
            var unlisted = await Start();
            Assert.True(unlisted.Missing, "ScratchSwap resolves although no list names its archive.");
            Assert.Equal(0, ProjectProtocol.FromArchives(unlisted.Catalog));

            File.Delete(megaFiles);
            File.Move(Path.Combine(root, "Data", "ScratchScripts.meg"), Path.Combine(root, "Data", "Config.meg"));
            var replacing = await Start();
            Assert.False(replacing.Missing, "ScratchSwap is unresolved with the mod's Config.meg.");
            Assert.Equal(packed, ProjectProtocol.FromArchives(replacing.Catalog));
        }
        finally
        {
            ProtocolWorkspaceFixture.RunSetup("PRJ-17");
        }
    }

    private static async Task<string> ErrorShownStarting(string root)
    {
        await using var server = await AdHocWorkspaceFixture.StartAsync(ProjectProtocol.Required(root));
        for (var i = 0; i < 60; i++)
        {
            lock (server.MessagesShown)
                if (server.MessagesShown.FirstOrDefault(m => m.Type == OmniSharpWindow.Error) is { } error)
                    return error.Message;
            await Task.Delay(500);
        }

        lock (server.MessagesShown)
            return "no error shown; messages: " + string.Join(" | ", server.MessagesShown.Select(m => m.Message));
    }

    [Fact]
    public async Task SET_29_AnOldShapeProjectFile_FailsWithTheFixNamed()
    {
        ProtocolWorkspaceFixture.RunSetup("SET-29");
        var error = await ErrorShownStarting(@"C:\temp\aet-test-SET-29\OldMod");
        Assert.StartsWith("Could not load mod project '", error, StringComparison.Ordinal);
        Assert.Contains("': 'directories.text' and 'directories.textResourceType' were removed in 0.2.0. Move the " +
                        "localisation directory into a top-level 'localisation' node instead", error,
            StringComparison.Ordinal);
        Assert.EndsWith("See the extension's 'Upgrading from 0.1.x' README section for the full migration steps.",
            error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PRJ_26_AnEawProjectReferencingAFocProject_IsRefused()
    {
        ProtocolWorkspaceFixture.RunSetup("PRJ-26");
        var error = await ErrorShownStarting(@"C:\temp\aet-test-PRJ-mixed\eaw-mod");
        Assert.Contains("'eaw-mod.pgproj': It targets Empire at War and references 'foc-lib.pgproj',", error,
            StringComparison.Ordinal);
        Assert.Contains("which targets Forces of Corruption. The base game cannot load the expansion's data.", error,
            StringComparison.Ordinal);
    }

    private static int ShippedDefinitions(string game)
    {
        var file = Path.Combine(LspTestEnvironment.RepositoryRoot!, "baseline", game,
            $"aet-pg-swg-lsp-{game}-baseline.aet");
        return BaselineSerializer.Deserialize(File.ReadAllBytes(ProjectProtocol.Required(file)))!.SymbolCount;
    }

    [Fact]
    public async Task SET_30_ALocalBaselineFolder_ServesEachProjectItsOwnGamesBaseline()
    {
        foreach (var (workspace, game) in new[]
                 {
                     (LspTestEnvironment.EawWorkspacePath, "eaw"), (LspTestEnvironment.WorkspacePath, "foc")
                 })
        {
            await using var server = await AdHocWorkspaceFixture.StartAsync(
                ProjectProtocol.Required(workspace ?? $"{game}/ fixture"));
            await ProtocolDocument.WaitForScanAsync(server);
            var status = await ProjectProtocol.StatusAsync(server);
            Assert.Equal(ShippedDefinitions(game), status.Extended!.Symbols.Baseline);
            Assert.DoesNotContain(status.Warnings, w => w.Contains("baseline", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task PRJ_27_TheVanillaEawWorkspace_ResolvesNoExpansionContent()
    {
        await using var server = await AdHocWorkspaceFixture.StartAsync(
            ProjectProtocol.Required(LspTestEnvironment.EawWorkspacePath ?? "eaw/ fixture"));
        var path = ProtocolDocument.Find(server.Root!, @"Data\XML\SpaceUnitsCorvettes.xml");
        var document = await ProtocolDocument.OpenAsync(server, path);
        var line = document.Lines.Select((l, i) => (l, i)).First(x => x.l.Contains("Name=\"Corellian_Corvette\"")).i;
        var lines = document.Lines.ToList();
        lines.Insert(line + 1, "\t\t<Company_Units>Executor_Super_Star_Destroyer, Bossk</Company_Units>");
        await document.ReplaceAsync(string.Join("\n", lines));

        var unresolved = document.WithCode("aetswg-001-0021").Where(d => d.Range.Start.Line == line + 1).ToList();
        Assert.Contains(unresolved, d => d.Message.Contains("'Executor_Super_Star_Destroyer'"));
        Assert.Contains(unresolved, d => d.Message.Contains("'Bossk'"));

        lines[line + 1] = "\t\t<Company_Units>Exec</Company_Units>";
        await document.ReplaceAsync(string.Join("\n", lines));
        var items = await document.CompleteAsync(new Position(line + 1, "\t\t<Company_Units>Exec".Length));
        Assert.DoesNotContain(items, i => i.Label.StartsWith("Executor", StringComparison.OrdinalIgnoreCase));
        document.Close();

        var status = await ProjectProtocol.StatusAsync(server);
        Assert.Equal(ShippedDefinitions("eaw"), status.Extended!.Symbols.Baseline);
    }
}