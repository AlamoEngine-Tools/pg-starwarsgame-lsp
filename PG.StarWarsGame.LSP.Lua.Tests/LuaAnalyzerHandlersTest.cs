// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

/// <summary>Requests this server has nothing of its own for: the analyzer's answer, passed through.</summary>
public sealed class LuaAnalyzerHandlersTest
{
    // ── the sandbox (#249) ───────────────────────────────────────────────────

    private const string Interdictor = "file:///mod/data/scripts/gameobject/interdictor.lua";
    private const string Vader = "file:///mod/data/scripts/gameobject/darthvader.lua";
    private const string StateMachine = "file:///mod/data/scripts/library/pgstatemachine.lua";

    private const string Stub = "file:///C:/schema/lua/pgstatemachine.d.lua";
    private static readonly TextDocumentIdentifier Lua = new(DocumentUri.From("file:///script.lua"));
    private static readonly TextDocumentIdentifier Xml = new(DocumentUri.From("file:///units.xml"));
    private static readonly LuaStubLocation s_stubs = new() { Directory = "C:/schema/lua" };

    [Fact]
    public async Task References_AreTheAnalyzers()
    {
        var theirs = new LocationContainer(new Location { Uri = Lua.Uri, Range = new Range(3, 1, 3, 4) });
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/references", theirs);

        var result = await new LuaReferencesHandler(analyzer, FixedSandbox.Empty(), new LuaStubLocation()).Handle(
            new ReferenceParams
            {
                TextDocument = Lua, Position = new Position(0, 0), Context = new ReferenceContext()
            }, CancellationToken.None);

        Assert.Equal(3, Assert.Single(result!).Range.Start.Line);
    }

    /// <summary>Two object scripts, each requiring the state machine: two states.</summary>
    private static FixedSandbox TwoObjectScripts()
    {
        var documents = new (string Uri, string[] Requires)[]
            {
                (Interdictor, ["PGStateMachine"]), (Vader, ["PGStateMachine"]), (StateMachine, [])
            }
            .ToImmutableDictionary(s => s.Uri, s => new DocumentIndex(s.Uri, 1, [], [], [.. s.Requires]));
        return new FixedSandbox(LuaSandbox.Build(documents,
            new FileHelper(new MockFileSystem())));
    }

    private static Location At(string uri, int line)
    {
        return new Location { Uri = DocumentUri.From(uri), Range = new Range(line, 9, line, 20) };
    }

    // Measured on the EaW scripts: from Interdictor.lua's Definitions the analyzer answered 216
    // locations in 207 files - every other script's Definitions among them.
    [Fact]
    public async Task References_InAnotherScriptsState_AreDropped()
    {
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/references",
            new LocationContainer(At(Interdictor, 44), At(Vader, 70), At(StateMachine, 172)));

