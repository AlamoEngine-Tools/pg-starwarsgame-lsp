// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaInlayHintHandlerTest
{
    private const string LuaUri = "file:///script.lua";

    private static InlayHintParams RequestAt(int startLine, int endLine, string uri = LuaUri)
    {
        return new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uri) },
            Range = new LspRange(new Position(startLine, 0), new Position(endLine, 999))
        };
    }

    private static LuaInlayHintHandler BuildHandler(
        GameIndex? index = null,
        ILuaApiSchemaProvider? schema = null,
        ILuaAnnotationRepository? repo = null,
        string docText = "",
        string docUri = LuaUri,
        ILspConfigurationProvider? config = null)
    {
        var host = new FakeWorkspaceHost();
        if (docText.Length > 0) host.AddOrUpdate(docUri, docText, 1);
        var svc = new FakeIndexService { Current = index ?? GameIndex.Empty };
        return new LuaInlayHintHandler(
            svc,
            TestLuaParseCache.For(host),
            new FileHelper(new MockFileSystem()),
            schema ?? new LuaApiSchemaProvider([]),
            repo ?? new LuaAnnotationRepository(),
            NullLogger<LuaInlayHintHandler>.Instance,
            config ?? new FakeLspConfigurationProvider());
    }

    // ── feature flag ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LuaInlayHintsFlagOff_ReturnsNull()
    {
        // Same arrange as Handle_EngineFunction_NonSpeakingArg_ShowsHint - only the flag differs.
        const string lua = """
                           --- Plays a movie.
                           ---@param mission_name string
                           ---@param force_level integer
                           function PlayMovie(mission_name, force_level) end
                           """;
        var schema = new LuaApiSchemaProvider([lua]);
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Lua = new LuaFeatureFlags { InlayHints = false } });
        var handler = BuildHandler(schema: schema, docText: "PlayMovie(x, y)", config: config);

        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);

        Assert.Null(result);
    }

    private static IReadOnlyList<InlayHint> GetHints(InlayHintContainer? container)
    {
        return container?.ToList() ?? [];
    }

    // ── gating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NonLuaFile_ReturnsNull()
    {
        var handler = BuildHandler();
        var result = await handler.Handle(RequestAt(0, 0, "file:///test.xml"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_UnknownDocument_ReturnsNull()
    {
        var handler = BuildHandler();
        var result = await handler.Handle(RequestAt(0, 10), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_FunctionWithNoParams_ReturnsNoHints()
    {
        var schema = new LuaApiSchemaProvider(["function NoParams() end"]);
        var handler = BuildHandler(schema: schema, docText: "NoParams(42)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        Assert.Empty(GetHints(result));
    }

    // ── engine-schema params ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_EngineFunction_NonSpeakingArg_ShowsHint()
    {
        const string lua = """
                           --- Plays a movie.
                           ---@param mission_name string
                           ---@param force_level integer
                           function PlayMovie(mission_name, force_level) end
                           """;
        var schema = new LuaApiSchemaProvider([lua]);
        // Call with short non-speaking args
        var handler = BuildHandler(schema: schema, docText: "PlayMovie(x, y)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        var hints = GetHints(result);
        Assert.Equal(2, hints.Count);
        Assert.Contains(hints, h => GetLabel(h).StartsWith("mission_name"));
        Assert.Contains(hints, h => GetLabel(h).StartsWith("force_level"));
    }

    [Fact]
    public async Task Handle_EngineFunction_SpeakingArgContainsParamName_SuppressesHint()
    {
        const string lua = """
                           ---@param mission string
                           function StartMission(mission) end
                           """;
        var schema = new LuaApiSchemaProvider([lua]);
        // "run_mission" contains "mission" → should suppress hint
        var handler = BuildHandler(schema: schema, docText: "StartMission(run_mission)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        Assert.Empty(GetHints(result));
    }

    [Fact]
    public async Task Handle_EngineFunction_ShortArgNotInParamName_ShowsHint()
    {
        const string lua = """
                           ---@param count integer
                           function SetCount(count) end
                           """;
        var schema = new LuaApiSchemaProvider([lua]);
        // "x" (len < 3) is not in "count" → show hint
        var handler = BuildHandler(schema: schema, docText: "SetCount(x)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        var hints = GetHints(result);
        Assert.Single(hints);
        Assert.StartsWith("count", GetLabel(hints[0]));
    }

    [Fact]
    public async Task Handle_EngineFunction_ShortArgIsInParamName_SuppressesHint()
    {
        const string lua = """
                           ---@param id string
                           function SetId(id) end
                           """;
        var schema = new LuaApiSchemaProvider([lua]);
        // "id" (len == 2, < 3) IS in param name "id" → suppress
        var handler = BuildHandler(schema: schema, docText: "SetId(id)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        Assert.Empty(GetHints(result));
    }

    // ── workspace function params ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_WorkspaceFunction_NonSpeakingArg_ShowsHint()
    {
        var sym = new GameSymbol("MyFunc", GameSymbolKind.LuaGlobal, null,
            new FileOrigin("file:///lib.lua", 0, null), null);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("MyFunc", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var repo = new LuaAnnotationRepository();
        var ann = EmmyLuaAnnotations.Empty with
        {
            Params = [new LuaParamAnnotation("target_object", false, new LuaTypeRef("string"), null)]
        };
        repo.UpdateFunctionAnnotations("file:///lib.lua", [("MyFunc", ann)]);

        var handler = BuildHandler(index, repo: repo, docText: "MyFunc(x)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        var hints = GetHints(result);
        Assert.Single(hints);
        Assert.StartsWith("target_object", GetLabel(hints[0]));
    }

    [Fact]
    public async Task Handle_WorkspaceFunction_SpeakingArg_SuppressesHint()
    {
        var sym = new GameSymbol("MyFunc", GameSymbolKind.LuaGlobal, null,
            new FileOrigin("file:///lib.lua", 0, null), null);
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("MyFunc", [sym]),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var repo = new LuaAnnotationRepository();
        var ann = EmmyLuaAnnotations.Empty with
        {
            Params = [new LuaParamAnnotation("target", false, new LuaTypeRef("string"), null)]
        };
        repo.UpdateFunctionAnnotations("file:///lib.lua", [("MyFunc", ann)]);

        // "the_target_unit" contains "target" → suppress
        var handler = BuildHandler(index, repo: repo, docText: "MyFunc(the_target_unit)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        Assert.Empty(GetHints(result));
    }

    // ── range filtering ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CallOutsideRange_ReturnsNoHints()
    {
        const string lua = """
                           ---@param x integer
                           function Foo(x) end
                           """;
        var schema = new LuaApiSchemaProvider([lua]);
        // Call is on line 0; range covers only line 5+
        var handler = BuildHandler(schema: schema, docText: "Foo(z)");
        var result = await handler.Handle(RequestAt(5, 10), CancellationToken.None);
        Assert.Empty(GetHints(result));
    }

    // ── hint position ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_HintPosition_IsAtStartOfArgument()
    {
        const string lua = """
                           ---@param count integer
                           function SetCount(count) end
                           """;
        var schema = new LuaApiSchemaProvider([lua]);
        var handler = BuildHandler(schema: schema, docText: "SetCount(z)");
        var result = await handler.Handle(RequestAt(0, 0), CancellationToken.None);
        var hints = GetHints(result);
        Assert.Single(hints);
        // "z" starts at column 9 (after "SetCount(")
        Assert.Equal(0, hints[0].Position.Line);
        Assert.Equal(9, hints[0].Position.Character);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string GetLabel(InlayHint hint)
    {
        return hint.Label.String ?? "";
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;
        public event Action<ILocalisationIndex>? LocalisationChanged;
        public event Action<GameIndex>? DynamicEnumChanged;

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

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
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

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}