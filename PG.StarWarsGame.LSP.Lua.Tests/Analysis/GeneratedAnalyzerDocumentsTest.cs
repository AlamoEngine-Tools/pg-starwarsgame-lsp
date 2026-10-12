// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Generated;
using PG.StarWarsGame.LSP.Lua.Schema;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     The analyzer reads a generated document: the author's text with annotation lines inserted
///     above functions (#251). Every position going in is mapped onto it, every position coming
///     back is mapped off it, and anything on a generated line is dropped or refused.
/// </summary>
public sealed class GeneratedAnalyzerDocumentsTest
{
    private const string Uri = "file:///mod/data/scripts/gameobject/interdictor.lua";
    private const string Other = "file:///mod/data/scripts/library/pgcommands.lua";

    private const string Stub = "--- Finds a player.\n---@param name string\n---@return Player\n" +
                                "function Find_Player(name) end";

    // Authored line 1 overrides the stub's Find_Player: three lines go in above it, so authored
    // line 0 is above the insertion and authored line 4 (generated 7) below it.
    private const string Authored =
        "local x = 1\nfunction Find_Player(name)\n  return nil\nend\nFind_Player(\"Empire\")\n";

    private static (GeneratedAnalyzerDocuments Documents, ScriptedLuaAnalyzer Inner) Open()
    {
        var inner = new ScriptedLuaAnalyzer();
        var documents = new GeneratedAnalyzerDocuments(inner,
            [new StubOverrideAnnotations(new LuaApiSchemaProvider([Stub]))], new FileHelper(new MockFileSystem()));
        documents.DidOpen(Uri, Authored, 1);
        return (documents, inner);
    }

    private static JObject Range(int startLine, int endLine)
    {
        return JObject.FromObject(new
        {
            start = new { line = startLine, character = 0 }, end = new { line = endLine, character = 3 }
        });
    }

    private static TextDocumentIdentifier Doc => new(DocumentUri.From(Uri));

    private static JObject SentParams(ScriptedLuaAnalyzer inner)
    {
        return JObject.FromObject(inner.Requests[^1].Parameters);
    }

    // ── the line map ─────────────────────────────────────────────────────────

    [Fact]
    public void TheLineMap_ShiftsLinesBelowAnInsertion_AndKnowsGeneratedLines()
    {
        var map = new LuaLineMap([new LuaAnnotationInsertion(1, ["a", "b", "c"])]);

        Assert.Equal(0, map.ToGenerated(0));
        Assert.Equal(4, map.ToGenerated(1));
        Assert.Equal(1, map.ToAuthored(4));
        Assert.Equal(0, map.ToAuthored(0));
        Assert.Null(map.ToAuthored(2));
    }

    // ── what is generated ────────────────────────────────────────────────────

    [Fact]
    public void AnOverrideOfAStubGlobal_GetsTheStubsAnnotations_UnderTheScriptsOwnParameterNames()
    {
        var (_, inner) = Open();
        var again = new ScriptedLuaAnalyzer();
        var renamed = new GeneratedAnalyzerDocuments(again,
            [new StubOverrideAnnotations(new LuaApiSchemaProvider([Stub]))], new FileHelper(new MockFileSystem()));
        renamed.DidOpen(Uri, "function Find_Player(faction)\nend\n", 1);

        Assert.Equal("local x = 1\n--- Finds a player.\n---@param name string\n---@return Player\n" +
                     "function Find_Player(name)\n  return nil\nend\nFind_Player(\"Empire\")\n", inner.Texts[Uri]);
        Assert.StartsWith("--- Finds a player.\n---@param faction string\n", again.Texts[Uri]);
    }

    // The author's own annotation wins: nothing is generated above an annotated function.
    [Fact]
    public void AnAnnotatedFunction_GetsNothing()
    {
        var inner = new ScriptedLuaAnalyzer();
        var documents = new GeneratedAnalyzerDocuments(inner,
            [new StubOverrideAnnotations(new LuaApiSchemaProvider([Stub]))], new FileHelper(new MockFileSystem()));
        const string annotated = "---@param name string\nfunction Find_Player(name)\nend\n";

        documents.DidOpen(Uri, annotated, 1);

        Assert.Equal(annotated, inner.Texts[Uri]);
    }

