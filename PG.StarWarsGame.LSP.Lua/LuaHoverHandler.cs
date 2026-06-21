// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaHoverHandler : ILuaHoverProvider
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaHoverHandler> _logger;
    private readonly ILuaApiSchemaProvider _schemaProvider;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaHoverHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaHoverHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _logger = logger;
    }

    public Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Hover?>(null);

        if (!_workspaceHost.TryGetOrReadFromDisk(_fileHelper, uri, out var doc))
            return Task.FromResult<Hover?>(null);

        var line = request.Position.Line;
        var character = request.Position.Character;
        var index = _indexService.Current;

        // Phase 1: XML reference hover from DocumentIndex — no AST needed.
        var xmlRefHover = TryBuildXmlRefHover(index, uri, line, character);
        if (xmlRefHover is not null)
            return Task.FromResult<Hover?>(xmlRefHover);

        // Parse AST once for Phases 2 and 3.
        var tree = LuaSyntaxTree.ParseText(doc.Text, s_parseOptions);
        var root = tree.GetRoot();

        // Phase 2: require() module hover.
        var requireHover = TryBuildRequireHover(root, line, character, index.Documents, _fileHelper);
        if (requireHover is not null)
            return Task.FromResult<Hover?>(requireHover);

        // Phase 3: Lua identifier hover (workspace global or engine global).
        var identHover = TryBuildIdentifierHover(root, line, character, index, _schemaProvider);
        return Task.FromResult(identHover);
    }

    private static Hover? TryBuildXmlRefHover(GameIndex index, string uri, int line, int character)
    {
        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return null;

        foreach (var reference in docIndex.References)
        {
            if (reference.Line != line) continue;
            if (character < reference.Column || character > reference.Column + reference.Length) continue;

            var sym = index.Resolve(reference.TargetId);
            var typeName = sym?.TypeName ?? reference.ExpectedTypeName ?? "XmlObject";
            var range = new LspRange(
                new Position(reference.Line, reference.Column),
                new Position(reference.Line, reference.Column + reference.Length));

            string markdown;
            if (sym?.Origin is MegArchiveOrigin meg)
            {
                var archiveName = Path.GetFileName(meg.ArchivePath);
                markdown = $"### `{typeName}` *`\"{reference.TargetId}\"`*\n\n" +
                           $"*Packaged in* `{archiveName}` — this object is read-only and cannot be renamed or navigated to.";
            }
            else
            {
                markdown = $"### `{typeName}` *`\"{reference.TargetId}\"`*";
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

        return null;
    }

    private static Hover? TryBuildRequireHover(SyntaxNode root, int line, int character,
        IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper)
    {
        foreach (var call in root.DescendantNodes().OfType<FunctionCallExpressionSyntax>())
        {
            if (call.Expression is not IdentifierNameSyntax { Name: "require" }) continue;

            string? requireArg = null;
            if (call.Argument is StringFunctionArgumentSyntax strArg)
                requireArg = strArg.Expression.Token.ValueText;
            else if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
                     exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
                requireArg = lit.Token.ValueText;

            if (requireArg is null) continue;

            var tokenSpan = GetRequireArgSpan(call);
            if (tokenSpan is null) continue;

            var (startLine, startChar, endLine, endChar) = tokenSpan.Value;
            if (line < startLine || line > endLine) continue;
            if (line == startLine && character < startChar) continue;
            if (line == endLine && character > endChar) continue;

            var range = new LspRange(
                new Position(startLine, startChar),
                new Position(endLine, endChar));

            var resolved = LuaRequireResolver.Resolve(requireArg, documents, fileHelper);
            string markdown;
            if (resolved is not null)
            {
                var normalized = resolved.Replace('\\', '/');
                var slashIdx = normalized.LastIndexOf('/');
                var filename = slashIdx >= 0 ? normalized[(slashIdx + 1)..] : normalized;
                markdown = $"**require** `{requireArg}`\n→ `{filename}`";
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

        return null;
    }

    private static (int StartLine, int StartChar, int EndLine, int EndChar)? GetRequireArgSpan(
        FunctionCallExpressionSyntax call)
    {
        if (call.Argument is StringFunctionArgumentSyntax strArg)
        {
            var s = strArg.Expression.Token.GetLocation().GetLineSpan();
            return (s.StartLinePosition.Line, s.StartLinePosition.Character,
                s.EndLinePosition.Line, s.EndLinePosition.Character);
        }

        if (call.Argument is ExpressionListFunctionArgumentSyntax exprList &&
            exprList.Expressions.FirstOrDefault() is LiteralExpressionSyntax lit)
        {
            var s = lit.Token.GetLocation().GetLineSpan();
            return (s.StartLinePosition.Line, s.StartLinePosition.Character,
                s.EndLinePosition.Line, s.EndLinePosition.Character);
        }

        return null;
    }

    private static Hover? TryBuildIdentifierHover(
        SyntaxNode root, int line, int character, GameIndex index, ILuaApiSchemaProvider schema)
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

            // Workspace LuaGlobal takes precedence over engine global.
            if (index.WorkspaceDefinitions.TryGetValue(id.Name, out var defs) &&
                defs.Any(s => s.Kind == GameSymbolKind.LuaGlobal))
                return new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = $"**function** `{id.Name}`"
                    }),
                    Range = range
                };

            // Engine global: show name and description from schema.
            if (schema.AllFunctionNames.Contains(id.Name))
            {
                var sb = new StringBuilder();
                sb.Append($"**function** `{id.Name}`");
                var desc = schema.GetFunctionDescription(id.Name);
                if (desc is not null)
                {
                    sb.AppendLine();
                    sb.Append(desc);
                }

                return new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = sb.ToString()
                    }),
                    Range = range
                };
            }
        }

        return null;
    }
}