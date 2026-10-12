// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Lua;

// Requests this server has nothing of its own for: the Lua analyzer's answer, passed through. With
// the analyzer off, not installed or silent, each answers with nothing - as before it existed.

internal static class LuaAnalyzerPassThrough
{
    public static readonly TextDocumentSelector Selector = TextDocumentSelector.ForLanguage("lua");

    public static bool IsLua(TextDocumentIdentifier document)
    {
        return document.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class LuaReferencesHandler(ILuaAnalyzer analyzer) : ReferencesHandlerBase
{
    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken ct)
    {
        return LuaAnalyzerPassThrough.IsLua(request.TextDocument)
            ? analyzer.RequestAsync<LocationContainer>("textDocument/references", request, ct)
            : Task.FromResult<LocationContainer?>(null);
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions { DocumentSelector = LuaAnalyzerPassThrough.Selector };
    }
}

public sealed class LuaDocumentSymbolHandler(ILuaAnalyzer analyzer) : DocumentSymbolHandlerBase
{
    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(DocumentSymbolParams request,
        CancellationToken ct)
    {
        return LuaAnalyzerPassThrough.IsLua(request.TextDocument)
            ? analyzer.RequestAsync<SymbolInformationOrDocumentSymbolContainer>("textDocument/documentSymbol", request, ct)
            : Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentSymbolRegistrationOptions { DocumentSelector = LuaAnalyzerPassThrough.Selector };
    }
}

public sealed class LuaSignatureHelpHandler(ILuaAnalyzer analyzer) : SignatureHelpHandlerBase
{
    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken ct)
    {
        return LuaAnalyzerPassThrough.IsLua(request.TextDocument)
            ? analyzer.RequestAsync<SignatureHelp>("textDocument/signatureHelp", request, ct)
            : Task.FromResult<SignatureHelp?>(null);
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities)
    {
        // The analyzer's (measured, 0.25.1).
        return new SignatureHelpRegistrationOptions
        {
            DocumentSelector = LuaAnalyzerPassThrough.Selector,
            TriggerCharacters = new Container<string>("(", ","),
            RetriggerCharacters = new Container<string>("(", ",")
        };
    }
}

public sealed class LuaFoldingRangeHandler(ILuaAnalyzer analyzer) : FoldingRangeHandlerBase
{
    public override Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request, CancellationToken ct)
    {
        return LuaAnalyzerPassThrough.IsLua(request.TextDocument)
            ? analyzer.RequestAsync<Container<FoldingRange>>("textDocument/foldingRange", request, ct)
            : Task.FromResult<Container<FoldingRange>?>(null);
    }

    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability, ClientCapabilities clientCapabilities)
    {
        return new FoldingRangeRegistrationOptions { DocumentSelector = LuaAnalyzerPassThrough.Selector };
    }
}

/// <summary>
///     The analyzer's semantic tokens. Its legend is this server's, declared up front - the editor
///     reads it from the initialize answer, long before the analyzer starts - so token indices pass
///     through unchanged. The analyzer answers full-document requests only (measured, 0.25.1); a
///     range is cut from the full set.
/// </summary>
public sealed class LuaSemanticTokensHandler(ILuaAnalyzer analyzer)
    : ISemanticTokensFullHandler, ISemanticTokensRangeHandler
{
    /// <summary>emmylua_ls 0.25.1's legend, measured from its initialize answer.</summary>
    public static readonly SemanticTokensLegend Legend = new()
    {
        TokenTypes = new Container<SemanticTokenType>(new[]
        {
            "namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter", "variable",
            "property", "enumMember", "event", "function", "method", "macro", "keyword", "modifier", "comment",
            "string", "number", "regexp", "operator", "decorator", "delimiter"
        }.Select(t => new SemanticTokenType(t))),
        TokenModifiers = new Container<SemanticTokenModifier>(new[]
        {
            "declaration", "definition", "readonly", "static", "abstract", "deprecated", "async", "modification",
            "documentation", "defaultLibrary", "operator.logical"
        }.Select(m => new SemanticTokenModifier(m)))
    };

    public Task<SemanticTokens?> Handle(SemanticTokensParams request, CancellationToken ct)
    {
        return LuaAnalyzerPassThrough.IsLua(request.TextDocument)
            ? analyzer.RequestAsync<SemanticTokens>("textDocument/semanticTokens/full", request, ct)
            : Task.FromResult<SemanticTokens?>(null);
    }

    public async Task<SemanticTokens?> Handle(SemanticTokensRangeParams request, CancellationToken ct)
    {
        if (!LuaAnalyzerPassThrough.IsLua(request.TextDocument)) return null;
        var full = await analyzer.RequestAsync<SemanticTokens>("textDocument/semanticTokens/full",
            new SemanticTokensParams { TextDocument = request.TextDocument }, ct);
        return full is null ? null : new SemanticTokens { Data = Slice(full.Data, request.Range) };
    }

    public SemanticTokensRegistrationOptions GetRegistrationOptions(SemanticTokensCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = LuaAnalyzerPassThrough.Selector,
            Legend = Legend,
            Full = new BooleanOr<SemanticTokensCapabilityRequestFull>(true),
            Range = true
        };
    }

    // Tokens are (deltaLine, deltaStart, length, type, modifiers), each relative to the one before.
    // Kept: those starting on a line inside the range; re-encoded relative to each other, the first
    // relative to the start of the document as the protocol has it.
    private static ImmutableArray<int> Slice(ImmutableArray<int> data, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
    {
        var result = ImmutableArray.CreateBuilder<int>();
        int line = 0, column = 0, lastLine = 0, lastColumn = 0;
        for (var i = 0; i + 4 < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            if (line < range.Start.Line || line > range.End.Line) continue;
            if (line == range.End.Line && column >= range.End.Character) continue;

            result.Add(line - lastLine);
            result.Add(line == lastLine ? column - lastColumn : column);
            result.Add(data[i + 2]);
            result.Add(data[i + 3]);
            result.Add(data[i + 4]);
            (lastLine, lastColumn) = (line, column);
        }

        return result.ToImmutable();
    }
}
