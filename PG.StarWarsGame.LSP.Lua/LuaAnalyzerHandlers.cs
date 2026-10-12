// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Lua.Analysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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

/// <summary>
///     The analyzer's references, less those in a state the requesting script does not share: it
///     sees one namespace, the game one per script (#249).
/// </summary>
public sealed class LuaReferencesHandler(ILuaAnalyzer analyzer, ILuaSandboxProvider? sandbox = null)
    : ReferencesHandlerBase
{
    public override async Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken ct)
    {
        if (!LuaAnalyzerPassThrough.IsLua(request.TextDocument)) return null;
        var theirs = await analyzer.RequestAsync<LocationContainer>("textDocument/references", request, ct);
        if (theirs is null || sandbox is null) return theirs;

        var current = sandbox.Current;
        var from = request.TextDocument.Uri.ToString();
        return new LocationContainer(theirs.Where(l => current.ShareAState(from, l.Uri.ToString())));
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
            ? analyzer.RequestAsync<SymbolInformationOrDocumentSymbolContainer>("textDocument/documentSymbol", request,
                ct)
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

public sealed class LuaInlayHintHandler(ILuaAnalyzer analyzer) : InlayHintsHandlerBase
{
    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken ct)
    {
        return LuaAnalyzerPassThrough.IsLua(request.TextDocument)
            ? analyzer.RequestAsync<InlayHintContainer>("textDocument/inlayHint", request, ct)
            : Task.FromResult<InlayHintContainer?>(null);
    }

    // The analyzer resolves no hint (measured, 0.25.1); registered without resolve, so never asked.
    public override Task<InlayHint> Handle(InlayHint request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(
        InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
    {
        return new InlayHintRegistrationOptions
            { DocumentSelector = LuaAnalyzerPassThrough.Selector, ResolveProvider = false };
    }
}

/// <summary>The analyzer's lenses: sent without a command, which its resolve fills (measured, 0.25.1).</summary>
public sealed class LuaCodeLensHandler(ILuaAnalyzer analyzer, ILuaSandboxProvider? sandbox = null)
    : CodeLensHandlerBase
{
    // The router's own key rides in the lens data, and with it present the analyzer resolves
    // nothing (measured, 0.25.1), so the analyzer is sent its data alone.
    private const string RoutingKey = "$$__handler_id__$$";

    public override Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken ct)
    {
        return LuaAnalyzerPassThrough.IsLua(request.TextDocument)
            ? analyzer.RequestAsync<CodeLensContainer>("textDocument/codeLens", request, ct)
            : Task.FromResult<CodeLensContainer?>(null);
    }

    public override async Task<CodeLens> Handle(CodeLens request, CancellationToken ct)
    {
        var data = request.Data is JObject withKey ? (JObject)withKey.DeepClone() : null;
        data?.Remove(RoutingKey);
        var resolved = await analyzer.RequestAsync<CodeLens>("codeLens/resolve", request with { Data = data }, ct);
        if (resolved?.Command is not { Name: "editor.action.showReferences" or "emmy.showReferences" } command)
            return resolved ?? request;

        // The editor's command takes typed arguments the protocol cannot carry, and the analyzer's
        // extension is not installed; ours takes them as sent - document, position, locations.
        return resolved with { Command = InOwnState(command) with { Name = "aet-eaw-edit.lsp.showReferences" } };
    }

    // The arguments are (document, position, locations), measured; the count in the title is the
    // number of locations. Recounted over the document's own state (#249).
    private Command InOwnState(Command command)
    {
        if (sandbox is null || command.Arguments is not { Count: 3 } arguments
                            || arguments[0] is not { Type: JTokenType.String } document
                            || arguments[2] is not JArray locations)
            return command;

        var current = sandbox.Current;
        var kept = new JArray(locations.Where(l =>
            l["uri"]?.Value<string>() is not { } uri || current.ShareAState(document.Value<string>()!, uri)));
        var title = kept.Count == 1 ? "1 usage" : $"{kept.Count} usages";
        return command with { Title = title, Arguments = new JArray(document, arguments[1], kept) };
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(
        CodeLensCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CodeLensRegistrationOptions
            { DocumentSelector = LuaAnalyzerPassThrough.Selector, ResolveProvider = true };
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

    public async Task<SemanticTokens?> Handle(SemanticTokensRangeParams request, CancellationToken ct)
    {
        if (!LuaAnalyzerPassThrough.IsLua(request.TextDocument)) return null;
        var full = await analyzer.RequestAsync<SemanticTokens>("textDocument/semanticTokens/full",
            new SemanticTokensParams { TextDocument = request.TextDocument }, ct);
        return full is null ? null : new SemanticTokens { Data = Slice(full.Data, request.Range) };
    }

    // Tokens are (deltaLine, deltaStart, length, type, modifiers), each relative to the one before.
    // Kept: those starting on a line inside the range; re-encoded relative to each other, the first
    // relative to the start of the document as the protocol has it.
    private static ImmutableArray<int> Slice(ImmutableArray<int> data,
        Range range)
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