    // A state handler's message is one of the library's three values (#252).
    [Fact]
    public void AStateHandler_GetsItsMessageTyped()
    {
        const string library = "file:///mod/data/scripts/library/pgstatemachine.lua";
        const string script = "require(\"PGStateMachine\")\nDefine_State(\"Init\", State_Init)\n" +
                              "function State_Init(msg)\nend\n";
        var host = new StateHost();
        host.AddOrUpdate(Uri, script, 1);
        host.AddOrUpdate(library, "function Define_State(s, f) end\n" +
                                  "function Set_Next_State(s) if StateMachine[s] ~= nil then end end\n" +
                                  "function Process_States() _curproc(OnEnter) _curproc(OnUpdate) _curproc(OnExit) end",
            1);
        var fileHelper = new FileHelper(new MockFileSystem());
        var index = new FakeIndexService
        {
            Current = new GameIndex(BaselineIndex.Empty,
                ImmutableDictionary<string, DocumentIndex>.Empty
                    .Add(Uri, new DocumentIndex(Uri, 1, [], [], ["PGStateMachine"]))
                    .Add(library, new DocumentIndex(library, 1, [], [])),
                ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
                ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty)
        };
        var inner = new ScriptedLuaAnalyzer();
        var documents = new GeneratedAnalyzerDocuments(inner,
            [new StateHandlerAnnotations(index, TestLuaParseCache.For(host, fileHelper), fileHelper)], fileHelper);

        documents.DidOpen(Uri, script, 1);

        Assert.Contains("---@param msg 1|2|3 OnEnter (1), OnUpdate (2) or OnExit (3)\nfunction State_Init(msg)",
            inner.Texts[Uri]);
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current { get; set; } = GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
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

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class StateHost : IGameWorkspaceHost
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

    [Fact]
    public void AChange_IsSentAsTheNewGeneratedText()
    {
        var (documents, inner) = Open();

        documents.DidChange(Uri, "-- moved\n" + Authored, 2);

        Assert.StartsWith("-- moved\nlocal x = 1\n--- Finds a player.\n", inner.Texts[Uri]);
    }

    // ── requests and answers, one per forwarded method ───────────────────────

    [Fact]
    public async Task Hover_PositionGoesOnto_RangeComesOff()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/hover", new JObject { ["contents"] = "x", ["range"] = Range(7, 7) });

        var hover = await documents.RequestAsync<Hover>("textDocument/hover",
            new HoverParams { TextDocument = Doc, Position = new Position(4, 3) }, CancellationToken.None);

        Assert.Equal(7, SentParams(inner)["position"]!["line"]!.Value<int>());
        Assert.Equal(4, hover!.Range!.Start.Line);
    }

    [Fact]
    public async Task Hover_AboveTheInsertion_IsUnchanged()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/hover", new JObject { ["contents"] = "x", ["range"] = Range(0, 0) });

        var hover = await documents.RequestAsync<Hover>("textDocument/hover",
            new HoverParams { TextDocument = Doc, Position = new Position(0, 7) }, CancellationToken.None);

