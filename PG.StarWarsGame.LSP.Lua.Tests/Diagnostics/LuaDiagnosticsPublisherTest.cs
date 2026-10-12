// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Diagnostics;

public sealed class LuaDiagnosticsPublisherTest
{
    private const string LuaUri = "file:///script.lua";
    private const string XmlUri = "file:///units.xml";

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (LuaDiagnosticsPublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeGameIndexService indexService,
        FakeGameWorkspaceHost workspaceHost) Build(
            ILuaApiSchemaProvider? schema = null,
            ILspConfigurationProvider? config = null)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var fileHelper = new FileHelper(new MockFileSystem());
        var publisher = new LuaDiagnosticsPublisher(
            p => published.Add(p),
            indexService,
            workspaceHost,
            fileHelper,
            schema ?? new LuaApiSchemaProvider([]),
            NullLogger<LuaDiagnosticsPublisher>.Instance,
            configProvider: config);
        return (publisher, published, indexService, workspaceHost);
    }

    // ── the analyzer's findings, folded in (#153) ───────────────────────────

    private static (List<PublishDiagnosticsParams> published, FakeGameWorkspaceHost host, FakeAnalyzer analyzer,
        FakeGameIndexService index) BuildWithAnalyzer(bool running = true)
    {
        var published = new List<PublishDiagnosticsParams>();
        var index = new FakeGameIndexService();
        var host = new FakeGameWorkspaceHost();
        var analyzer = new FakeAnalyzer { IsRunning = running };
        _ = new LuaDiagnosticsPublisher(p => published.Add(p), index, host, new FileHelper(new MockFileSystem()),
            new LuaApiSchemaProvider([]), NullLogger<LuaDiagnosticsPublisher>.Instance, analyzer: analyzer);
        return (published, host, analyzer, index);
    }

    private static Diagnostic AnalyzerFinding(string rule, int line = 0)
    {
        return new Diagnostic
        {
            Code = rule, Severity = DiagnosticSeverity.Warning, Message = rule + " here",
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(line, 0, line, 1)
        };
    }

    private static List<string> Codes(PublishDiagnosticsParams p)
    {
        return p.Diagnostics.Select(d => d.Code?.String ?? "").ToList();
    }

    [Fact]
    public void AnAnalyzerPublish_ForAnOpenDocument_IsFoldedIntoOnePublish_UnderOurIds()
    {
        var (published, host, analyzer, _) = BuildWithAnalyzer();
        host.Set(LuaUri, "local x = 1");

        analyzer.Raise(LuaUri, AnalyzerFinding("unused"), AnalyzerFinding("param-type-mismatch"));

        var last = published.Last(p => p.Uri.ToString() == LuaUri);
        Assert.Contains(DiagnosticIds.LuaAnalyzerUnused.ToString(), Codes(last));
        Assert.DoesNotContain("param-type-mismatch", Codes(last));
        Assert.DoesNotContain("unused", Codes(last));
    }

    /// <summary>
    ///     The analyzer writes URIs its own way (case, escaping); the open document keeps the
    ///     editor's. Measured live: matched exactly, not one finding arrived.
    /// </summary>
    [Fact]
    public void AnAnalyzerPublish_InAnotherCase_ReachesTheOpenDocument()
    {
        var (published, host, analyzer, _) = BuildWithAnalyzer();
        host.Set("file:///Mods/MyMod/Data/Scripts/Script.lua", "local x = 1");

        analyzer.Raise("file:///mods/mymod/data/scripts/script.lua", AnalyzerFinding("unused"));

        var last = published.Last();
        Assert.Equal("file:///Mods/MyMod/Data/Scripts/Script.lua", last.Uri.ToString());
        Assert.Contains(DiagnosticIds.LuaAnalyzerUnused.ToString(), Codes(last));
    }

    [Fact]
    public void AnAnalyzerPublish_ForAClosedDocument_PublishesNothing()
    {
        var (published, _, analyzer, _) = BuildWithAnalyzer();

        analyzer.Raise(LuaUri, AnalyzerFinding("unused"));

        Assert.Empty(published);
    }

    [Fact]
    public void AnAnalyzerFinding_StaysThroughTheNextIndexRepublish()
    {
        var (published, host, analyzer, index) = BuildWithAnalyzer();
        host.Set(LuaUri, "local x = 1");
        analyzer.Raise(LuaUri, AnalyzerFinding("unused"));

        index.Fire(GameIndex.Empty);

        Assert.Contains(DiagnosticIds.LuaAnalyzerUnused.ToString(), Codes(published.Last()));
    }

    [Fact]
    public void WithTheAnalyzerRunning_ItsSyntaxErrorIsTheOnlyOne()
    {
        var (published, host, analyzer, index) = BuildWithAnalyzer();
        host.Set(LuaUri, "local broken = {1 2}");
        analyzer.Raise(LuaUri, AnalyzerFinding("syntax-error"));

        index.Fire(GameIndex.Empty);

        var codes = Codes(published.Last());
        Assert.Equal([DiagnosticIds.LuaSyntaxError.ToString()], codes);
    }

    [Fact]
    public void WithoutTheAnalyzer_OurOwnParserStillReportsSyntaxErrors()
    {
        var (published, host, _, index) = BuildWithAnalyzer(running: false);
        host.Set(LuaUri, "local broken = {1 2}");

        index.Fire(GameIndex.Empty);

        Assert.NotEmpty(published.Last().Diagnostics);
        Assert.DoesNotContain(DiagnosticIds.LuaSyntaxError.ToString(), Codes(published.Last()));
    }

    [Fact]
    public void AnAnalyzerFinding_IsSilencedByAnInlineSuppression()
    {
        var (published, host, analyzer, _) = BuildWithAnalyzer();
        host.Set(LuaUri, $"-- aetswg:suppress {DiagnosticIds.LuaAnalyzerUnused}\nlocal x = 1");

        analyzer.Raise(LuaUri, AnalyzerFinding("unused", line: 1));

        Assert.DoesNotContain(DiagnosticIds.LuaAnalyzerUnused.ToString(), Codes(published.Last()));
    }

    private sealed class FakeAnalyzer : ILuaAnalyzer
    {
        public bool IsRunning { get; set; }

        public event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

        public void DidOpen(string uri, string text, int version)
        {
        }

        public void DidChange(string uri, string text, int version)
        {
        }

        public Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class
        {
            return Task.FromResult<T?>(null);
        }

        public void DidClose(string uri)
        {
        }

        public void Raise(string uri, params Diagnostic[] diagnostics)
        {
            DiagnosticsPublished?.Invoke(new PublishDiagnosticsParams
            {
                Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(uri),
                Diagnostics = new Container<Diagnostic>(diagnostics)
            });
        }
    }

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_LuaDiagnosticsFlagOff_PublishesNothing()
    {
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Lua = new LuaFeatureFlags { Diagnostics = false } });
        var (_, published, indexService, workspaceHost) = Build(config: config);
        workspaceHost.Set(LuaUri, "function Foo() end");

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    private static GameIndex IndexWithLuaRef(string documentUri, string targetId,
        string? expectedTypeName = null, GameSymbol? resolvedSymbol = null,
        GameSymbolKind kind = GameSymbolKind.XmlObject, ILocalisationIndex? localisation = null)
    {
        var reference = new GameReference(targetId, kind, expectedTypeName,
            documentUri, 0, 20, targetId.Length);
        var doc = new DocumentIndex(documentUri, 1, [], [reference]);

        var definitions = resolvedSymbol is not null
            ? ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add(targetId, [resolvedSymbol])
            : ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty;

        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(documentUri, doc),
            definitions,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
        return localisation is null ? index : index with { Localisation = localisation };
    }

    // ── text keys ────────────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_UnknownTextKey_WarnsWithTheLocalisationId()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Game_Message("TEXT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "TEXT_MISSING", kind: GameSymbolKind.LocalisationKey,
            localisation: new StubLocalisationIndex("TEXT_OTHER"));

        indexService.Fire(index);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Equal(DiagnosticIds.LocalisationKeyExistence.ToString(), diag.Code?.String);
        Assert.Contains("TEXT_MISSING", diag.Message);
    }

    [Fact]
    public void OnIndexChanged_KnownTextKey_NoDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Game_Message("TEXT_HELLO")""");
        var index = IndexWithLuaRef(LuaUri, "TEXT_HELLO", kind: GameSymbolKind.LocalisationKey,
            localisation: new StubLocalisationIndex("TEXT_HELLO"));

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    private sealed class StubLocalisationIndex(params string[] keys) : ILocalisationIndex
    {
        public IEnumerable<string> Keys => keys;

        public bool ContainsKey(string key)
        {
            return keys.Contains(key, StringComparer.OrdinalIgnoreCase);
        }

        public string? GetValue(string key)
        {
            return ContainsKey(key) ? key : null;
        }
    }

    // ── shared parse (cache) ──────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_PlanWithoutThreadFunction_PublishesThePlanDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, "TaskForce = { { \"MainForce\" } }\n");
        indexService.Fire(GameIndex.Empty);

        var diagnostic = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(DiagnosticIds.LuaPlanThreadFunctionMissing.ToString(), diagnostic.Code?.String);
    }

    [Fact]
    public void PublishForDocument_SameContentTwice_ParsesOnce()
    {
        // One publish = one parse shared by the syntax-error pass and all three analyzers
        // (previously four separate parses); a second publish of unchanged content is a cache hit.
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var fileHelper = new FileHelper(new MockFileSystem());
        var cache = TestLuaParseCache.For(workspaceHost, fileHelper);
        var publisher = new LuaDiagnosticsPublisher(
            p => published.Add(p),
            indexService,
            workspaceHost,
            fileHelper,
            new LuaApiSchemaProvider([]),
            NullLogger<LuaDiagnosticsPublisher>.Instance,
            parseCache: cache);
        workspaceHost.Set(LuaUri, "function Foo() end");

        indexService.Fire(GameIndex.Empty);
        indexService.Fire(IndexWithLuaRef(LuaUri, "UNIT_A")); // different index, same text

        Assert.Equal(2, published.Count);
        var (hits, misses, _) = cache.Statistics;
        Assert.Equal(1, misses);
        Assert.Equal(1, hits);
    }

    // ── file-type filtering ───────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_XmlFileOpen_NoPublish()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(XmlUri, "<Root/>");

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void OnIndexChanged_NoOpenLuaFiles_NoPublish()
    {
        var (_, published, indexService, _) = Build();

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    // ── unresolved XML references ─────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_UnresolvedXmlRef_EmitsErrorDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("UNIT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "UNIT_MISSING");

        indexService.Fire(index);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("UNIT_MISSING", diag.Message);
        Assert.Contains("No object with this name exists", diag.Message);
    }

    // ── suppression, end to end ───────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_SuppressionDirective_SilencesTheDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri,
            $"-- aetswg:suppress {DiagnosticIds.UnresolvedReference} reason:: spawned at runtime\n" +
            """Find_First_Object("UNIT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "UNIT_MISSING");

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    // The directive names one id; everything else it does not name stays visible.
    [Fact]
    public void OnIndexChanged_SuppressionOfAnotherId_LeavesTheDiagnosticAlone()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri,
            $"-- aetswg:suppress {DiagnosticIds.LuaEngineUpvalue}\n" +
            """Find_First_Object("UNIT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "UNIT_MISSING");

        indexService.Fire(index);

        Assert.Single(Assert.Single(published).Diagnostics!);
    }

    // The failure this prevents: a mistyped directive suppresses nothing and says nothing, so the
    // diagnostic just sits there and the user has no way to learn their comment is the problem.
    [Fact]
    public void OnIndexChanged_MistypedDirective_WarnsAndLeavesTheDiagnosticVisible()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri,
            "-- aetswg:suppress aetswg-1-21\n" +
            """Find_First_Object("UNIT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "UNIT_MISSING");

        indexService.Fire(index);

        var diagnostics = Assert.Single(published).Diagnostics!.ToList();
        var warning = Assert.Single(diagnostics,
            d => d.Code?.String == DiagnosticIds.SuppressionUnknownRule.ToString());

        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(0, warning.Range.Start.Line);
        Assert.Contains("aetswg-1-21", warning.Message);

        // And the diagnostic the user was trying to silence is still there.
        Assert.Contains(diagnostics, d => d.Message.Contains("UNIT_MISSING"));
    }

    // Suppression complaints go through the same filter as everything else, so a user who has
    // decided they do not want them can turn them off like any other group.
    [Fact]
    public void OnIndexChanged_SuppressionGroupSuppressed_SilencesTheWarningItself()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri,
            "-- aetswg:suppress-file aetswg-013-*\n" +
            "-- aetswg:suppress aetswg-1-21\n" +
            """Find_First_Object("UNIT_MISSING")""");

        indexService.Fire(IndexWithLuaRef(LuaUri, "UNIT_MISSING"));

        var diagnostics = Assert.Single(published).Diagnostics!.ToList();

        Assert.DoesNotContain(diagnostics,
            d => d.Code?.String == DiagnosticIds.SuppressionUnknownRule.ToString());

        // Still only silencing what was asked for: the mistyped directive suppresses nothing, so
        // the reference error it named remains.
        Assert.Contains(diagnostics, d => d.Message.Contains("UNIT_MISSING"));
    }

    [Fact]
    public void OnIndexChanged_ResolvedRef_NoDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("UNIT_A")""");
        var symbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, null,
            new FileOrigin(XmlUri, 0, null), null);
        var index = IndexWithLuaRef(LuaUri, "UNIT_A", resolvedSymbol: symbol);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public void OnIndexChanged_TypeMismatch_EmitsErrorDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_Player("UNIT_A")""");
        var symbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(XmlUri, 0, null), null);
        var index = IndexWithLuaRef(LuaUri, "UNIT_A",
            "Faction", symbol);

        indexService.Fire(index);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("UNIT_A", diag.Message);
        Assert.Contains("Faction", diag.Message);
    }

    [Fact]
    public void OnIndexChanged_TypeMismatch_GameObjectType_NoDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Spawn_Object("UNIT_A")""");
        var symbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin(XmlUri, 0, null), null);
        // GameObjectType is a wildcard - any XmlObject matches, no diagnostic expected.
        var index = IndexWithLuaRef(LuaUri, "UNIT_A", "GameObjectType", symbol);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public void OnIndexChanged_ResolvedRef_MatchingType_NoDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_Player("REBEL")""");
        var symbol = new GameSymbol("REBEL", GameSymbolKind.XmlObject, "Faction",
            new FileOrigin(XmlUri, 0, null), null);
        var index = IndexWithLuaRef(LuaUri, "REBEL",
            "Faction", symbol);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public void OnIndexChanged_DiagnosticRange_MatchesReference_LineAndColumn()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("UNIT_MISSING")""");
        var index = IndexWithLuaRef(LuaUri, "UNIT_MISSING");

        indexService.Fire(index);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Equal(0, diag.Range.Start.Line);
        Assert.Equal(20, diag.Range.Start.Character);
        Assert.Equal(0, diag.Range.End.Line);
        Assert.Equal(20 + "UNIT_MISSING".Length, diag.Range.End.Character);
    }

    // ── syntax errors ────────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_LuaSyntaxError_EmitsErrorDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = Build();
        // Incomplete function - a real Lua syntax error
        workspaceHost.Set(LuaUri, "function Foo(");
        var doc = new DocumentIndex(LuaUri, 1, [], []);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        Assert.Contains(pub.Diagnostics!, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OnIndexChanged_ValidLua_NoSyntaxDiagnostics()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, "function Definitions() end");
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    // Loretta's ids are surfaced through our own scheme rather than raw, so a syntax error can be
    // named by a suppression like any other diagnostic. The number is preserved, so it still
    // identifies the exact Loretta diagnostic to anyone looking it up.
    [Fact]
    public void OnIndexChanged_LuaSyntaxError_DiagnosticCodeIsTheMappedDiagnosticId()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, "function Foo(");
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        var errorDiags = pub.Diagnostics!.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.NotEmpty(errorDiags);
        Assert.All(errorDiags, d =>
        {
            Assert.True(DiagnosticId.TryParse(d.Code?.String, out var id),
                $"Code should be a diagnostic id, was: {d.Code}");
            Assert.Equal((int)DiagnosticGroup.Syntax, id.Group);
        });
    }

    // ── import (require) errors ───────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_MissingRequire_EmitsError()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """require("MissingLib")""");

        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        Assert.Contains(pub.Diagnostics!,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("MissingLib"));
    }

    [Fact]
    public void OnIndexChanged_RequireResolved_NoImportError()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """require("PGStateMachine")""");

        const string libUri = "file:///data/scripts/library/pgstatemachine.lua";
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []))
                .Add(libUri, new DocumentIndex(libUri, 1, [], [])),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        // No import error; the unused-require hint (L-6) is acceptable.
        Assert.DoesNotContain(Assert.Single(published).Diagnostics!,
            d => d.Severity == DiagnosticSeverity.Error);
    }

    // ── global-scope analysis (L-6) ──────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_UsesGlobalFromUnrequiredFile_EmitsWarning()
    {
        const string libUri = "file:///data/scripts/library/statemachine.lua";
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, "RunStateMachine()");

        var sym = new GameSymbol("RunStateMachine", GameSymbolKind.LuaGlobal, null,
            new FileOrigin(libUri, 0, null), null);
        var libDoc = new DocumentIndex(libUri, 1, [sym], []);
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(LuaUri, new DocumentIndex(LuaUri, 1, [], []))
                .Add(libUri, libDoc),
            GameIndex.Empty.WorkspaceDefinitions.Add("RunStateMachine", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        indexService.Fire(index);

        var pub = Assert.Single(published);
        Assert.Contains(pub.Diagnostics!,
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("RunStateMachine"));
    }

    // ── open/close lifecycle ──────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_ClosedLuaFile_ClearsDiagnostics()
    {
        var (_, published, indexService, workspaceHost) = Build();
        workspaceHost.Set(LuaUri, """Find_First_Object("MISSING")""");
        indexService.Fire(IndexWithLuaRef(LuaUri, "MISSING"));
        published.Clear();

        workspaceHost.Remove(LuaUri);
        indexService.Fire(GameIndex.Empty);

        var clear = Assert.Single(published);
        Assert.Equal(LuaUri, clear.Uri.ToString());
        Assert.Empty(clear.Diagnostics!);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = new();

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;

        public void Set(string uri, string text)
        {
            _docs[uri] = new TrackedDocument(uri, text, 1);
        }
    }

    internal sealed class FakeGameIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add => _indexChanged += value;
            remove => _indexChanged -= value;
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(
            ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(
            ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return NullDisposable.Instance;
        }

        private event Action<GameIndex>? _indexChanged;

        public void Fire(GameIndex index)
        {
            _indexChanged?.Invoke(index);
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}