// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Completion;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaCompletionHandler : CompletionHandlerBase
{
    private readonly ILuaAnalyzer? _analyzer;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaCompletionHandler> _logger;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    public LuaCompletionHandler(
        IGameIndexService indexService,
        ILuaParseCache parseCache,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaCompletionHandler> logger,
        // Optional: with the analyzer running, its items join ours.
        ILuaAnalyzer? analyzer = null)
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
        var ours = await HandleOurs(request);
        if (_analyzer is null ||
            !request.TextDocument.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return ours;

        var theirs = await _analyzer.RequestAsync<CompletionList>("textDocument/completion", request, ct);
        return LuaAnalyzerAnswers.MergeCompletion(ours, theirs);
    }

    private Task<CompletionList> HandleOurs(CompletionParams request)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new CompletionList());

        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return Task.FromResult(new CompletionList());

        var ctx = LuaCompletionContextClassifier.Classify(parsed.Text, request.Position.Line,
            request.Position.Character);
        return Task.FromResult(BuildCompletions(ctx, uri, _indexService.Current));
    }

    public override async Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
    {
        // Only the analyzer's own items are its to resolve; ours are complete as sent.
        if (_analyzer is null || LuaAnalyzerAnswers.AnalyzerItem(request) is not { } original) return request;
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

    private CompletionList BuildCompletions(StringArgContext? ctx, string uri, GameIndex index)
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
                var xmlRefs = _schemaProvider.GetXmlRefs(fn);
                if (xmlRefs.Count == 0) return new CompletionList();

                XmlRefEntry? refEntry = null;
                foreach (var r in xmlRefs)
                    if (r.ParamIndex == param)
                    {
                        refEntry = r;
                        break;
                    }

                if (refEntry is null) return new CompletionList();

                _logger.LogDebug("Lua completion: {Function} param {Index} -> {Kind} {Type}",
                    fn, param, refEntry.Value.Kind, refEntry.Value.ExpectedTypeName ?? "*");

                return refEntry.Value.Kind switch
                {
                    ReferenceKind.XmlObject => new CompletionList(
                        BuildXmlRefCompletions(index, refEntry.Value.ExpectedTypeName)),
                    ReferenceKind.LocalisationKey => new CompletionList(BuildTextKeyCompletions(index)),
                    _ => new CompletionList()
                };
            }

            // Identifiers, members and keywords are the analyzer's (#155).
            default:
                return new CompletionList();
        }
    }

    // The states the script registers, where its state machine library keeps the game's contract.
    private IEnumerable<CompletionItem> BuildStateCompletions(string uri, GameIndex index)
    {
        if (_parseCache.GetOrParse(uri) is not { } parsed) return [];
        var contract = LuaStateMachineContract.For(uri, index.Documents, _parseCache, _fileHelper);
        if (!contract.StateMachine) return [];

        var states = LuaStateMachine.Read(parsed.Tree.GetRoot()).Registrations
            .Where(r => r.Source == LuaStateSource.DEFINE_STATE || contract.StoryMode)
            .Select(r => (r.Name, Detail: (r.Value as Loretta.CodeAnalysis.Lua.Syntax.IdentifierNameSyntax)?.Name))
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

            var normalized = uri.Replace('\\', '/');
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

        foreach (var (id, sym) in index.Baseline.Symbols)
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