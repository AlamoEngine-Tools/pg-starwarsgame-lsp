// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaImportAnalyzerTest
{
    private const string ScriptUri = "file:///data/scripts/story/myscript.lua";

    private static readonly string[] EmptyWorkspace = [];

    private static readonly string[] WorkspaceWithLib =
    [
        "file:///data/scripts/library/pgstatemachine.lua",
    ];

    [Fact]
    public void Analyze_NoRequireCalls_NoDiagnostics()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri, "function Foo() end", EmptyWorkspace);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RequireResolves_NoDiagnostic()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("PGStateMachine")""", WorkspaceWithLib);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RequireNotResolved_EmitsError()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("MissingLibrary")""", EmptyWorkspace);
        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("MissingLibrary", diag.Message);
    }

    [Fact]
    public void Analyze_DynamicRequire_NoDiagnostic()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            "require(someVar)", EmptyWorkspace);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RelativeRequire_NoDiagnostic()
    {
        // Relative requires (../../X) cannot be resolved statically; no false positive.
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("../../CustomFactionName")""", EmptyWorkspace);
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
        var result = LuaImportAnalyzer.Analyze(ScriptUri, code, WorkspaceWithLib);
        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void Analyze_MissingRequire_DiagnosticRangeIsNonZeroLength()
    {
        var result = LuaImportAnalyzer.Analyze(ScriptUri,
            """require("Missing")""", EmptyWorkspace);
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
        var result = LuaImportAnalyzer.Analyze(ScriptUri, code, EmptyWorkspace);
        var diag = Assert.Single(result);
        Assert.Equal(1, diag.Range.Start.Line);
    }
}
