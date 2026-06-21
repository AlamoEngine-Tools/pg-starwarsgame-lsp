// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaImportAnalyzerTest
{
    private const string ScriptUri = "file:///data/scripts/story/myscript.lua";

    private static readonly IFileHelper s_fileHelper = new FileHelper(new MockFileSystem());

    private static readonly IReadOnlyDictionary<string, DocumentIndex> EmptyWorkspace =
        new Dictionary<string, DocumentIndex>();

    private static readonly IReadOnlyDictionary<string, DocumentIndex> WorkspaceWithLib =
        MakeDocs("file:///data/scripts/library/pgstatemachine.lua");

    private static Dictionary<string, DocumentIndex> MakeDocs(params string[] uris)
    {
        var dict = new Dictionary<string, DocumentIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in uris)
            dict[uri] = new DocumentIndex(uri, 1, [], []);
        return dict;
    }

    [Fact]
    public void Analyze_NoRequireCalls_NoDiagnostics()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri, "function Foo() end", EmptyWorkspace, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RequireResolves_NoDiagnostic()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("PGStateMachine")""", WorkspaceWithLib, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RequireNotResolved_EmitsError()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("MissingLibrary")""", EmptyWorkspace, s_fileHelper);
        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("MissingLibrary", diag.Message);
    }

    [Fact]
    public void Analyze_DynamicRequire_NoDiagnostic()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            "require(someVar)", EmptyWorkspace, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RelativeRequire_NoDiagnostic()
    {
        // Relative requires (../../X) cannot be resolved statically; no false positive.
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("../../CustomFactionName")""", EmptyWorkspace, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_MultipleRequires_OnlyMissingEmitsError()
    {
        var code = """
                   require("PGStateMachine")
                   require("MissingOne")
                   require("MissingTwo")
                   """;
        var result = LuaImportAnalyzer.Analyze(ScriptUri, code, WorkspaceWithLib, s_fileHelper);
        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void Analyze_MissingRequire_DiagnosticRangeIsNonZeroLength()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("Missing")""", EmptyWorkspace, s_fileHelper);
        var diag = Assert.Single(result);
        Assert.True(diag.Range.End.Character > diag.Range.Start.Character ||
                    diag.Range.End.Line > diag.Range.Start.Line);
    }

    [Fact]
    public void Analyze_MissingRequire_DiagnosticRangeOnCorrectLine()
    {
        var code = """
                   local x = 1
                   require("Missing")
                   """;
        var result = LuaImportAnalyzer.Analyze(ScriptUri, code, EmptyWorkspace, s_fileHelper);
        var diag = Assert.Single(result);
        Assert.Equal(1, diag.Range.Start.Line);
    }
}