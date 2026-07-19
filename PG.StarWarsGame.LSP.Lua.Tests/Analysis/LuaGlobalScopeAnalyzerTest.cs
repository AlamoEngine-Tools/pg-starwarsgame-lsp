// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

public sealed class LuaGlobalScopeAnalyzerTest
{
    private const string CurrentUri = "file:///scripts/story/myscript.lua";
    private const string LibUri = "file:///scripts/library/mylib.lua";
    private const string OtherUri = "file:///scripts/other/other.lua";

    private static readonly IFileHelper s_fileHelper = new FileHelper(new MockFileSystem());

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
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, "", GameIndex.Empty, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_OnlyLocalVars_NoDiagnostics()
    {
        const string text = """
                            local x = 1
                            local function Helper() return x end
                            """;
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, GameIndex.Empty, EmptySchema, s_fileHelper);
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
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, GameIndex.Empty, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_EngineGlobal_IsSkipped_NoDiagnostic()
    {
        const string text = """Find_First_Object("UNIT_REBEL")""";
        var index = AddCurrentDoc(GameIndex.Empty);
        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, SchemaWithFindFirst, s_fileHelper);
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

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    // ── missing require ──────────────────────────────────────────────────────

    [Fact]
    public void Analyze_UsesGlobalFromOtherFile_NotRequired_EmitsWarning()
    {
        const string text = "MyHelper()";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

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

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_MissingRequire_MessageContainsDefiningFilename()
    {
        const string text = "MyHelper()";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var diag = Assert.Single(LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper));

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

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        // Only one warning for MyHelper even though called 3 times
        Assert.Single(result);
    }

