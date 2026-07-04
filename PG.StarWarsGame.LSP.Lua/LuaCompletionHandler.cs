// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Completion;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaCompletionHandler : CompletionHandlerBase
{
    private readonly ILuaAnnotationRepository _annotationRepository;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaCompletionHandler> _logger;
    private readonly ILuaApiSchemaProvider _schemaProvider;
    private readonly ILuaParseCache _parseCache;

    public LuaCompletionHandler(
        IGameIndexService indexService,
        ILuaParseCache parseCache,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILuaAnnotationRepository annotationRepository,
        ILogger<LuaCompletionHandler> logger)
    {
        _indexService = indexService;
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _annotationRepository = annotationRepository;
        _logger = logger;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new CompletionList());

        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return Task.FromResult(new CompletionList());

        var line = request.Position.Line;
        var character = request.Position.Character;
        var index = _indexService.Current;

        var ctx = LuaCompletionContextClassifier.Classify(parsed.Text, line, character);
        return Task.FromResult(BuildCompletions(ctx, uri, parsed, line, character, index));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lua"),
            TriggerCharacters = new Container<string>("\"", "'", ".", ":"),
            ResolveProvider = false
        };
    }

    private CompletionList BuildCompletions(
        LuaCompletionContext? ctx,
        string uri, ParsedLuaDocument parsed, int line, int character,
        GameIndex index)
    {
        switch (ctx)
        {
            case StringArgContext { FunctionName: var fn, ParamIndex: var param }
                when string.Equals(fn, "require", StringComparison.OrdinalIgnoreCase):
                return new CompletionList(BuildRequireCompletions(uri, index));

            case StringArgContext { FunctionName: var fn, ParamIndex: var param }:
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

                _logger.LogDebug("Lua completion: {Function} param {Index} → type {Type}",
                    fn, param, refEntry.Value.ExpectedTypeName ?? "*");

                return new CompletionList(BuildXmlRefCompletions(index, refEntry.Value.ExpectedTypeName));
            }

            case IdentifierContext { AtStatementStart: var atStart }:
            {
                var scope = LuaLocalScopeCollector.CollectAt(
                    parsed.Tree, parsed.Text, line, character, uri, index, _schemaProvider, _fileHelper);
                var items = LuaIdentifierCompletionProvider.Provide(scope).ToList();
                if (atStart)
                    items.AddRange(LuaSnippetCompletionProvider.Snippets);
                return new CompletionList(items);
            }

            case MemberAccessContext memberCtx:
            {
                var scope = LuaLocalScopeCollector.CollectAt(
                    parsed.Tree, parsed.Text, line, character, uri, index, _schemaProvider, _fileHelper);
                return new CompletionList(LuaMemberCompletionProvider.Provide(
                    memberCtx, scope, _schemaProvider, _annotationRepository.Current));
            }

            default:
                return new CompletionList();
        }
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
                // Standalone — omit from require completions
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