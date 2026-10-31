// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A vanilla state machine's names, renamed through the server (#252): the state's name - the
///     string the machine looks it up by - and its handler, the function registered by name, each
///     with every place that names it.
/// </summary>
/// <remarks>
///     <c>Interdictor.lua</c> line 47 is <c>Define_State("State_Init", State_Init);</c>, line 51
///     <c>function State_Init(message)</c>.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class LuaStateRenameSmokeTest
{
    private const int Registration = 46;
    private const int Declaration = 50;

    [Fact]
    public async Task AStatesNameAndItsHandler_AreRenamedWhereverTheyAreNamed()
    {
        var workspace = LspTestEnvironment.EawWorkspacePath
                        ?? throw new Exception("$XunitDynamicSkip$eaw/ not found.");
        await using var session = await RawLspSession.StartAsync(
            new { textDocument = new { rename = new { prepareSupport = true } } },
            new
            {
                workspaceRoot = workspace, schemaLocalPath = LspTestEnvironment.SchemaLocalPath,
                baselineLocalPath = LspTestEnvironment.BaselineLocalPath
            },
            workspace,
            new Dictionary<string, string?> { ["AET_EMMYLUA_LS"] = null });
        var path = Path.Combine(workspace, "Data", "Scripts", "Gameobject", "Interdictor.lua");
        var uri = new Uri(path).AbsoluteUri;
        await session.NotifyAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri, languageId = "lua", version = 1,
                text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)
            }
        });

        // The handler, from its declaration: the registration's argument moves with it, the name does not.
        Assert.Equal([(Registration, 28), (Declaration, 9)],
            await EditsAsync(session, uri, Declaration, 12, "State_Start"));

        // The handler, from the registration's argument.
        Assert.NotNull(await session.RequestAsync("textDocument/prepareRename",
            new { textDocument = new { uri }, position = new { line = Registration, character = 31 } },
            TimeSpan.FromSeconds(10)));
        Assert.Equal([(Registration, 28), (Declaration, 9)],
            await EditsAsync(session, uri, Registration, 31, "State_Start"));

        // The state's name, from the string: the string alone - the handler is the function's.
        Assert.Equal([(Registration, 15)], await EditsAsync(session, uri, Registration, 18, "State_Start"));
    }

    private static async Task<List<(int, int)>> EditsAsync(RawLspSession session, string uri, int line,
        int character, string newName)
    {
        var edit = await session.RequestAsync("textDocument/rename",
            new { textDocument = new { uri }, position = new { line, character }, newName }, TimeSpan.FromSeconds(10));
        return
        [
            .. (((edit as JObject)?["changes"] as JObject)?.Properties() ?? [])
            .Where(p => p.Name.EndsWith("Interdictor.lua", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => (JArray)p.Value)
            .Select(e => ((int)e["range"]!["start"]!["line"]!, (int)e["range"]!["start"]!["character"]!))
            .Order()
        ];
    }
}