    [Fact]
    public void Analyze_TwoMissingGlobalsFromDifferentSharedFiles_TwoWarnings()
    {
        // Both LibUri (library by path) and OtherUri (dependency - required by a third file)
        // are shared files. Globals from both should trigger missing-require warnings.
        const string requirer = "file:///scripts/ai/plan_x.lua";
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
        // plan_x.lua requires "other" → makes OtherUri a Dependency (Tier 2)
        var requirerDoc = new DocumentIndex(requirer, 1, [], [], ImmutableArray.Create("other"));
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, libDoc)
                .Add(OtherUri, otherDoc)
                .Add(requirer, requirerDoc),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("HelperA", [symA])
                .Add("HelperB", [symB])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Analyze_MissingRequire_DiagnosticRangeCoversIdentifier()
    {
        const string text = "MyHelper()";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var diag = Assert.Single(LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper));

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

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_RequiredFileExportsNoUsedGlobals_EmitsHint()
    {
        const string text = """require("mylib")""";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

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

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Hint, diag.Severity);
    }

    [Fact]
    public void Analyze_UnusedRequire_MessageContainsModuleName()
    {
        const string text = """require("mylib")""";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var diag = Assert.Single(LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper));

        Assert.Contains("mylib", diag.Message);
    }

    [Fact]
    public void Analyze_UnresolvedRequire_NoUnusedHint()
    {
        // require of a file that doesn't exist in workspace → already flagged by LuaImportAnalyzer,
        // LuaGlobalScopeAnalyzer should not emit an additional unused-require hint.
        const string text = """require("nonexistent")""";
        var index = AddCurrentDoc(GameIndex.Empty);

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_DynamicRequire_NoUnusedHint()
    {
        const string text = "require(someVar)";
        var index = AddCurrentDoc(IndexWithLibGlobal("MyHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    // ── transitive require (Bug A) ───────────────────────────────────────────

    [Fact]
    public void Analyze_TransitiveRequire_GlobalFromTransitiveDep_NoDiagnostic()
    {
        // A requires B (in text), B.RequireArgs = ["c"] (so B transitively requires C),
        // C defines Foo → A calls Foo() → no warning.
        const string uriB = "file:///scripts/b.lua";
        const string uriC = "file:///scripts/c.lua";
        const string text = """
                            require("b")
                            Foo()
                            """;
        var symFoo = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(uriC, 0, null), null);
        var docB = new DocumentIndex(uriB, 1, [], [],
            ImmutableArray.Create("c"));
        var docC = new DocumentIndex(uriC, 1, [symFoo], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, docB)
                .Add(uriC, docC),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Foo", [symFoo])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_TransitiveRequire_GlobalFromUnreachableSharedFile_EmitsWarning()
    {
        // A does NOT require anything; OtherUri is a Dependency (required by LibUri via "other").
        // A calls Foo() which is defined in OtherUri → warning because OtherUri is shared but not required by A.
        const string text = "Foo()";
        var symFoo = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var docB = new DocumentIndex(LibUri, 1, [], [],
            ImmutableArray.Create("other")); // LibUri requires "other" → OtherUri becomes Dependency
        var docC = new DocumentIndex(OtherUri, 1, [symFoo], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, docB)
                .Add(OtherUri, docC),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Foo", [symFoo])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Warning,
            result[0].Severity);
    }

    [Fact]
    public void Analyze_TransitiveRequire_Phase4_UsesDirectRequiresOnly()
    {
        // A requires B (direct). B requires C and exports no globals used by A.
        // The unused-require hint should appear for B (A doesn't use B's exports),
        // NOT be suppressed because B transitively depends on something else.
        const string uriB = "file:///scripts/b.lua";
        const string text = """require("b")""";
        var symFoo = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(uriB, 0, null), null);
        var docB = new DocumentIndex(uriB, 1, [symFoo], [],
            ImmutableArray.Create("c")); // B exports Foo, requires C
        var docC = new DocumentIndex(OtherUri, 1, [], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, docB)
                .Add(OtherUri, docC),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Foo", [symFoo])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        // Unused-require hint for require("b") since Foo is not called
        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Hint,
            diag.Severity);
    }

    // ── cyclic require detection (Feature C) ─────────────────────────────────

    [Fact]
    public void Analyze_DirectCycle_EmitsError()
    {
        // A requires B (in text), B.RequireArgs = ["a"] → B transitively requires A back → cycle.
        const string uriA = "file:///scripts/a.lua";
        const string uriB = "file:///scripts/b.lua";
        const string text = """require("b")""";
        var docB = new DocumentIndex(uriB, 1, [], [],
            ImmutableArray.Create("a")); // B requires "a" → resolves back to uriA
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(uriA, new DocumentIndex(uriA, 1, [], []))
                .Add(uriB, docB)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(uriA, text, index, EmptySchema, s_fileHelper);

        var errors = result.Where(d =>
            d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Single(errors);
        Assert.Contains("cyclic", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_IndirectCycle_EmitsError()
    {
        // A requires B (text), B.RequireArgs = ["c"], C.RequireArgs = ["a"] → A→B→C→A cycle.
        const string uriA = "file:///scripts/a.lua";
        const string uriB = "file:///scripts/b.lua";
        const string uriC = "file:///scripts/c.lua";
        const string text = """require("b")""";
        var docB = new DocumentIndex(uriB, 1, [], [], ImmutableArray.Create("c"));
        var docC = new DocumentIndex(uriC, 1, [], [], ImmutableArray.Create("a"));
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(uriA, new DocumentIndex(uriA, 1, [], []))
                .Add(uriB, docB)
                .Add(uriC, docC)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(uriA, text, index, EmptySchema, s_fileHelper);

        var errors = result.Where(d =>
            d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Single(errors);
    }

    [Fact]
    public void Analyze_NoCycle_NoCyclicError()
    {
        const string text = """require("b")""";
        var docB = new DocumentIndex(LibUri, 1, [], [], ImmutableArray.Create("c"));
        var docC = new DocumentIndex(OtherUri, 1, [], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, docB)
                .Add(OtherUri, docC)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.DoesNotContain(result, d =>
            d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Analyze_UnresolvableRequire_NoCyclicError()
    {
        // Unresolvable require has resolvedUri = null; must not emit cyclic-require error.
        const string text = """require("nonexistent")""";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.DoesNotContain(result, d =>
            d.Severity == DiagnosticSeverity.Error);
    }

    // ── own-file / local-scope false positives (Bug B) ───────────────────────

    [Fact]
    public void Analyze_OwnFileGlobal_SameNameInOtherFile_NoWarning()
    {
        // Current file declares State_Init; another file also declares it.
        // Calling State_Init in the current file must not warn.
        const string text = """
                            function State_Init() end
                            State_Init()
                            """;
        var symCurrent = new GameSymbol("State_Init", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(CurrentUri, 0, null), null);
        var symOther = new GameSymbol("State_Init", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var otherDoc = new DocumentIndex(OtherUri, 1, [symOther], []);
        var currentDoc = new DocumentIndex(CurrentUri, 1, [symCurrent], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, currentDoc)
                .Add(OtherUri, otherDoc),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("State_Init", [symCurrent, symOther])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_OwnFileGlobal_PassedAsValue_NoWarning()
    {
        // Passing a locally-declared function as a value (e.g. Define_State("x", State_Init))
        // must not warn even when another workspace file also declares State_Init.
        const string text = """
                            function State_Init() end
                            function Definitions()
                                local _ = State_Init
                            end
                            """;
        var symCurrent = new GameSymbol("State_Init", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(CurrentUri, 0, null), null);
        var symOther = new GameSymbol("State_Init", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [symCurrent], []))
                .Add(OtherUri, new DocumentIndex(OtherUri, 1, [symOther], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("State_Init", [symCurrent, symOther])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_LocalVariable_SameNameAsOtherFileGlobal_NoWarning()
    {
        const string text = """
                            local counter = 0
                            counter = counter + 1
                            """;
        var symOther = new GameSymbol("counter", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(OtherUri, new DocumentIndex(OtherUri, 1, [symOther], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("counter", [symOther])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_LocalFunction_SameNameAsOtherFileGlobal_NoWarning()
    {
        const string text = """
                            local function Helper() end
                            Helper()
                            """;
        var symOther = new GameSymbol("Helper", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(OtherUri, new DocumentIndex(OtherUri, 1, [symOther], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("Helper", [symOther])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_FunctionParameter_SameNameAsOtherFileGlobal_NoWarning()
    {
        const string text = """
                            function Foo(counter)
                                return counter + 1
                            end
                            """;
        var symOther = new GameSymbol("counter", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(OtherUri, new DocumentIndex(OtherUri, 1, [symOther], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("counter", [symOther])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    // ── global override warning (Feature D) ─────────────────────────────────

    [Fact]
    public void Analyze_DirectlyRequiredFile_GlobalRedeclared_EmitsOverrideWarning()
    {
        const string text = """
                            require("mylib")
                            function Foo() end
                            """;
        var sym = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, new DocumentIndex(LibUri, 1, [sym], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Foo", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var overrideWarnings = result.Where(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("overrides", StringComparison.OrdinalIgnoreCase)).ToList();
        var warning = Assert.Single(overrideWarnings);
        Assert.Contains("Foo", warning.Message);
    }

    [Fact]
    public void Analyze_TransitivelyRequiredFile_GlobalRedeclared_EmitsOverrideWarning()
    {
        const string uriB = "file:///scripts/b.lua";
        const string uriC = "file:///scripts/c.lua";
        const string text = """
                            require("b")
                            function Foo() end
                            """;
        var sym = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(uriC, 0, null), null);
        var docB = new DocumentIndex(uriB, 1, [], [], ImmutableArray.Create("c"));
        var docC = new DocumentIndex(uriC, 1, [sym], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, docB)
                .Add(uriC, docC),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Foo", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var overrideWarnings = result.Where(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("overrides", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(overrideWarnings);
    }

    [Fact]
    public void Analyze_OverrideAnnotation_SuppressesOverrideWarning()
    {
        const string text = """
                            require("mylib")
                            ---@Override
                            function Foo() end
                            """;
        var sym = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, new DocumentIndex(LibUri, 1, [sym], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Foo", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.DoesNotContain(result, d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("overrides", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DeclaredGlobal_NotInRequiredFile_NoOverrideWarning()
    {
        const string text = """
                            require("mylib")
                            function Foo() end
                            """;
        var sym = new GameSymbol("Bar", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, new DocumentIndex(LibUri, 1, [sym], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Bar", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.DoesNotContain(result, d =>
            d.Message.Contains("overrides", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_OverrideWarning_RangeCoversNameToken()
    {
        // Line 0: require("mylib")
        // Line 1: function Foo() end  ← Foo starts at col 9
        const string text = """
                            require("mylib")
                            function Foo() end
                            """;
        var sym = new GameSymbol("Foo", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, new DocumentIndex(LibUri, 1, [sym], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("Foo", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var warning = result.Single(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("overrides", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, warning.Range.Start.Line);
        Assert.Equal(9, warning.Range.Start.Character); // after "function "
    }

    // ── redundant require (Feature E) ───────────────────────────────────────

    [Fact]
    public void Analyze_RequiredFile_AlreadyTransitivelyRequired_EmitsRedundantWarning()
    {
        // A requires both B and C; B transitively requires C → require("c") is redundant.
        const string uriB = "file:///scripts/b.lua";
        const string uriC = "file:///scripts/c.lua";
        const string text = """
                            require("b")
                            require("c")
                            """;
        var docB = new DocumentIndex(uriB, 1, [], [], ImmutableArray.Create("c"));
        var docC = new DocumentIndex(uriC, 1, [], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, docB)
                .Add(uriC, docC)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var redundant = result.Where(d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(redundant);
        Assert.Contains("\"c\"", redundant[0].Message);
    }

    [Fact]
    public void Analyze_RequiredFiles_NoTransitiveOverlap_NoRedundantWarning()
    {
        // A requires B and C; neither requires the other → no redundant.
        const string uriB = "file:///scripts/b.lua";
        const string uriC = "file:///scripts/c.lua";
        const string text = """
                            require("b")
                            require("c")
                            """;
        var docB = new DocumentIndex(uriB, 1, [], []);
        var docC = new DocumentIndex(uriC, 1, [], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, docB)
                .Add(uriC, docC)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.DoesNotContain(result, d =>
            d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_SingleRequire_NoRedundantWarning()
    {
        const string uriB = "file:///scripts/b.lua";
        const string text = """require("b")""";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, new DocumentIndex(uriB, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.DoesNotContain(result, d =>
            d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RedundantRequire_MessageMentionsCoveringModule()
    {
        // A requires PGStateMachine and PGBase; PGStateMachine.RequireArgs=["pgbase"].
        const string uriSm = "file:///scripts/pgstatemachine.lua";
        const string uriBase = "file:///scripts/pgbase.lua";
        const string text = """
                            require("PGStateMachine")
                            require("PGBase")
                            """;
        var docSm = new DocumentIndex(uriSm, 1, [], [], ImmutableArray.Create("PGBase"));
        var docBase = new DocumentIndex(uriBase, 1, [], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriSm, docSm)
                .Add(uriBase, docBase)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var redundant = result.Single(d =>
            d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("PGBase", redundant.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PGStateMachine", redundant.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_DeepTransitiveRedundant_EmitsWarning()
    {
        // A requires B and D; B→C→D (deep chain) → require("d") is redundant.
        const string uriB = "file:///scripts/b.lua";
        const string uriC = "file:///scripts/c.lua";
        const string uriD = "file:///scripts/d.lua";
        const string text = """
                            require("b")
                            require("d")
                            """;
        var docB = new DocumentIndex(uriB, 1, [], [], ImmutableArray.Create("c"));
        var docC = new DocumentIndex(uriC, 1, [], [], ImmutableArray.Create("d"));
        var docD = new DocumentIndex(uriD, 1, [], []);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, docB)
                .Add(uriC, docC)
                .Add(uriD, docD)
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var redundant = result.Where(d =>
            d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(redundant);
        Assert.Contains("\"d\"", redundant[0].Message);
    }

    // ── duplicate require (Phase 8) ───────────────────────────────────────────

    [Fact]
    public void Analyze_SameRequireTwice_EmitsDuplicateWarningOnSecond()
    {
        const string uriB = "file:///scripts/b.lua";
        const string text = "require(\"b\")\nrequire(\"b\")";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, new DocumentIndex(uriB, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var dups = result.Where(d => d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(dups);
        Assert.Equal(DiagnosticSeverity.Warning, dups[0].Severity);
        Assert.Equal(1, dups[0].Range.Start.Line); // second require is on line 1
    }

    [Fact]
    public void Analyze_SameRequireThreeTimes_EmitsTwoDuplicateWarnings()
    {
        const string uriB = "file:///scripts/b.lua";
        const string text = "require(\"b\")\nrequire(\"b\")\nrequire(\"b\")";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, new DocumentIndex(uriB, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var dups = result.Where(d => d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, dups.Count);
    }

    [Fact]
    public void Analyze_SingleRequire_NoDuplicateWarning()
    {
        const string uriB = "file:///scripts/b.lua";
        const string text = "require(\"b\")";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, new DocumentIndex(uriB, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.DoesNotContain(result, d => d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DifferentRequires_NoDuplicateWarning()
    {
        const string uriB = "file:///scripts/b.lua";
        const string uriC = "file:///scripts/c.lua";
        const string text = "require(\"b\")\nrequire(\"c\")";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, new DocumentIndex(uriB, 1, [], []))
                .Add(uriC, new DocumentIndex(uriC, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        Assert.DoesNotContain(result, d => d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_UnresolvableRequireDuplicated_EmitsDuplicateWarning()
    {
        // Even when the module cannot be resolved, two identical require calls are still duplicates.
        const string text = "require(\"unknown_lib\")\nrequire(\"unknown_lib\")";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var dups = result.Where(d => d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(dups);
    }

    [Fact]
    public void Analyze_DuplicateRequire_DiagnosticCodeIsDuplicateRequire()
    {
        const string uriB = "file:///scripts/b.lua";
        const string text = "require(\"b\")\nrequire(\"b\")";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, new DocumentIndex(uriB, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var dup = result.Single(d => d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.True(dup.Code?.IsString == true);
        Assert.Equal(LuaDiagnosticCodes.DuplicateRequire, dup.Code?.String);
    }

    [Fact]
    public void Analyze_DuplicateRequire_MessageMentionsOriginalLineNumber()
    {
        const string uriB = "file:///scripts/b.lua";
        const string text = "require(\"b\")\nrequire(\"b\")";
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(uriB, new DocumentIndex(uriB, 1, [], []))
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var dup = result.Single(d => d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        // Original is on line 0 (0-indexed) = line 1 for human display.
        Assert.Contains("line 1", dup.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── sandbox isolation (Tier 3 standalones invisible to other files) ───────

    [Fact]
    public void Analyze_GlobalInStandaloneFile_NotRequiredAnywhere_NoDiagnostic()
    {
        // PlanB is standalone (nobody requires it). CurrentUri uses "Definitions" which happens
        // to be defined in PlanB, but CurrentUri also defines its own Definitions.
        // After sandbox isolation: PlanB's global is invisible → no missing-require warning.
        const string planB = "file:///scripts/ai/plan_b.lua";
        const string text = """
                            function Definitions() end
                            Definitions()
                            """;
        var symCurrent = new GameSymbol("Definitions", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(CurrentUri, 0, null), null);
        var symOther = new GameSymbol("Definitions", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(planB, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [symCurrent], []))
                .Add(planB, new DocumentIndex(planB, 1, [symOther], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("Definitions", [symCurrent, symOther])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_StandaloneGlobalUsedByAnotherStandalone_NoDiagnostic()
    {
        // PlanB defines UniqueHelper(). CurrentUri calls UniqueHelper() but doesn't define it.
        // PlanB is standalone (nobody requires it) → UniqueHelper is invisible → no warning.
        const string planB = "file:///scripts/ai/plan_b.lua";
        const string text = "UniqueHelper()";
        var sym = new GameSymbol("UniqueHelper", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(planB, 0, null), null);
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(planB, new DocumentIndex(planB, 1, [sym], [])),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("UniqueHelper", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_LibraryGlobalUsedByStandalone_NotRequired_EmitsWarning()
    {
        // Library files (Tier 1) are always shared - their globals ARE visible and trigger warnings.
        const string text = "LibHelper()";
        var index = AddCurrentDoc(IndexWithLibGlobal("LibHelper"));

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("LibHelper", diag.Message);
    }

    [Fact]
    public void Analyze_DependencyGlobalUsedByStandalone_NotRequired_EmitsWarning()
    {
        // Dependency files (Tier 2: required by some other file) are shared - their globals trigger warnings.
        const string depUri = "file:///scripts/pgstatemachine.lua";
        const string requirer = "file:///scripts/ai/plan_x.lua";
        const string text = "StateMachineInit()";
        var sym = new GameSymbol("StateMachineInit", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(depUri, 0, null), null);
        // plan_x requires pgstatemachine → depUri becomes Dependency (Tier 2)
        var requirerDoc = new DocumentIndex(requirer, 1, [], [], ImmutableArray.Create("pgstatemachine"));
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(depUri, new DocumentIndex(depUri, 1, [sym], []))
                .Add(requirer, requirerDoc),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions.Add("StateMachineInit", [sym])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);

        var diag = Assert.Single(result);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("StateMachineInit", diag.Message);
    }

    [Fact]
    public void Analyze_MultipleFilesDefineGlobal_AtLeastOneRequired_NoDiagnostic()
    {
        // HelperA is defined in both LibUri AND OtherUri; current file requires "mylib" (LibUri).
        // Since at least one defining URI is in the require closure, no warning.
        const string text = """
                            require("mylib")
                            HelperA()
                            """;
        var symA = new GameSymbol("HelperA", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(LibUri, 0, null), null);
        var symB = new GameSymbol("HelperA", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(OtherUri, 0, null), null);
        var libDoc = new DocumentIndex(LibUri, 1, [symA], []);
        // Make OtherUri a dependency so it's in the shared set
        const string requirer = "file:///scripts/ai/plan_x.lua";
        var otherDoc = new DocumentIndex(OtherUri, 1, [symB], []);
        var requirerDoc = new DocumentIndex(requirer, 1, [], [], ImmutableArray.Create("other"));
        var index = GameIndex.Empty with
        {
            Documents = GameIndex.Empty.Documents
                .Add(CurrentUri, new DocumentIndex(CurrentUri, 1, [], []))
                .Add(LibUri, libDoc)
                .Add(OtherUri, otherDoc)
                .Add(requirer, requirerDoc),
            WorkspaceDefinitions = GameIndex.Empty.WorkspaceDefinitions
                .Add("HelperA", [symA, symB])
        };

        var result = LuaGlobalScopeAnalyzer.Analyze(CurrentUri, text, index, EmptySchema, s_fileHelper);
        Assert.Empty(result);
    }
}