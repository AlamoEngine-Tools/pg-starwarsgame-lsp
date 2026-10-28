// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The game binds an engine member by its name - <c>fett.Move_To</c> - and a rename of it breaks
///     every script calling it: F2 on one is refused, through the server with the real analyzer.
/// </summary>
/// <remarks>Local only, like <see cref="LuaAnalyzerSmokeTest" />, over the EaW workspace.</remarks>
[Trait("Category", "E2E")]
public sealed class LuaEngineMemberRenameSmokeTest
{
    [Fact]
    public async Task AnEngineMember_IsNotRenamed()
    {
        var workspace = LspTestEnvironment.EawWorkspacePath
                        ?? throw new Exception("$XunitDynamicSkip$eaw/ not found.");
        var analyzer = LuaAnalyzerSmokeTest.FindAnalyzer(Path.GetDirectoryName(workspace)!);
        if (analyzer is null)
            Assert.Skip("No emmylua_ls: set AET_EMMYLUA_LS, or run scripts/stage-bundled-executables.ps1.");

        await using var session = await RawLspSession.StartAsync(
            new { textDocument = new { rename = new { prepareSupport = true } } },
            new { workspaceRoot = workspace, schemaLocalPath = LspTestEnvironment.SchemaLocalPath },
            workspace,
            new Dictionary<string, string?> { ["AET_EMMYLUA_LS"] = analyzer });

        var path = Path.Combine(workspace, "Data", "Scripts", "Ai", "Landmode", "Bobafettflameinfantry.lua");
        var uri = new Uri(path).AbsoluteUri;
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await session.NotifyAsync("textDocument/didOpen", new
        {
            textDocument = new { uri, languageId = "lua", version = 1, text }
        });
        var line = text.Replace("\r\n", "\n").Split('\n')[185];
        var member = new { line = 185, character = line.IndexOf("Move_To", StringComparison.Ordinal) + 2 };

        // The analyzer answers once it has read the script; until then a hover is ours alone.
        await LuaAnalyzerSmokeTest.PollAsync(() => session.RequestAsync("textDocument/hover", new
        {
            textDocument = new { uri }, position = member
        }, TimeSpan.FromSeconds(10)));

        var prepared = await session.RequestAsync("textDocument/prepareRename",
            new { textDocument = new { uri }, position = member }, TimeSpan.FromSeconds(10));
        string? renamed;
        try
        {
            var edit = await session.RequestAsync("textDocument/rename",
                new { textDocument = new { uri }, position = member, newName = "Move_ToX" }, TimeSpan.FromSeconds(10));
            renamed = string.Join(", ", ((edit as JObject)?["changes"] as JObject)?.Properties()
                .Select(p => Path.GetFileName(new Uri(p.Name).LocalPath)) ?? []);
        }
        catch (InvalidOperationException refused)
        {
            renamed = "refused: " + refused.Message;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"prepare: {prepared}; rename: {renamed}");
        Assert.Null(prepared);
        Assert.StartsWith("refused: ", renamed, StringComparison.Ordinal);
    }
}