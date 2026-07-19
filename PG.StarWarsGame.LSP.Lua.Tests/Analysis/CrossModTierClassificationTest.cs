// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Completion;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     Regression tests verifying that Library/Dependency/Standalone tier classification
///     and sandbox isolation work correctly when the workspace spans multiple mod projects
///     (a root mod plus one or more dependency mods at lower layer ranks).
/// </summary>
public sealed class CrossModTierClassificationTest
{
    // ── URIs representing a two-mod workspace ────────────────────────────────
    // Base mod (dependency, lower rank) paths:
    private const string BaseLibUri = "file:///base_mod/data/scripts/library/baseutils.lua";

    private const string BaseStandaloneUri = "file:///base_mod/data/scripts/ai/base_plan.lua";

    // Root (addon) mod paths:
    private const string RootScriptUri = "file:///addon_mod/data/scripts/story/rootscript.lua";
    private const string RootLibUri = "file:///addon_mod/data/scripts/library/addonutils.lua";

    private static readonly IFileHelper s_fileHelper = new FileHelper(new MockFileSystem());
    private static readonly ILuaApiSchemaProvider s_emptySchema = new LuaApiSchemaProvider([]);

    // ── helpers ──────────────────────────────────────────────────────────────

    private static DocumentIndex MakeDoc(string uri, int rank = 0, params string[] requireArgs)
    {
        return new DocumentIndex(uri, 1, [], [], ImmutableArray.Create(requireArgs), LayerRank: rank);
    }

    private static DocumentIndex MakeDocWithSymbol(string uri, string globalName, int rank = 0)
    {
        var sym = new GameSymbol(globalName, GameSymbolKind.LuaGlobal, null,
            new FileOrigin(uri, 0, null), null);
        return new DocumentIndex(uri, 1, [sym], [], LayerRank: rank);
    }

