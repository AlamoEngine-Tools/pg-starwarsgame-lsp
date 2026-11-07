// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Completion;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaCompletionHandler : CompletionHandlerBase
{
    private readonly ILuaAnalyzer _analyzer;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaCompletionHandler> _logger;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    // The analyzer's items join ours; when none runs, it answers nothing.
    public LuaCompletionHandler(
        IGameIndexService indexService,
        ILuaParseCache parseCache,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaCompletionHandler> logger,
        ILuaAnalyzer analyzer)
    {
        _indexService = indexService;
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
        _analyzer = analyzer;
    }

    public override async Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var (ours, alone) = HandleOurs(request);
        if (alone || !request.TextDocument.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return ours;

        var theirs = await _analyzer.RequestAsync<CompletionList>("textDocument/completion", request, ct);
        return LuaAnalyzerAnswers.MergeCompletion(ours, theirs);
    }

    // Alone: an argument naming a game reference, before its quotes - a string, so none of the
    // analyzer's functions, keywords or other variables belong there.
    private (CompletionList List, bool Alone) HandleOurs(CompletionParams request)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return (new CompletionList(), false);

        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return (new CompletionList(), false);

        var ctx = LuaCompletionContextClassifier.Classify(parsed.Text, request.Position.Line,
            request.Position.Character);
        if (ctx is { Quoted: false })
        {
            var lines = parsed.Tree.GetText().Lines;
            if (request.Position.Line >= lines.Count) return (new CompletionList(), false);
            var position = Math.Min(lines[request.Position.Line].Start + request.Position.Character,
                lines[request.Position.Line].End);
            return BuildUnquoted(ctx, parsed.Tree, position, _indexService.Current) is { } unquoted
                ? (unquoted, true)
                : (new CompletionList(), false);
        }

        return (BuildCompletions(ctx, uri, _indexService.Current), false);
    }

    public override async Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
    {
        // Only the analyzer's own items are its to resolve; ours are complete as sent.
        if (LuaAnalyzerAnswers.AnalyzerItem(request) is not { } original) return request;
        return await _analyzer.RequestAsync<CompletionItem>("completionItem/resolve", original, ct) ?? request;
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lua"),
            // Ours, then the analyzer's (measured, 0.25.1) less the space: it would open the list
            // after every word.
            TriggerCharacters = new Container<string>("\"", "'", ".", ":", "(", "[", "@", "\\", "/", "|", "#", "?"),
            ResolveProvider = true
        };
    }

    private CompletionList BuildCompletions(ArgumentContext? ctx, string uri, GameIndex index)
    {
        switch (ctx)
        {
            case { FunctionName: var fn }
                when string.Equals(fn, "require", StringComparison.OrdinalIgnoreCase):
                return new CompletionList(BuildRequireCompletions(uri, index));

            case { FunctionName: "Set_Next_State", ParamIndex: 0 }:
                return new CompletionList(BuildStateCompletions(uri, index));

            case { FunctionName: var fn, ParamIndex: var param }:
            {
                if (ReferenceAt(fn, param) is not { } refEntry) return new CompletionList();
                return new CompletionList(LiteralCompletions(refEntry).Concat(refEntry.Kind switch
                {
                    ReferenceKind.XmlObject => BuildXmlRefCompletions(index, refEntry.ExpectedTypeName),
                    ReferenceKind.LocalisationKey => BuildTextKeyCompletions(index),
                    _ => []
                }));
            }

            // Identifiers, members and keywords are the analyzer's (#155).
            default:
                return new CompletionList();
        }
    }

    // The reference a function's parameter takes, from its @aetref.
    private XmlRefEntry? ReferenceAt(string function, int param)
    {
        foreach (var r in _schemaProvider.GetXmlRefs(function))
            if (r.ParamIndex == param)
            {
                _logger.LogDebug("Lua completion: {Function} param {Index} -> {Kind} {Type}",
                    function, param, r.Kind, r.ExpectedTypeName ?? "*");
                return r;
            }

        return null;
    }

    // Before the quotes: every value the argument takes, written as the string it is, and the
    // variables only ever holding one. Null where the argument takes no reference.
    private CompletionList? BuildUnquoted(ArgumentContext ctx, SyntaxTree tree, int position,
        GameIndex index)
    {
        if (ReferenceAt(ctx.FunctionName, ctx.ParamIndex) is not { } refEntry ||
            refEntry.Kind is not (ReferenceKind.XmlObject or ReferenceKind.LocalisationKey))
            return null;

        var values = LiteralCompletions(refEntry).Concat(refEntry.Kind == ReferenceKind.XmlObject
            ? BuildXmlRefCompletions(index, refEntry.ExpectedTypeName)
            : BuildTextKeyCompletions(index)).ToList();
        var taken = values.Select(v => v.Label).ToHashSet(StringComparer.Ordinal);

        var quoted = values.Select(v => v with
        {
            Label = $"\"{v.Label}\"", InsertText = $"\"{v.Label}\"", FilterText = v.Label
        });
        var variables = LuaStringVariables.Holding(tree, position, taken.Contains).Select(name =>
            new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Variable,
                Detail = refEntry.ExpectedTypeName ?? refEntry.Kind.ToString()
            });
        return new CompletionList(variables.Concat(quoted));
    }

    // The literals the parameter's declared type names, such as "local": values the engine takes
    // as they are.
    private static IEnumerable<CompletionItem> LiteralCompletions(XmlRefEntry refEntry)
    {
        return (refEntry.Literals ?? []).Select(literal => new CompletionItem
        {
            Label = literal,
            Kind = CompletionItemKind.EnumMember
        });
    }

    // The states the script registers, where its state machine library keeps the game's contract.
    private IEnumerable<CompletionItem> BuildStateCompletions(string uri, GameIndex index)
    {
        if (_parseCache.GetOrParse(uri) is not { } parsed) return [];
        var contract = LuaStateMachineContract.For(uri, index.Documents, _parseCache, _fileHelper);
        if (!contract.StateMachine) return [];

        var states = LuaStateMachine.Read(parsed.Tree.GetRoot()).Registrations
            .Where(r => r.Source == LuaStateSource.DEFINE_STATE || contract.StoryMode)
            .Select(r => (r.Name, Detail: (r.Value as IdentifierNameSyntax)?.Name))
            .ToList();
        if (contract.StoryMode) states.Insert(0, (LuaStateMachine.StoryInitState, null));
        return states.DistinctBy(s => s.Name).Select(s => new CompletionItem
        {
            Label = s.Name,
            Kind = CompletionItemKind.EnumMember,
            Detail = s.Detail is null ? "state" : $"state - handler {s.Detail}"
        });
    }

    private IEnumerable<CompletionItem> BuildRequireCompletions(string currentUri, GameIndex index)
    {
        var fileHelper = _fileHelper;
        var sharedUris = LuaFileClassifier.GetSharedUris(index.Documents, fileHelper);

        foreach (var uri in index.Documents.Keys)
        {
            if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(uri, currentUri, StringComparison.OrdinalIgnoreCase)) continue;

            var normalized = GamePaths.Forward(uri);
            var slashIdx = normalized.LastIndexOf('/');
            var filename = slashIdx >= 0 ? normalized[(slashIdx + 1)..] : normalized;
            if (!filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
            var moduleName = filename[..^4];

            // Determine tier and sort order
            string sortText;
            string? detail;
            if (LuaFileClassifier.IsLibraryUri(uri))
            {
                sortText = "0_" + moduleName;
                detail = "library";
            }
            else if (sharedUris.Contains(uri))
            {
                sortText = "1_" + moduleName;
                detail = "dependency";
            }
            else
            {
                // Standalone - omit from require completions
                continue;
            }

            yield return new CompletionItem
            {
                Label = moduleName,
                Kind = CompletionItemKind.Module,
                SortText = sortText,
                Detail = detail
            };
        }
    }

    private static IEnumerable<CompletionItem> BuildTextKeyCompletions(GameIndex index)
    {
        return index.Localisation.Keys.Select(key => new CompletionItem
        {
            Label = key,
            Detail = index.Localisation.GetValue(key),
            Kind = CompletionItemKind.Text
        });
    }

    private static IEnumerable<CompletionItem> BuildXmlRefCompletions(GameIndex index, string? expectedTypeName)
    {
        foreach (var (id, symbols) in index.WorkspaceDefinitions)
        foreach (var sym in symbols)
        {
            if (sym.Kind != GameSymbolKind.XmlObject) continue;
            if (expectedTypeName is not null &&
                !string.Equals(sym.TypeName, expectedTypeName, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new CompletionItem
            {
                Label = id,
                Detail = sym.TypeName,
                Kind = CompletionItemKind.Reference
            };
            break;
        }

        foreach (var (id, sym) in index.ShippedSymbols)
        {
            if (index.WorkspaceDefinitions.ContainsKey(id)) continue;
            if (expectedTypeName is not null &&
                !string.Equals(sym.TypeName, expectedTypeName, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new CompletionItem
            {
                Label = id,
                Detail = sym.TypeName,
                Kind = CompletionItemKind.Reference
            };
        }
    }
}