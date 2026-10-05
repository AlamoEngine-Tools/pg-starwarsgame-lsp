// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaPlanAnalyzerTest
{
    private const string PlanUri = "file:///data/scripts/ai/landmode/airstrike.lua";
    private const string LibraryUri = "file:///data/scripts/library/pginterventions.lua";

    private static IReadOnlyList<Diagnostic> Analyze(string text, string uri = PlanUri)
    {
        var tree = LuaSyntaxTree.ParseText(text, new LuaParseOptions(LuaSyntaxOptions.Lua51));
        return LuaPlanAnalyzer.Analyze(uri, tree);
    }

    [Fact]
    public void Analyze_EveryForceHasItsThreadFunction_NoDiagnostic()
    {
        const string text = """
                            function Definitions()
                                TaskForce = {
                                { "MainForce", "Air = 1, 5" },
                                { "ReserveForce", "Infantry = 2" }
                                }
                            end
                            function MainForce_Thread() end
                            function ReserveForce_Thread() end
                            """;
        Assert.Empty(Analyze(text));
    }

    [Fact]
    public void Analyze_ForceWithoutThreadFunction_WarnsAtTheName()
    {
        const string text = """
                            function Definitions()
                                TaskForce = {
                                { "MainForce", "Air = 1, 5" }
                                }
                            end
                            """;
        var diagnostic = Assert.Single(Analyze(text));
        Assert.Equal(DiagnosticIds.LuaPlanThreadFunctionMissing.ToString(), diagnostic.Code?.String);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("MainForce_Thread", diagnostic.Message);
        Assert.Equal(2, diagnostic.Range.Start.Line);
        Assert.Equal("    { \"".Length, diagnostic.Range.Start.Character);
        Assert.Equal("    { \"MainForce".Length, diagnostic.Range.End.Character);
    }

    [Fact]
    public void Analyze_ThreadFunctionDeclaredAsLocal_StillMissing()
    {
        // The engine looks the function up as a global of the script state.
        const string text = """
                            TaskForce = { { "MainForce" } }
                            local function MainForce_Thread() end
                            """;
        Assert.Single(Analyze(text));
    }

    [Fact]
    public void Analyze_LibraryFile_IsNeverJudged()
    {
        // A library's Definitions runs inside the plan that requires it, and the thread function
        // lives in that plan. The library alone cannot be judged.
        const string text = """
                            function Definitions()
                                TaskForce = { { "Intervention", "TaskForceRequired" } }
                            end
                            """;
        Assert.Empty(Analyze(text, LibraryUri));
    }

    [Fact]
    public void Analyze_NoTaskForceTable_NoDiagnostic()
    {
        Assert.Empty(Analyze("function MainForce_Thread() end"));
    }
}
