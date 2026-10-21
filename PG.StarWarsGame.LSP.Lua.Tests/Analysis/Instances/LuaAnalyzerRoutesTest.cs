// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua.Analysis.Instances;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis.Instances;

/// <summary>
///     Every answer about a file comes from the states holding it. A script nobody requires is held by
///     its own instance alone, so its instance answers everything. A library is held by every script
///     requiring it, so where it is used - definitions, references, lenses - is the whole-project
///     analyzer's answer (filtered to those states by the handlers). A rename is always the
///     whole-project analyzer's. Nothing else - findings, hover, types - ever comes from it.
/// </summary>
public sealed class LuaAnalyzerRoutesTest
{
    private const string Script = "file:///mod/data/scripts/gameobject/interdictor.lua";
    private const string Library = "file:///mod/data/scripts/library/pgstatemachine.lua";

    private readonly ScriptedLuaAnalyzer _instances = new();
    private readonly ScriptedLuaAnalyzer _project = new();

    private LuaAnalyzerRoutes Routes()
    {
        return new LuaAnalyzerRoutes(_instances, _project, uri => uri == Library);
    }

    private static object About(string uri)
    {
        return new { textDocument = new { uri }, position = new { line = 0, character = 0 } };
    }

    [Theory]
    [InlineData("textDocument/definition")]
    [InlineData("textDocument/references")]
    [InlineData("textDocument/codeLens")]
    public async Task WhereALibraryIsUsed_IsTheWholeProjects(string method)
    {
        await Routes().RequestAsync<JToken>(method, About(Library), CancellationToken.None);

        Assert.Single(_project.Requests);
        Assert.Empty(_instances.Requests);
    }

    [Theory]
    [InlineData("textDocument/definition")]
    [InlineData("textDocument/references")]
    [InlineData("textDocument/codeLens")]
    [InlineData("textDocument/hover")]
    public async Task AScriptNobodyRequires_IsAnsweredByItsInstance(string method)
    {
        await Routes().RequestAsync<JToken>(method, About(Script), CancellationToken.None);

        Assert.Single(_instances.Requests);
        Assert.Empty(_project.Requests);
    }

    [Theory]
    [InlineData("textDocument/hover")]
    [InlineData("textDocument/completion")]
    [InlineData("textDocument/inlayHint")]
    [InlineData("textDocument/semanticTokens/full")]
    [InlineData("textDocument/signatureHelp")]
    public async Task ALibrarysTypesAndWords_AreItsInstances(string method)
    {
        await Routes().RequestAsync<JToken>(method, About(Library), CancellationToken.None);

        Assert.Single(_instances.Requests);
        Assert.Empty(_project.Requests);
    }

    [Theory]
    [InlineData("textDocument/rename")]
    [InlineData("textDocument/prepareRename")]
    public async Task ARename_IsTheWholeProjects_FromAnyFile(string method)
    {
        await Routes().RequestAsync<JToken>(method, About(Script), CancellationToken.None);

        Assert.Single(_project.Requests);
        Assert.Empty(_instances.Requests);
    }

    // A lens is resolved by the analyzer that made it: its data says which.
    [Fact]
    public async Task ALibrarysLens_IsResolvedByTheWholeProject_AScriptsByItsInstance()
    {
        _project.Answer("textDocument/codeLens",
            JArray.FromObject(new[] { new { range = new Range(0, 0, 0, 1), data = new { id = 7 } } }));
        _instances.Answer("textDocument/codeLens",
            JArray.FromObject(new[] { new { range = new Range(0, 0, 0, 1), data = new { id = 8 } } }));
        var routes = Routes();
        var libraryLens = (await routes.RequestAsync<JArray>("textDocument/codeLens", About(Library),
            CancellationToken.None))!.Single();
        var scriptLens = (await routes.RequestAsync<JArray>("textDocument/codeLens", About(Script),
            CancellationToken.None))!.Single();

        await routes.RequestAsync<JToken>("codeLens/resolve", libraryLens, CancellationToken.None);
        await routes.RequestAsync<JToken>("codeLens/resolve", scriptLens, CancellationToken.None);

        var toProject = JToken.FromObject(_project.Requests[^1].Parameters);
        Assert.Equal("codeLens/resolve", _project.Requests[^1].Method);
        Assert.Equal(7, toProject["data"]!["id"]!.Value<int>());
        Assert.Equal("codeLens/resolve", _instances.Requests[^1].Method);
        Assert.Equal(8, JToken.FromObject(_instances.Requests[^1].Parameters)["data"]!["id"]!.Value<int>());
    }

    [Fact]
    public void BothAreToldOfEveryDocument_FindingsComeFromTheInstancesAlone()
    {
        var routes = Routes();
        var published = new List<PublishDiagnosticsParams>();
        routes.DiagnosticsPublished += published.Add;

        routes.DidOpen(Script, "x = 1", 1);
        routes.DidChange(Script, "x = 2", 2);
        _project.Publish(new PublishDiagnosticsParams { Uri = DocumentUri.From(Script) });
        _instances.Publish(new PublishDiagnosticsParams { Uri = DocumentUri.From(Script) });

        Assert.Equal("x = 2", _instances.Texts[Script]);
        Assert.Equal("x = 2", _project.Texts[Script]);
        Assert.Single(published);

        routes.DidClose(Script);
        Assert.Empty(_instances.Texts);
        Assert.Empty(_project.Texts);
    }
}