    private static Dictionary<string, DocumentIndex> MakeDocs(
        params (string uri, DocumentIndex doc)[] entries)
    {
        var dict = new Dictionary<string, DocumentIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var (uri, doc) in entries)
            dict[uri] = doc;
        return dict;
    }

    private static GameIndex BuildIndex(IReadOnlyDictionary<string, DocumentIndex> docs)
    {
        var workspaceDefs = ImmutableDictionary.CreateBuilder<string, ImmutableArray<GameSymbol>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs.Values)
        foreach (var sym in doc.Symbols)
        {
            workspaceDefs.TryGetValue(sym.Id, out var existing);
            workspaceDefs[sym.Id] = existing.IsDefault
                ? [sym]
                : existing.Add(sym);
        }

        return new GameIndex(
            BaselineIndex.Empty,
            docs.ToImmutableDictionary(StringComparer.Ordinal),
            workspaceDefs.ToImmutable(),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    // ── Tier classification across mod boundaries ─────────────────────────────

    [Fact]
    public void GetTier_DepModLibraryFile_IsLibraryRegardlessOfModPath()
    {
        var docs = MakeDocs(
            (BaseLibUri, MakeDoc(BaseLibUri, 1)),
            (RootScriptUri, MakeDoc(RootScriptUri, 2)));

        var tier = LuaFileClassifier.GetTier(BaseLibUri, docs, s_fileHelper);

        Assert.Equal(LuaFileTier.Library, tier);
    }

    [Fact]
    public void GetTier_DepModStandaloneFileNotRequired_IsStandalone()
    {
        // base_plan.lua is in a non-library directory and nobody requires it
        var docs = MakeDocs(
            (BaseStandaloneUri, MakeDoc(BaseStandaloneUri, 1)),
            (RootScriptUri, MakeDoc(RootScriptUri, 2)));

        var tier = LuaFileClassifier.GetTier(BaseStandaloneUri, docs, s_fileHelper);

        Assert.Equal(LuaFileTier.Standalone, tier);
    }

    [Fact]
    public void GetTier_DepModFileRequiredByRoot_IsDependency()
    {
        // Root mod explicitly requires base_plan; it becomes Dependency tier
        var docs = MakeDocs(
            (BaseStandaloneUri, MakeDoc(BaseStandaloneUri, 1)),
            (RootScriptUri, MakeDoc(RootScriptUri, 2, "base_plan")));

        var tier = LuaFileClassifier.GetTier(BaseStandaloneUri, docs, s_fileHelper);

        Assert.Equal(LuaFileTier.Dependency, tier);
    }

    [Fact]
    public void GetSharedUris_MultiModWorkspace_IncludesDepModLibraryAndExcludesStandalone()
    {
        var docs = MakeDocs(
            (BaseLibUri, MakeDoc(BaseLibUri, 1)),
            (BaseStandaloneUri, MakeDoc(BaseStandaloneUri, 1)),
            (RootScriptUri, MakeDoc(RootScriptUri, 2)),
            (RootLibUri, MakeDoc(RootLibUri, 2)));

        var shared = LuaFileClassifier.GetSharedUris(docs, s_fileHelper);

        Assert.Contains(BaseLibUri, shared);
        Assert.Contains(RootLibUri, shared);
        Assert.DoesNotContain(BaseStandaloneUri, shared);
        Assert.DoesNotContain(RootScriptUri, shared);
    }

    // ── Sandbox isolation via LuaGlobalScopeAnalyzer ─────────────────────────

    [Fact]
    public void GlobalOverride_DepModLibraryRequired_TriggersWarning()
    {
        // Root script requires the base mod's library file and redefines its global.
        // Because the library is required + shared, the override warning fires.
        const string rootText = """
                                require("baseutils")
                                function Base_Util() end
                                """;

        var baseLibDoc = MakeDocWithSymbol(BaseLibUri, "Base_Util", 1);
        var rootDoc = MakeDoc(RootScriptUri, 2, "baseutils");
        var docs = MakeDocs((BaseLibUri, baseLibDoc), (RootScriptUri, rootDoc));
        var index = BuildIndex(docs);

        var diagnostics = LuaGlobalScopeAnalyzer.Analyze(
            RootScriptUri, rootText, index, s_emptySchema, s_fileHelper);

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning &&
            d.Message.Contains("Base_Util") &&
            d.Message.Contains("overrides"));
    }

    [Fact]
    public void GlobalOverride_DepModStandaloneNotRequired_NoWarning()
    {
        // Root script defines the same function name as a standalone dep-mod file,
        // but doesn't require it - no cross-sandbox bleed, no warning.
        const string rootText = """
                                function Base_Plan() end
                                """;

        var baseDoc = MakeDocWithSymbol(BaseStandaloneUri, "Base_Plan", 1);
        var rootDoc = MakeDoc(RootScriptUri, 2);
        var docs = MakeDocs((BaseStandaloneUri, baseDoc), (RootScriptUri, rootDoc));
        var index = BuildIndex(docs);

        var diagnostics = LuaGlobalScopeAnalyzer.Analyze(
            RootScriptUri, rootText, index, s_emptySchema, s_fileHelper);

        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Base_Plan"));
    }

    [Fact]
    public void SandboxIsolation_SameNameInTwoStandalones_NoSpuriousOverrideWarning()
    {
        // Both the root and dep-mod standalone files define the same global name.
        // Since neither is in a shared (Library/Dependency) tier, the analyzer must NOT
        // emit a cross-sandbox global-override warning - the sandboxes are isolated.
        const string rootText = """
                                function Shared_Impl() end
                                """;

        var depDoc = MakeDocWithSymbol(BaseStandaloneUri, "Shared_Impl", 1);
        var rootDoc = MakeDoc(RootScriptUri, 2);
        var docs = MakeDocs((BaseStandaloneUri, depDoc), (RootScriptUri, rootDoc));
        var index = BuildIndex(docs);

        var diagnostics = LuaGlobalScopeAnalyzer.Analyze(
            RootScriptUri, rootText, index, s_emptySchema, s_fileHelper);

        // No override warning - the dep-mod's standalone file is not in the shared URI set
        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Shared_Impl"));
    }

    // ── Completion scope with cross-mod requires ──────────────────────────────

    [Fact]
    public void CompletionScope_RequiredDepModGlobal_AppearsAsRequiredGlobal()
    {
        // Root script requires the base lib; its global must appear in completion scope.
        var baseDoc = MakeDocWithSymbol(BaseLibUri, "Base_Util", 1);
        var rootDoc = new DocumentIndex(
            RootScriptUri, 1, [], [],
            ImmutableArray.Create("baseutils"),
            LayerRank: 2);
        var docs = MakeDocs((BaseLibUri, baseDoc), (RootScriptUri, rootDoc));
        var index = BuildIndex(docs);

        var entries = LuaLocalScopeCollector.CollectAt(
            "require(\"baseutils\")", 0, 0,
            RootScriptUri, index, s_emptySchema, s_fileHelper);

        Assert.Contains(entries, e =>
            e.Name == "Base_Util" && e.Kind == ScopeEntryKind.RequiredGlobal);
    }

    [Fact]
    public void CompletionScope_UnrequiredDepModStandaloneGlobal_AbsentFromScope()
    {
        // Root script never requires the dep-mod standalone file;
        // its globals must not appear in completion.
        var baseDoc = MakeDocWithSymbol(BaseStandaloneUri, "Base_Plan", 1);
        var rootDoc = MakeDoc(RootScriptUri, 2);
        var docs = MakeDocs((BaseStandaloneUri, baseDoc), (RootScriptUri, rootDoc));
        var index = BuildIndex(docs);

        var entries = LuaLocalScopeCollector.CollectAt(
            "", 0, 0,
            RootScriptUri, index, s_emptySchema, s_fileHelper);

        Assert.DoesNotContain(entries, e => e.Name == "Base_Plan");
    }
}