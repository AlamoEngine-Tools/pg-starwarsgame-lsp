// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
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
using PG.StarWarsGame.LSP.Lua.Analysis.Annotations;
using PG.StarWarsGame.LSP.Lua.Schema;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaHoverHandler : ILuaHoverProvider
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly ILuaAnnotationRepository _annotationRepository;
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
        ILuaAnnotationRepository annotationRepository,
        ILogger<LuaHoverHandler> logger)
    {
        _indexService = indexService;
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _schemaProvider = schemaProvider;
        _annotationRepository = annotationRepository;
        _logger = logger;
    }

    public Task<Hover?> Handle(HoverParams request, CancellationToken ct)
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

        // Phase 1: XML reference hover from DocumentIndex — no AST needed.
        var xmlRefHover = TryBuildXmlRefHover(index, uri, line, character);
        if (xmlRefHover is not null)
            return Task.FromResult<Hover?>(xmlRefHover);

        // AST (shared via the parse cache) for Phases 2 and 3.
        var root = parsed.Tree.GetRoot();

        // Phase 2: require() module hover.
        var requireHover = TryBuildRequireHover(root, line, character, index.Documents, _fileHelper, uri);
        if (requireHover is not null)
            return Task.FromResult<Hover?>(requireHover);

        // Phase 3: Lua identifier hover (workspace global, engine global, or type).
        var identHover = TryBuildIdentifierHover(root, line, character, index, _schemaProvider,
            _annotationRepository);
        return Task.FromResult(identHover);
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
            {
                markdown = $"### `{typeName}` *`\"{reference.TargetId}\"`*\n\n" +
                           MegArchiveOriginHoverText.Describe(meg);
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

    private static Hover? TryBuildIdentifierHover(
        SyntaxNode root, int line, int character, GameIndex index, ILuaApiSchemaProvider schema,
        ILuaAnnotationRepository repository)
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
            if (index.WorkspaceDefinitions.TryGetValue(id.Name, out var defs))
            {
                var luaGlobal = defs.FirstOrDefault(s => s.Kind == GameSymbolKind.LuaGlobal);
                if (luaGlobal is not null)
                {
                    var ann = repository.GetFunctionAnnotation(id.Name);
                    // If no annotation is indexed yet, synthesise one from the symbol description.
                    if (ann is null && luaGlobal.Description is not null)
                        ann = EmmyLuaAnnotations.Empty with { Description = luaGlobal.Description };
                    return BuildHover(BuildFunctionHoverMarkdown(id.Name, ann), range);
                }
            }

            // Engine global function: show name, params, and return type from schema.
            if (schema.AllFunctionNames.Contains(id.Name))
            {
                var ann = EmmyLuaAnnotations.Empty with
                {
                    Description = schema.GetFunctionDescription(id.Name),
                    Params = schema.GetFunctionParams(id.Name).ToImmutableArray(),
                    Returns = schema.GetReturnTypeName(id.Name) is { } ret
                        ? [new LuaReturnAnnotation(new LuaTypeRef(ret), null, null)]
                        : ImmutableArray<LuaReturnAnnotation>.Empty
                };
                return BuildHover(BuildFunctionHoverMarkdown(id.Name, ann), range);
            }

            // Workspace type (class, alias, enum) from the annotation repository.
            var typeIndex = repository.Current;
            var typeHover = TryBuildTypeHover(id.Name, range, typeIndex, schema);
            if (typeHover is not null)
                return typeHover;
        }

        return null;
    }

    private static Hover? TryBuildTypeHover(
        string name, LspRange range, ILuaTypeIndex typeIndex, ILuaApiSchemaProvider schema)
    {
        // Workspace @class
        if (typeIndex.GetClass(name) is { } cls)
            return BuildClassHover(cls, range);

        // Engine API @class (from api.d.lua)
        if (schema.GetClassDefinition(name) is { } apiCls)
            return BuildClassHover(apiCls, range);

        // Workspace @alias
        if (typeIndex.GetAlias(name) is { } alias)
        {
            var variants = string.Join(" | ", alias.Variants.Select(v => v.Raw));
            var md = variants.Length > 0
                ? $"**alias** `{alias.Name}` = {variants}"
                : $"**alias** `{alias.Name}`";
            return BuildHover(md, range);
        }

        // Workspace @enum
        if (typeIndex.GetEnum(name) is { } en)
            return BuildHover($"**enum** `{en.Name}`", range);

        return null;
    }

    private static Hover BuildClassHover(LuaClassDefinition cls, LspRange range)
    {
        var sb = new StringBuilder();
        sb.Append($"**class** `{cls.Name}`");
        if (cls.Description is not null)
        {
            sb.AppendLine();
            sb.Append(cls.Description);
        }

        if (!cls.Fields.IsDefaultOrEmpty)
        {
            sb.AppendLine();
            foreach (var field in cls.Fields)
                sb.AppendLine($"`{field.Name}` — {field.Type.Raw}");
        }

        return BuildHover(sb.ToString().TrimEnd(), range);
    }

    private static string BuildFunctionHoverMarkdown(string name, EmmyLuaAnnotations? ann)
    {
        var sb = new StringBuilder();

        // Signature line with param names if available.
        if (ann is not null && !ann.Params.IsDefaultOrEmpty)
        {
            var paramList = string.Join(", ", ann.Params.Select(p => p.IsOptional ? p.Name + "?" : p.Name));
            sb.Append($"**function** `{name}({paramList})`");
        }
        else
        {
            sb.Append($"**function** `{name}`");
        }

        if (ann is null)
            return sb.ToString();

        if (ann.IsDeprecated)
        {
            sb.AppendLine();
            sb.Append("*⚠ Deprecated*");
        }

        if (ann.Description is not null)
        {
            sb.AppendLine();
            sb.Append(ann.Description);
        }

        if (!ann.Params.IsDefaultOrEmpty)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("**Parameters:**");
            foreach (var p in ann.Params)
            {
                sb.AppendLine();
                var pName = p.IsOptional ? $"`{p.Name}?`" : $"`{p.Name}`";
                if (p.Description is not null)
                    sb.Append($"- {pName} `{p.Type.Raw}` — {p.Description}");
                else
                    sb.Append($"- {pName} `{p.Type.Raw}`");
            }
        }

        if (!ann.Returns.IsDefaultOrEmpty)
        {
            sb.AppendLine();
            sb.AppendLine();
            if (ann.Returns.Length == 1)
            {
                var r = ann.Returns[0];
                sb.Append($"**Returns:** `{r.Type.Raw}`");
                if (r.Description is not null)
                    sb.Append($" — {r.Description}");
            }
            else
            {
                sb.Append("**Returns:**");
                foreach (var r in ann.Returns)
                {
                    sb.AppendLine();
                    var line = $"- `{r.Type.Raw}`";
                    if (r.Description is not null) line += $" — {r.Description}";
                    sb.Append(line);
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static Hover BuildHover(string markdown, LspRange range) =>
        new()
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = markdown
            }),
            Range = range
        };
}