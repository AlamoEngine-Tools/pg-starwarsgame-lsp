// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua.Tests;

/// <summary>Requests this server has nothing of its own for: the analyzer's answer, passed through.</summary>
public sealed class LuaAnalyzerHandlersTest
{
    private static readonly TextDocumentIdentifier Lua = new(DocumentUri.From("file:///script.lua"));
    private static readonly TextDocumentIdentifier Xml = new(DocumentUri.From("file:///units.xml"));

    [Fact]
    public async Task References_AreTheAnalyzers()
    {
        var theirs = new LocationContainer(new Location { Uri = Lua.Uri, Range = new Range(3, 1, 3, 4) });
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/references", theirs);

        var result = await new LuaReferencesHandler(analyzer).Handle(new ReferenceParams
        {
            TextDocument = Lua, Position = new Position(0, 0), Context = new ReferenceContext()
        }, CancellationToken.None);

        Assert.Equal(3, Assert.Single(result!).Range.Start.Line);
    }

    [Fact]
    public async Task DocumentSymbols_AreTheAnalyzers()
    {
        var theirs = new SymbolInformationOrDocumentSymbolContainer(new SymbolInformationOrDocumentSymbol(
            new DocumentSymbol { Name = "Story_Mode_Service", Kind = SymbolKind.Function, Range = new Range(0, 0, 4, 3), SelectionRange = new Range(0, 9, 0, 27) }));
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
            Signatures = new Container<SignatureInformation>(new SignatureInformation { Label = "Find_Player(name: string)" })
        };
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/signatureHelp", theirs);

        var result = await new LuaSignatureHelpHandler(analyzer)
            .Handle(new SignatureHelpParams { TextDocument = Lua, Position = new Position(0, 12) }, CancellationToken.None);

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
    public async Task ANonLuaDocument_IsNeverSentToTheAnalyzer()
    {
        var analyzer = new ScriptedLuaAnalyzer();

        await new LuaFoldingRangeHandler(analyzer).Handle(new FoldingRangeRequestParam { TextDocument = Xml }, CancellationToken.None);

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
        var analyzer = new ScriptedLuaAnalyzer().Answer("textDocument/semanticTokens/full", new SemanticTokens { Data = full });

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