        Assert.Equal(0, SentParams(inner)["position"]!["line"]!.Value<int>());
        Assert.Equal(0, hover!.Range!.Start.Line);
    }

    [Fact]
    public async Task Definition_OnAGeneratedLine_IsDropped_AnotherDocumentIsUntouched()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/definition", new JArray(
            new JObject { ["uri"] = Uri, ["range"] = Range(2, 2) },
            new JObject { ["uri"] = Uri, ["range"] = Range(4, 4) },
            new JObject { ["uri"] = Other, ["range"] = Range(2, 2) }));

        var result = await documents.RequestAsync<LocationOrLocationLinks>("textDocument/definition",
            new DefinitionParams { TextDocument = Doc, Position = new Position(4, 1) }, CancellationToken.None);

        Assert.Equal([(Uri, 1), (Other, 2)],
            result!.Select(l => (l.Location!.Uri.ToString(), l.Location.Range.Start.Line)));
    }

    // A link's origin is in the requesting document, its target in the target's.
    [Fact]
    public async Task Definition_ALinksOriginAndTarget_AreMappedEachInItsOwnDocument()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/definition", new JArray(new JObject
        {
            ["targetUri"] = Other, ["targetRange"] = Range(9, 9), ["targetSelectionRange"] = Range(9, 9),
            ["originSelectionRange"] = Range(7, 7)
        }));

        var result = await documents.RequestAsync<LocationOrLocationLinks>("textDocument/definition",
            new DefinitionParams { TextDocument = Doc, Position = new Position(4, 1) }, CancellationToken.None);

        var link = Assert.Single(result!).LocationLink!;
        Assert.Equal(9, link.TargetRange.Start.Line);
        Assert.Equal(4, link.OriginSelectionRange!.Start.Line);
    }

    [Fact]
    public async Task References_AreMapped_AndDroppedOnGeneratedLines()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/references", new JArray(
            new JObject { ["uri"] = Uri, ["range"] = Range(4, 4) },
            new JObject { ["uri"] = Uri, ["range"] = Range(3, 3) },
            new JObject { ["uri"] = Uri, ["range"] = Range(7, 7) }));

        var result = await documents.RequestAsync<LocationContainer>("textDocument/references",
            new ReferenceParams
                { TextDocument = Doc, Position = new Position(1, 10), Context = new ReferenceContext() },
            CancellationToken.None);

        Assert.Equal([1, 4], result!.Select(l => l.Range.Start.Line));
        Assert.Equal(4, SentParams(inner)["position"]!["line"]!.Value<int>());
    }

    [Fact]
    public async Task Rename_IsMapped()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/rename", new JObject
        {
            ["changes"] = new JObject { [Uri] = new JArray(new JObject { ["range"] = Range(7, 7), ["newText"] = "F" }) }
        });

        var edit = await documents.RequestAsync<WorkspaceEdit>("textDocument/rename",
            new RenameParams { TextDocument = Doc, Position = new Position(4, 1), NewName = "F" },
            CancellationToken.None);

        Assert.Equal(4, edit!.Changes![DocumentUri.From(Uri)].Single().Range.Start.Line);
    }

    // An edit to a generated line would write into text the author never wrote: the whole edit goes.
    [Fact]
    public async Task Rename_TouchingAGeneratedLine_IsRefusedWhole()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/rename", new JObject
        {
            ["changes"] = new JObject
            {
                [Uri] = new JArray(new JObject { ["range"] = Range(7, 7), ["newText"] = "F" },
                    new JObject { ["range"] = Range(2, 2), ["newText"] = "F" })
            }
        });

        var edit = await documents.RequestAsync<WorkspaceEdit>("textDocument/rename",
            new RenameParams { TextDocument = Doc, Position = new Position(4, 1), NewName = "F" },
            CancellationToken.None);

        Assert.Null(edit);
    }

    [Fact]
    public async Task PrepareRename_RangeComesOff()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/prepareRename", Range(7, 7));

        var range = await documents.RequestAsync<RangeOrPlaceholderRange>("textDocument/prepareRename",
            new PrepareRenameParams { TextDocument = Doc, Position = new Position(4, 1) }, CancellationToken.None);

        Assert.Equal(4, range!.Range!.Start.Line);
    }

    [Fact]
    public async Task Completion_ItemEditRanges_ComeOff()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/completion", new JObject
        {
            ["isIncomplete"] = false,
            ["items"] = new JArray(new JObject
            {
                ["label"] = "Find_Player",
                ["textEdit"] = new JObject { ["range"] = Range(7, 7), ["newText"] = "Find_Player" }
            })
        });

        var list = await documents.RequestAsync<CompletionList>("textDocument/completion",
            new CompletionParams { TextDocument = Doc, Position = new Position(4, 3) }, CancellationToken.None);

        Assert.Equal(4, list!.Items.Single().TextEdit!.TextEdit!.Range.Start.Line);
    }

    [Fact]
    public async Task CompletionResolve_IsPassedThrough()
    {
        var (documents, inner) = Open();
        inner.Answer("completionItem/resolve", new JObject { ["label"] = "Find_Player", ["detail"] = "d" });

        var item = await documents.RequestAsync<CompletionItem>("completionItem/resolve",
            new CompletionItem { Label = "Find_Player" }, CancellationToken.None);

        Assert.Equal("d", item!.Detail);
    }

    [Fact]
    public async Task DocumentSymbols_OnGeneratedLinesDropped_OthersMapped()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/documentSymbol", new JArray(new JObject
        {
            ["name"] = "Find_Player", ["kind"] = 12, ["range"] = Range(4, 6), ["selectionRange"] = Range(4, 4)
        }));

        var symbols = await documents.RequestAsync<SymbolInformationOrDocumentSymbolContainer>(
            "textDocument/documentSymbol", new DocumentSymbolParams { TextDocument = Doc }, CancellationToken.None);

        var symbol = Assert.Single(symbols!).DocumentSymbol!;
        Assert.Equal((1, 3), (symbol.Range.Start.Line, symbol.Range.End.Line));
    }

    [Fact]
    public async Task SignatureHelp_PositionGoesOnto()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/signatureHelp", new JObject
        {
            ["signatures"] = new JArray(new JObject { ["label"] = "Find_Player(name: string)" })
        });

        var help = await documents.RequestAsync<SignatureHelp>("textDocument/signatureHelp",
            new SignatureHelpParams { TextDocument = Doc, Position = new Position(4, 13) }, CancellationToken.None);

        Assert.Equal(7, SentParams(inner)["position"]!["line"]!.Value<int>());
        Assert.Equal("Find_Player(name: string)", help!.Signatures.Single().Label);
    }

    [Fact]
    public async Task FoldingRanges_AreMapped_ARangeStartingOnAGeneratedLineDropped()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/foldingRange", new JArray(
            new JObject { ["startLine"] = 4, ["endLine"] = 6 },
            new JObject { ["startLine"] = 1, ["endLine"] = 3 }));

        var ranges = await documents.RequestAsync<Container<FoldingRange>>("textDocument/foldingRange",
            new FoldingRangeRequestParam { TextDocument = Doc }, CancellationToken.None);

        Assert.Equal([(1, 3)], ranges!.Select(r => (r.StartLine, r.EndLine)));
    }

    [Fact]
    public async Task InlayHints_RangeGoesOnto_PositionsComeOff()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/inlayHint", new JArray(
            new JObject { ["position"] = new JObject { ["line"] = 7, ["character"] = 12 }, ["label"] = "name:" },
            new JObject { ["position"] = new JObject { ["line"] = 2, ["character"] = 3 }, ["label"] = "gen" }));

        var hints = await documents.RequestAsync<InlayHintContainer>("textDocument/inlayHint",
            new InlayHintParams { TextDocument = Doc, Range = new Range(0, 0, 4, 20) }, CancellationToken.None);

        Assert.Equal(7, SentParams(inner)["range"]!["end"]!["line"]!.Value<int>());
        Assert.Equal((4, "name:"), (Assert.Single(hints!).Position.Line, Assert.Single(hints!).Label.String));
    }

    [Fact]
    public async Task CodeLenses_AreMapped()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/codeLens", new JArray(new JObject { ["range"] = Range(4, 4) }));

        var lenses = await documents.RequestAsync<CodeLensContainer>("textDocument/codeLens",
            new CodeLensParams { TextDocument = Doc }, CancellationToken.None);

        Assert.Equal(1, Assert.Single(lenses!).Range.Start.Line);
    }

    // The resolve carries the lens back with its range, which goes onto the generated document.
    [Fact]
    public async Task CodeLensResolve_RangeGoesOntoAndComesOff()
    {
        var (documents, inner) = Open();
        inner.Answer("codeLens/resolve", new JObject
        {
            ["range"] = Range(4, 4),
            ["command"] = new JObject
            {
                ["title"] = "2 usages", ["command"] = "emmy.showReferences",
                ["arguments"] = new JArray(Uri, new JObject { ["line"] = 4, ["character"] = 9 },
                    new JArray(new JObject { ["uri"] = Uri, ["range"] = Range(7, 7) }))
            }
        });

        var lens = await documents.RequestAsync<CodeLens>("codeLens/resolve",
            new CodeLens { Range = new Range(1, 0, 1, 3), Data = new JObject { ["uri"] = Uri } },
            CancellationToken.None);

        Assert.Equal(4, SentParams(inner)["range"]!["start"]!["line"]!.Value<int>());
        Assert.Equal(1, lens!.Range.Start.Line);
        Assert.Equal(4, ((JArray)lens.Command!.Arguments![2])[0]!["range"]!["start"]!["line"]!.Value<int>());
    }

    [Fact]
    public async Task SemanticTokens_OnGeneratedLinesDropped_TheRestReEncoded()
    {
        var (documents, inner) = Open();
        // (deltaLine, deltaStart, length, type, modifiers): generated lines 0, 2 and 4.
        inner.Answer("textDocument/semanticTokens/full", new JObject
        {
            ["data"] = new JArray(0, 6, 1, 8, 0, 2, 0, 3, 17, 0, 2, 9, 11, 12, 1)
        });

        var tokens = await documents.RequestAsync<SemanticTokens>("textDocument/semanticTokens/full",
            new SemanticTokensParams { TextDocument = Doc }, CancellationToken.None);

        Assert.Equal([0, 6, 1, 8, 0, 1, 9, 11, 12, 1], tokens!.Data);
    }

    [Fact]
    public void Diagnostics_AreMapped_AndDroppedOnGeneratedLines()
    {
        var (documents, inner) = Open();
        PublishDiagnosticsParams? seen = null;
        documents.DiagnosticsPublished += p => seen = p;

        inner.Publish(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(Uri),
            Diagnostics = new Container<Diagnostic>(
                new Diagnostic { Range = new Range(5, 2, 5, 12), Message = "return-type-mismatch" },
                new Diagnostic { Range = new Range(3, 0, 3, 10), Message = "on a generated line" })
        });

        var diagnostic = Assert.Single(seen!.Diagnostics);
        Assert.Equal(2, diagnostic.Range.Start.Line);
    }

    // A document not opened through the decorator has no map: passed through as it is.
    [Fact]
    public async Task ADocumentWithoutGeneratedLines_IsPassedThrough()
    {
        var (documents, inner) = Open();
        inner.Answer("textDocument/hover", new JObject { ["contents"] = "x", ["range"] = Range(7, 7) });

        var hover = await documents.RequestAsync<Hover>("textDocument/hover",
            new HoverParams
                { TextDocument = new TextDocumentIdentifier(DocumentUri.From(Other)), Position = new Position(7, 1) },
            CancellationToken.None);

        Assert.Equal(7, hover!.Range!.Start.Line);
    }
}