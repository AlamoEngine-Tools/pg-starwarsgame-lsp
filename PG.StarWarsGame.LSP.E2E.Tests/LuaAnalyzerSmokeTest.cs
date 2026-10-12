// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The server with the real Lua analyzer behind it: the analyzer starts after the scan, and its
///     answers reach the editor through this server (#156).
/// </summary>
/// <remarks>
///     Local only - CI fetches no analyzer for the E2E run. The binary is <c>AET_EMMYLUA_LS</c>,
///     else the copy scripts/stage-bundled-executables.ps1 puts in the extension.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class LuaAnalyzerSmokeTest
{
    private const string Script = "local function helper(a, b)\n  return a + b\nend\nlocal total = helper(1, 2)\n";

    [Fact]
    public async Task TheRealAnalyzer_AnswersThroughTheServer()
    {
        var workspace = LspTestEnvironment.E2eWorkspacePath
                        ?? throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found.");
        var analyzer = FindAnalyzer(Path.GetDirectoryName(workspace)!);
        if (analyzer is null)
            Assert.Skip("No emmylua_ls: set AET_EMMYLUA_LS, or run scripts/stage-bundled-executables.ps1.");

        await using var session = await RawLspSession.StartAsync(
            new { textDocument = new { codeLens = new { dynamicRegistration = false } } },
            new
            {
                workspaceRoot = workspace,
                schemaLocalPath = LspTestEnvironment.SchemaLocalPath,
                features = new { lua = new { analyzer = true } }
            },
            workspace,
            new Dictionary<string, string?> { ["AET_EMMYLUA_LS"] = analyzer });

        // As an editor spells it: the folder on disk is "Data"; "scripts" is declared but not created.
        var uri = new Uri(Path.Combine(workspace, "Data", "scripts", "aet_analyzer_smoke.lua")).AbsoluteUri;
        await session.NotifyAsync("textDocument/didOpen", new
        {
            textDocument = new { uri, languageId = "lua", version = 1, text = Script }
        });

        // The analyzer starts after the scan and indexes before it answers; until then, nothing.
        var hover = await PollAsync(() => session.RequestAsync("textDocument/hover", new
        {
            textDocument = new { uri }, position = new { line = 3, character = 8 }
        }, TimeSpan.FromSeconds(10)));
        Assert.Contains("local total", (string?)hover?["contents"]?["value"] ?? "");

        var lens = Assert.Single((JArray)(await session.RequestAsync("textDocument/codeLens",
            new { textDocument = new { uri } }, TimeSpan.FromSeconds(10)))!);
        var resolved = await session.RequestAsync("codeLens/resolve", lens, TimeSpan.FromSeconds(10));
        Assert.Equal("aet-eaw-edit.lsp.showReferences", (string?)resolved?["command"]?["command"]);
    }

    private static string? FindAnalyzer(string repoRoot)
    {
        var configured = Environment.GetEnvironmentVariable("AET_EMMYLUA_LS");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        var staged = Path.Combine(repoRoot, "PG.StarWarsGame.LSP.Client.VSCode", "aet-eaw-edit", "server",
            "emmylua", OperatingSystem.IsWindows() ? "emmylua_ls.exe" : "emmylua_ls");
        return File.Exists(staged) ? staged : null;
    }

    private static async Task<JToken?> PollAsync(Func<Task<JToken?>> request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (await request() is { } answer) return answer;
            await Task.Delay(500);
        }

        return null;
    }
}