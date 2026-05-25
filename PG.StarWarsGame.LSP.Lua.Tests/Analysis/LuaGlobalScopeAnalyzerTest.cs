// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaGlobalScopeAnalyzerTest
{
    private const string CurrentUri = "file:///scripts/story/myscript.lua";
    private const string LibUri = "file:///scripts/library/mylib.lua";
    private const string OtherUri = "file:///scripts/other/other.lua";

    private static readonly ILuaApiSchemaProvider EmptySchema =
        new LuaApiSchemaProvider([]);

    private static readonly ILuaApiSchemaProvider SchemaWithFindFirst =
        new LuaApiSchemaProvider([
            "---@param typeName string\n---@xmlref XmlObject\nfunction Find_First_Object(typeName) end\n"
        ]);

    // Builds a GameIndex containing a "lib" document that exports the given global name.
    private static GameIndex IndexWithLibGlobal(string globalName, string libUri = LibUri)
    {
        var sym = new GameSymbol(globalName, GameSymbolKind.LuaGlobal, null,
            new FileOrigin(libUri, 0, null), null);
        var libDoc = new DocumentIndex(libUri, 1, [sym], []);

        return GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents.Add(libUri, libDoc),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add(globalName, [sym])
        };
    }

    // Adds the current document to the index (needed for workspace URI resolution).
    private static GameIndex AddCurrentDoc(GameIndex index, string text = "")
    {
        var doc = new DocumentIndex(CurrentUri, 1, [], []);
        return index with { Documents = index.Documents.Add(CurrentUri, doc) };
    }

    // ── no diagnostics baseline ──────────────────────────────────────────────

    [Fact]
    public void Analyze_EmptyDocument_NoDiagnostics()
    {
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, "", GameIndex.Empty, EmptySchema);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_OnlyLocalVars_NoDiagnostics()
    {
        const string text = """
            local x = 1
            local function Helper() return x end
            """;
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, GameIndex.Empty, EmptySchema);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_StdlibIdentifiers_NoDiagnostics()
    {
        const string text = """
            for k, v in pairs(t) do
                print(k, v)
            end
            """;
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, GameIndex.Empty, EmptySchema);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_EngineGlobal_IsSkipped_NoDiagnostic()
    {
        const string text = """Find_First_Object("UNIT_REBEL")""";
        var index = AddCurrentDoc(GameIndex.Empty);
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, SchemaWithFindFirst);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_GlobalDefinedInSameFile_NoDiagnostic()
    {
        const string text = """
            function MyHelper() end
            MyHelper()
            """;
        var sym = new GameSymbol("MyHelper", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(CurrentUri, 0, null), null);
        var doc = new DocumentIndex(CurrentUri, 1, [sym], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents.Add(CurrentUri, doc),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("MyHelper", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);
        Assert.Empty(result);
    }

    // ── missing require ──────────────────────────────────────────────────────

    [Fact]
    public void Analyze_UsesGlobalFromOtherFile_NotRequired_EmitsWarning()
    {
        const string text = "MyHelper()";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);

        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("MyHelper", diag.Message);
    }

    [Fact]
    public void Analyze_UsesGlobalFromOtherFile_IsRequired_NoDiagnostic()
    {
        // require("mylib") resolves to LibUri
        const string text = """
            require("mylib")
            MyHelper()
            """;
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);

        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_MissingRequire_MessageContainsDefiningFilename()
    {
        const string text = "MyHelper()";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var diag = Assert.Single(LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema));

        Assert.Contains("mylib", diag.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_SameGlobalUsedMultipleTimes_OneWarningPerUniqueName()
    {
        const string text = """
            MyHelper()
            MyHelper()
            MyHelper()
            """;
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);

        // Only one warning for MyHelper even though called 3 times
        Assert.Single(result);
    }

    [Fact]
    public void Analyze_TwoMissingGlobalsFromDifferentFiles_TwoWarnings()
    {
        const string text = """
            HelperA()
            HelperB()
            """;
        var symA = new GameSymbol("HelperA", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var symB = new GameSymbol("HelperB", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var libDoc = new DocumentIndex(LibUri, 1, [symA], []);
        var otherDoc = new DocumentIndex(OtherUri, 1, [symB], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, libDoc)
                .Add(OtherUri, otherDoc),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("HelperA", [symA])
                .Add("HelperB", [symB])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Analyze_MissingRequire_DiagnosticRangeCoversIdentifier()
    {
        const string text = "MyHelper()";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var diag = Assert.Single(LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema));

        // "MyHelper" starts at col 0 on line 0
        Assert.Equal(0, diag.Range.Start.Line);
        Assert.Equal(0, diag.Range.Start.Character);
        Assert.Equal(0, diag.Range.End.Line);
        Assert.Equal("MyHelper".Length, diag.Range.End.Character);
    }

    // ── unused require ───────────────────────────────────────────────────────

    [Fact]
    public void Analyze_RequiredFileExportsUsedGlobal_NoHint()
    {
        const string text = """
            require("mylib")
            MyHelper()
            """;
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RequiredFileExportsNoUsedGlobals_EmitsHint()
    {
        const string text = """require("mylib")""";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);

        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Hint, diag.Severity);
        Assert.Contains("mylib", diag.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RequiredFileHasNoExports_EmitsHint()
    {
        const string text = """require("mylib")""";
        // lib exists but has no LuaGlobal symbols
        var libDoc = new DocumentIndex(LibUri, 1, [], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, libDoc)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);

        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Hint, diag.Severity);
    }

    [Fact]
    public void Analyze_UnusedRequire_MessageContainsModuleName()
    {
        const string text = """require("mylib")""";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var diag = Assert.Single(LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema));

        Assert.Contains("mylib", diag.Message);
    }

    [Fact]
    public void Analyze_UnresolvedRequire_NoUnusedHint()
    {
        // require of a file that doesn't exist in workspace → already flagged by LuaImportAnalyzer,
        // LuaGlobalScopeAnalyzer should not emit an additional unused-require hint.
        const string text = """require("nonexistent")""";
        var index = AddCurrentDoc(GameIndex.Empty);

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_DynamicRequire_NoUnusedHint()
    {
        const string text = "require(someVar)";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema);
        Assert.Empty(result);
    }
}
