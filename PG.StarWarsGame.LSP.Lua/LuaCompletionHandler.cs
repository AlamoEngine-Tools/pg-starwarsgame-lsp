// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaCompletionHandler : CompletionHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaCompletionHandler> _logger;
    private readonly ILuaApiSchemaProvider _schemaProvider;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaCompletionHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaCompletionHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new CompletionList());

        if (!_workspaceHost.TryGet(uri, out var doc))
            return Task.FromResult(new CompletionList());

        var lines = doc.Text.Split('\n');
        var lineIndex = request.Position.Line;
        if (lineIndex >= lines.Length)
            return Task.FromResult(new CompletionList());

        var line = lines[lineIndex].TrimEnd('\r');
        var character = request.Position.Character;

        var context = FindStringArgContext(line, character);
        if (context is null)
            return Task.FromResult(new CompletionList());

        var (functionName, paramIndex) = context.Value;
        var index = _indexService.Current;

        if (string.Equals(functionName, "require", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new CompletionList(BuildRequireCompletions(index)));

        var xmlRefs = _schemaProvider.GetXmlRefs(functionName);
        if (xmlRefs.Count == 0)
            return Task.FromResult(new CompletionList());

        XmlRefEntry? refEntry = null;
        foreach (var r in xmlRefs)
            if (r.ParamIndex == paramIndex)
            {
                refEntry = r;
                break;
            }

        if (refEntry is null)
            return Task.FromResult(new CompletionList());

        _logger.LogDebug("Lua completion: {Function} param {Index} → type {Type}",
            functionName, paramIndex, refEntry.Value.ExpectedTypeName ?? "*");

        return Task.FromResult(new CompletionList(BuildXmlRefCompletions(index, refEntry.Value.ExpectedTypeName)));
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
            TriggerCharacters = new Container<string>("\"", "'"),
            ResolveProvider = false
        };
    }

    // Scans the line text backward from `character` to detect if the cursor is inside
    // a string literal that is an argument to a function call.
    // Returns (FunctionName, ParamIndex) if found, null otherwise.
    private static (string FunctionName, int ParamIndex)? FindStringArgContext(string line, int character)
    {
        var bound = Math.Min(character, line.Length);

        // Walk backward to find the opening quote of the string we're inside.
        var i = bound - 1;
        while (i >= 0)
        {
            var ch = line[i];
            if (ch == '"' || ch == '\'') break;
            if (ch == ')' || ch == ';') return null; // not inside a string in a call
            i--;
        }

        if (i < 0) return null;
        var quotePos = i;

        // Walk backward from the opening quote, counting commas (for param index)
        // and tracking bracket depth to find the enclosing '('.
        i = quotePos - 1;
        var paramIndex = 0;
        var depth = 0;

        while (i >= 0)
        {
            var ch = line[i];
            if (ch == ')' || ch == ']' || ch == '}')
            {
                depth++;
                i--;
                continue;
            }

            if (ch == '(' || ch == '[' || ch == '{')
            {
                if (depth > 0)
                {
                    depth--;
                    i--;
                    continue;
                }

                break; // found the enclosing opening paren
            }

            if (ch == ',' && depth == 0) paramIndex++;
            if (ch == ';' || ch == '\n') return null;
            i--;
        }

        if (i < 0) return null;
        var parenPos = i;

        // Extract function name immediately before the paren.
        i = parenPos - 1;
        while (i >= 0 && char.IsWhiteSpace(line[i])) i--;

        var nameEnd = i;
        if (nameEnd < 0) return null;

        while (i >= 0 && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i--;
        var nameStart = i + 1;

        if (nameStart > nameEnd) return null;

        return (line[nameStart..(nameEnd + 1)], paramIndex);
    }

    private static IEnumerable<CompletionItem> BuildRequireCompletions(GameIndex index)
    {
        foreach (var uri in index.Documents.Keys)
        {
            if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;

            var normalized = uri.Replace('\\', '/');
            var slashIdx = normalized.LastIndexOf('/');
            var filename = slashIdx >= 0 ? normalized[(slashIdx + 1)..] : normalized;

            if (!filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
            var moduleName = filename[..^4]; // strip ".lua"

            yield return new CompletionItem
            {
                Label = moduleName,
                Kind = CompletionItemKind.Module
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