// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaHoverHandler : ILuaHoverProvider
{
    private readonly ILuaAnalyzer? _analyzer;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaHoverHandler> _logger;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    public LuaHoverHandler(
        IGameIndexService indexService,
        ILuaParseCache parseCache,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaHoverHandler> logger,
        // Optional: with the analyzer running, its card follows ours.
        ILuaAnalyzer? analyzer = null)
    {
        _indexService = indexService;
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
        _analyzer = analyzer;
    }

    public async Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var ours = await HandleOurs(request);
        if (_analyzer is null ||
            !request.TextDocument.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return ours;
        var theirs = await _analyzer.RequestAsync<Hover>("textDocument/hover", request, ct);
        return LuaAnalyzerAnswers.MergeHover(ours, theirs);
    }

    private Task<Hover?> HandleOurs(HoverParams request)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Hover?>(null);

        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return Task.FromResult<Hover?>(null);

        var line = request.Position.Line;
        var character = request.Position.Character;
        var index = _indexService.Current;

        // Phase 1: XML reference hover from DocumentIndex - no AST needed.
        var xmlRefHover = TryBuildXmlRefHover(index, uri, line, character);
        if (xmlRefHover is not null)
            return Task.FromResult<Hover?>(xmlRefHover);

        // AST (shared via the parse cache) for Phases 2 and 3.
        var root = parsed.Tree.GetRoot();

        // Phase 2: require() module hover.
        var requireHover = TryBuildRequireHover(root, line, character, index.Documents, _fileHelper, uri);
        if (requireHover is not null)
            return Task.FromResult<Hover?>(requireHover);

        // Phase 3: an engine function, from the stubs. The workspace's own functions and types are
        // the analyzer's (#155).
        return Task.FromResult(TryBuildEngineFunctionHover(root, line, character, _schemaProvider));
    }

    private static Hover? TryBuildXmlRefHover(GameIndex index, string uri, int line, int character)
    {
        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return null;

        foreach (var reference in docIndex.References)
        {
            if (reference.ExpectedKind != GameSymbolKind.XmlObject) continue;
            if (reference.Line != line) continue;
            if (character < reference.Column || character > reference.Column + reference.Length) continue;

            var sym = index.Resolve(reference.TargetId);
            var typeName = sym?.TypeName ?? reference.ExpectedTypeName ?? "XmlObject";
            var range = new LspRange(
                new Position(reference.Line, reference.Column),
                new Position(reference.Line, reference.Column + reference.Length));

            string markdown;
            if (sym?.Origin is MegArchiveOrigin meg)
                markdown = $"### `{typeName}` *`\"{reference.TargetId}\"`*\n\n" +
                           MegArchiveOriginHoverText.Describe(meg);
            else
                markdown = $"### `{typeName}` *`\"{reference.TargetId}\"`*";

            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = markdown
                }),
                Range = range
            };
        }

        return null;
    }

    private static Hover? TryBuildRequireHover(SyntaxNode root, int line, int character,
        IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper, string callerUri)
    {
        var found = LuaRequireCallLocator.TryFindAt(root, line, character);
        if (found is null) return null;

        var (requireArg, startLine, startChar, endLine, endChar) = found.Value;
        var range = new LspRange(
            new Position(startLine, startChar),
            new Position(endLine, endChar));

        var resolved = LuaRequireResolver.Resolve(requireArg, documents, fileHelper, callerUri);
        string markdown;
        if (resolved is not null)
        {
            var normalized = resolved.Replace('\\', '/');
            var slashIdx = normalized.LastIndexOf('/');
            var filename = slashIdx >= 0 ? normalized[(slashIdx + 1)..] : normalized;
            markdown = $"**require** `{requireArg}`\n-> `{filename}`";
        }
        else
        {
            markdown = $"**require** `{requireArg}`\n*Module not found in workspace*";
        }

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = markdown
            }),
            Range = range
        };
    }

    private static Hover? TryBuildEngineFunctionHover(
        SyntaxNode root, int line, int character, ILuaApiSchemaProvider schema)
    {
        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var loc = id.GetLocation();
            var span = loc.GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            if (line < start.Line || line > end.Line) continue;
            if (line == start.Line && character < start.Character) continue;
            if (line == end.Line && character > end.Character) continue;

            var range = new LspRange(
                new Position(start.Line, start.Character),
                new Position(end.Line, end.Character));

            if (schema.AllFunctionNames.Contains(id.Name))
                return BuildHover(BuildFunctionHoverMarkdown(id.Name, schema.GetFunctionDescription(id.Name),
                    schema.GetFunctionParams(id.Name), schema.GetReturnTypeName(id.Name)), range);
        }

        return null;
    }

    private static string BuildFunctionHoverMarkdown(string name, string? description,
        IReadOnlyList<LuaParamAnnotation> parameters, string? returnType)
    {
        var sb = new StringBuilder();

        // Signature line with param names if available.
        if (parameters.Count > 0)
        {
            var paramList = string.Join(", ", parameters.Select(p => p.IsOptional ? p.Name + "?" : p.Name));
            sb.Append($"**function** `{name}({paramList})`");
        }
        else
        {
            sb.Append($"**function** `{name}`");
        }

        if (description is not null)
        {
            sb.AppendLine();
            sb.Append(description);
        }

        if (parameters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("**Parameters:**");
            foreach (var p in parameters)
            {
                sb.AppendLine();
                var pName = p.IsOptional ? $"`{p.Name}?`" : $"`{p.Name}`";
                if (p.Description is not null)
                    sb.Append($"- {pName} `{p.Type.Raw}` - {p.Description}");
                else
                    sb.Append($"- {pName} `{p.Type.Raw}`");
            }
        }

        if (returnType is not null)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"**Returns:** `{returnType}`");
        }

        return sb.ToString().TrimEnd();
    }

    private static Hover BuildHover(string markdown, LspRange range)
    {
        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = markdown
            }),
            Range = range
        };
    }
}