        var result = await new LuaReferencesHandler(analyzer, TwoObjectScripts(), new LuaStubLocation()).Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(DocumentUri.From(Interdictor)),
                Position = new Position(44, 12),
                Context = new ReferenceContext { IncludeDeclaration = true }
            }, CancellationToken.None);

        Assert.Equal([Interdictor, StateMachine], result!.Select(l => l.Uri.ToString()));
    }

    [Fact]
    public async Task ACodeLens_CountsOnlyTheScriptsOwnState()
    {
        var locations = JArray.FromObject(new[] { At(Interdictor, 44), At(Vader, 70), At(StateMachine, 172) },
            JsonSerializer.Create(new JsonSerializerSettings
                { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
        var resolved = new CodeLens
        {
            Range = new Range(40, 9, 40, 20),
            Command = new Command
            {
                Name = "emmy.showReferences", Title = "3 usages",
                Arguments = new JArray(Interdictor, JObject.FromObject(new { line = 40, character = 9 }), locations)
            }
        };
        var analyzer = new ScriptedLuaAnalyzer().Answer("codeLens/resolve", resolved);

        var result = await new LuaCodeLensHandler(analyzer, TwoObjectScripts(), new LuaStubLocation())
            .Handle(new CodeLens { Range = new Range(40, 9, 40, 20) }, CancellationToken.None);

        Assert.Equal("2 usages", result.Command!.Title);
        Assert.Equal(2, ((JArray)result.Command.Arguments![2]).Count);
    }

    // The analyzer lists the declaration the lens sits on among its usages (measured, 0.25.1).
    [Fact]
    public async Task ACodeLens_DoesNotCountItsOwnDeclaration()
    {
        var locations = JArray.FromObject(new[] { At(Interdictor, 30), At(Interdictor, 44), At(Interdictor, 50) },
            JsonSerializer.Create(new JsonSerializerSettings
                { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
        var analyzer = new ScriptedLuaAnalyzer().Answer("codeLens/resolve", new CodeLens
        {
            Range = new Range(44, 9, 44, 20),
            Command = new Command
            {
                Name = "emmy.showReferences", Title = "3 usages",
                Arguments = new JArray(Interdictor, JObject.FromObject(new { line = 44, character = 9 }), locations)
            }
        });

        var result = await new LuaCodeLensHandler(analyzer, TwoObjectScripts(), new LuaStubLocation())
            .Handle(new CodeLens { Range = new Range(44, 9, 44, 20) }, CancellationToken.None);

        Assert.Equal("2 usages", result.Command!.Title);
        Assert.Equal([30, 50],
            ((JArray)result.Command.Arguments![2]).Select(l => l["range"]!["start"]!["line"]!.Value<int>()));
    }

    // The stubs are the analyzer's library: their declarations describe a function, they never use it.
    [Fact]
    public async Task References_InTheStubs_AreDropped()
    {
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/references",
            new LocationContainer(At(Interdictor, 44), At(Stub, 12), At(StateMachine, 172)));

        var result = await new LuaReferencesHandler(analyzer, TwoObjectScripts(), s_stubs).Handle(new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Interdictor)), Position = new Position(44, 12),
            Context = new ReferenceContext { IncludeDeclaration = true }
        }, CancellationToken.None);

        Assert.Equal([Interdictor, StateMachine], result!.Select(l => l.Uri.ToString()));
    }

    [Fact]
    public async Task ACodeLens_DoesNotCountTheStubs()
    {
        var locations = JArray.FromObject(new[] { At(Interdictor, 44), At(Stub, 12) },
            JsonSerializer.Create(new JsonSerializerSettings
                { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
        var analyzer = new ScriptedLuaAnalyzer().Answer("codeLens/resolve", new CodeLens
        {
            Range = new Range(40, 9, 40, 20),
            Command = new Command
            {
                Name = "emmy.showReferences", Title = "2 usages",
                Arguments = new JArray(Interdictor, JObject.FromObject(new { line = 40, character = 9 }), locations)
            }
        });

        var result = await new LuaCodeLensHandler(analyzer, TwoObjectScripts(), s_stubs)
            .Handle(new CodeLens { Range = new Range(40, 9, 40, 20) }, CancellationToken.None);

        Assert.Equal("1 usage", result.Command!.Title);
    }

    [Fact]
    public async Task DocumentSymbols_AreTheAnalyzers()
    {
        var theirs = new SymbolInformationOrDocumentSymbolContainer(new SymbolInformationOrDocumentSymbol(
            new DocumentSymbol
            {
                Name = "Story_Mode_Service", Kind = SymbolKind.Function, Range = new Range(0, 0, 4, 3),
                SelectionRange = new Range(0, 9, 0, 27)
            }));
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/documentSymbol", theirs);

        var result = await new LuaDocumentSymbolHandler(analyzer)
            .Handle(new DocumentSymbolParams { TextDocument = Lua }, CancellationToken.None);

        Assert.Equal("Story_Mode_Service", Assert.Single(result!).DocumentSymbol!.Name);
    }

    [Fact]
    public async Task SignatureHelp_IsTheAnalyzers()
    {
        var theirs = new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(new SignatureInformation
                { Label = "Find_Player(name: string)" })
        };
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/signatureHelp", theirs);

        var result = await new LuaSignatureHelpHandler(analyzer)
            .Handle(new SignatureHelpParams { TextDocument = Lua, Position = new Position(0, 12) },
                CancellationToken.None);

        Assert.Equal("Find_Player(name: string)", Assert.Single(result!.Signatures).Label);
    }

    [Fact]
    public async Task Folding_IsTheAnalyzers()
    {
        var theirs = new Container<FoldingRange>(new FoldingRange { StartLine = 2, EndLine = 9 });
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/foldingRange", theirs);

        var result = await new LuaFoldingRangeHandler(analyzer)
            .Handle(new FoldingRangeRequestParam { TextDocument = Lua }, CancellationToken.None);

        Assert.Equal(9, Assert.Single(result!).EndLine);
    }

    [Fact]
    public async Task InlayHints_AreTheAnalyzers()
    {
        var theirs = new InlayHintContainer(new InlayHint
        {
            Position = new Position(4, 22), Label = new StringOrInlayHintLabelParts("faction_name:")
        });
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/inlayHint", theirs);

        var result = await new LuaInlayHintHandler(analyzer).Handle(new InlayHintParams
        {
            TextDocument = Lua, Range = new Range(0, 0, 9, 0)
        }, CancellationToken.None);

        Assert.Equal(22, Assert.Single(result!).Position.Character);
    }

    [Fact]
    public async Task CodeLenses_AreTheAnalyzers()
    {
        var theirs = new CodeLensContainer(new CodeLens { Range = new Range(2, 0, 2, 0) });
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/codeLens", theirs);

        var result = await new LuaCodeLensHandler(analyzer, FixedSandbox.Empty(), new LuaStubLocation())
            .Handle(new CodeLensParams { TextDocument = Lua }, CancellationToken.None);

        Assert.Equal(2, Assert.Single(result!).Range.Start.Line);
    }

    // The analyzer sends its lenses without a command and fills it on resolve.
    [Fact]
    public async Task ACodeLens_IsResolvedByTheAnalyzer()
    {
        var resolved = new CodeLens { Range = new Range(2, 0, 2, 0), Command = new Command { Title = "3 usages" } };
        var analyzer = new ScriptedLuaAnalyzer().Answer("codeLens/resolve", resolved);

        var result = await new LuaCodeLensHandler(analyzer, FixedSandbox.Empty(), new LuaStubLocation())
            .Handle(new CodeLens { Range = new Range(2, 0, 2, 0) }, CancellationToken.None);

        Assert.Equal("3 usages", result.Command!.Title);
    }

    // Measured (0.25.1): a lens whose data carries any key besides its own comes back unresolved.
    [Fact]
    public async Task ACodeLens_IsSentToResolveWithoutTheRoutingKey()
    {
        var analyzer = new ScriptedLuaAnalyzer();
        var lens = new CodeLens
        {
            Range = new Range(2, 0, 2, 0),
            Data = JObject.Parse("""{"DeclId":"373|15","$$__handler_id__$$":"8f41a571"}""")
        };

        await new LuaCodeLensHandler(analyzer, FixedSandbox.Empty(), new LuaStubLocation()).Handle(lens,
            CancellationToken.None);

        var sent = (CodeLens)Assert.Single(analyzer.Requests).Parameters;
        Assert.Equal("""{"DeclId":"373|15"}""", sent.Data!.ToString(Formatting.None));
    }

    // The analyzer names the editor's command, or its own extension's under --editor vscode (both
    // measured, 0.25.1); ours takes the arguments as the protocol sends them, in the same order.
    [Theory]
    [InlineData("editor.action.showReferences")]
    [InlineData("emmy.showReferences")]
    public async Task AResolvedShowReferencesCommand_IsOurs(string theirs)
    {
        var resolved = new CodeLens
        {
            Range = new Range(2, 0, 2, 0),
            Command = new Command
            {
                Name = theirs, Title = "2 usages",
                Arguments = JArray.Parse("""["file:///a.lua"]""")
            }
        };
        var analyzer = new ScriptedLuaAnalyzer().Answer("codeLens/resolve", resolved);

        var result = await new LuaCodeLensHandler(analyzer, FixedSandbox.Empty(), new LuaStubLocation())
            .Handle(new CodeLens { Range = new Range(2, 0, 2, 0) }, CancellationToken.None);

        Assert.Equal("aet-eaw-edit.lsp.showReferences", result.Command!.Name);
        Assert.Equal("file:///a.lua", result.Command.Arguments![0].ToString());
    }

    [Fact]
    public async Task ACodeLens_TheAnalyzerSilent_ComesBackAsSent()
    {
        var lens = new CodeLens { Range = new Range(2, 0, 2, 0) };

        var result =
            await new LuaCodeLensHandler(new ScriptedLuaAnalyzer(), FixedSandbox.Empty(), new LuaStubLocation()).Handle(
                lens, CancellationToken.None);

        Assert.Same(lens, result);
    }

    [Fact]
    public async Task ANonLuaDocument_IsNeverSentToTheAnalyzer()
    {
        var analyzer = new ScriptedLuaAnalyzer();

        await new LuaFoldingRangeHandler(analyzer).Handle(new FoldingRangeRequestParam { TextDocument = Xml },
            CancellationToken.None);

        Assert.Empty(analyzer.Requests);
    }

    [Fact]
    public async Task SemanticTokensFull_AreTheAnalyzers()
    {
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/semanticTokens/full",
            new SemanticTokens { Data = ImmutableArray.Create(0, 0, 5, 15, 0) });

        var result = await new LuaSemanticTokensHandler(analyzer)
            .Handle(new SemanticTokensParams { TextDocument = Lua }, CancellationToken.None);

        Assert.Equal([0, 0, 5, 15, 0], result!.Data.ToArray());
    }

    // The analyzer answers only full-document requests (measured, 0.25.1); a range is cut from it.
    [Fact]
    public async Task SemanticTokensRange_IsCutFromTheFullSet_AndReEncoded()
    {
        // line 0 col 0 len 5; line 2 col 4 len 3; line 2 col 10 len 2; line 5 col 0 len 1
        var full = ImmutableArray.Create(0, 0, 5, 15, 0, 2, 4, 3, 8, 0, 0, 6, 2, 12, 1, 3, 0, 1, 15, 0);
        var analyzer =
            new ScriptedLuaAnalyzer().Answer("textDocument/semanticTokens/full", new SemanticTokens { Data = full });

        var result = await new LuaSemanticTokensHandler(analyzer).Handle(new SemanticTokensRangeParams
        {
            TextDocument = Lua, Range = new Range(1, 0, 3, 0)
        }, CancellationToken.None);

        // the two line-2 tokens, the first now relative to the start of the document
        Assert.Equal([2, 4, 3, 8, 0, 0, 6, 2, 12, 1], result!.Data.ToArray());
    }

    [Fact]
    public void TheLegend_IsTheAnalyzers_SoIndicesPassThroughUnchanged()
    {
        Assert.Equal(24, LuaSemanticTokensHandler.Legend.TokenTypes.Count());
        Assert.Equal("operator.logical", LuaSemanticTokensHandler.Legend.TokenModifiers.Last().ToString());
    }
}