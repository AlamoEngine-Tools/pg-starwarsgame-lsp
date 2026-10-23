// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;
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
    private readonly IMessageCatalog _messages;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaApiSchemaProvider _schemaProvider;

    public LuaHoverHandler(
        IGameIndexService indexService,
        ILuaParseCache parseCache,
        IFileHelper fileHelper,
        ILuaApiSchemaProvider schemaProvider,
        ILogger<LuaHoverHandler> logger,
        // Optional: with the analyzer running, its card follows ours.
        ILuaAnalyzer? analyzer = null,
        IMessageCatalog? messages = null)
    {
        _messages = messages ?? ResxMessageCatalog.Server("en");
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

        // Phase 3: the state machine library's states, handlers and message (#252).
        var stateHover = TryBuildStateMachineHover(root, uri, line, character, index);
        if (stateHover is not null)
            return Task.FromResult<Hover?>(stateHover);

        // Phase 4: an engine function, from the stubs. The workspace's own functions and types are
        // the analyzer's (#155).
        return Task.FromResult(TryBuildEngineFunctionHover(root, line, character, _schemaProvider));
    }

    private Hover? TryBuildStateMachineHover(SyntaxNode root, string uri, int line, int character, GameIndex index)
    {
        var machine = LuaStateMachine.Read(root);
        if (machine.IsEmpty || machine.At(line, character) is not { } hit) return null;
        var contract = LuaStateMachineContract.For(uri, index.Documents, _parseCache, _fileHelper);
        if (!contract.StateMachine) return null;
        if (hit.Registration?.Source == LuaStateSource.STORY_MODE_EVENTS && !contract.StoryMode) return null;

        var span = hit.Location.GetLineSpan();
        var range = new LspRange(
            new Position(span.StartLinePosition.Line, span.StartLinePosition.Character),
            new Position(span.EndLinePosition.Line, span.EndLinePosition.Character));
        var initial = contract.StoryMode
            ? LuaStateMachine.StoryInitState
            : machine.Registrations.FirstOrDefault(r => r.Source == LuaStateSource.DEFINE_STATE)?.Name;
        var role = hit.State == initial
            ? _messages.Format("LuaHover_InitialState")
            : _messages.Format("LuaHover_State");

        return hit.Kind switch
        {
            LuaStateHitKind.MESSAGE => BuildHover(
                _messages.Format("LuaHover_Message", hit.State),
                range),
            LuaStateHitKind.HANDLER => BuildHover(
                _messages.Format("LuaHover_StateHandler", role, hit.State),
                range),
            _ => BuildHover(hit.Registration is { } registration
                    ? _messages.Format("LuaHover_Registered", role, hit.State,
                          (registration.Value as IdentifierNameSyntax)?.Name ??
                          _messages.Format("LuaHover_InlineFunction")) +
                      (registration.Source == LuaStateSource.STORY_MODE_EVENTS
                          ? "\n" + _messages.Format("LuaHover_StoryEventState")
                          : "")
                    : _messages.Format("LuaHover_StateNotRegisteredThisScript", hit.State),
                range)
        };
    }

    private Hover? TryBuildXmlRefHover(GameIndex index, string uri, int line, int character)
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
                           MegArchiveOriginHoverText.Describe(meg, _messages);
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

    private Hover? TryBuildRequireHover(SyntaxNode root, int line, int character,
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
            markdown = _messages.Format("LuaHover_Require", requireArg, filename);
        }
        else
        {
            markdown = _messages.Format("LuaHover_RequireNotFound", requireArg);
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

    // A name the schema knows, used or defined: an engine function from the stubs, or an entry point.
    // The name in a script's own `function X(` counts too - the analyzer shows neither the stub's
    // documentation nor its types there (measured, 0.25.1), so the card is ours (#249).
    private Hover? TryBuildEngineFunctionHover(
        SyntaxNode root, int line, int character, ILuaApiSchemaProvider schema)
    {
        var uses = root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(id => (id.Name, Location: id.GetLocation()));
        var definitions = root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>()
            .Select(f => f.Name).OfType<SimpleFunctionNameSyntax>()
            .Select(n => (Name: n.Name.Text, Location: n.Name.GetLocation()));

        foreach (var (name, location) in uses.Concat(definitions))
        {
            var span = location.GetLineSpan();
            var start = span.StartLinePosition;
            var end = span.EndLinePosition;

            if (line < start.Line || line > end.Line) continue;
            if (line == start.Line && character < start.Character) continue;
            if (line == end.Line && character > end.Character) continue;

            var range = new LspRange(
                new Position(start.Line, start.Character),
                new Position(end.Line, end.Character));

            // An entry point first: a library stub declaring the same name (Definitions in
            // PGInterventions) is the library's own copy, and the game calls the script's.
            if (schema.GetEntryPoint(name) is { } entryPoint)
                return BuildHover(BuildEntryPointHoverMarkdown(entryPoint), range);
            if (schema.AllFunctionNames.Contains(name))
                return BuildHover(BuildFunctionHoverMarkdown(name, schema.GetFunctionDescription(name),
                    schema.GetFunctionParams(name), schema.GetReturnTypeName(name)), range);
        }

        return null;
    }

    private string BuildEntryPointHoverMarkdown(LuaEntryPoint entryPoint)
    {
        var caller = entryPoint.Caller == LuaEntryPointCaller.ENGINE
            ? _messages.Format("LuaHover_CalledByGame")
            : _messages.Format("LuaHover_CalledByIfDefined",
                string.Join(", ", entryPoint.Libraries.Select(l => $"`{l}`")));
        return _messages.Format("LuaHover_EntryPoint", entryPoint.Name) + $"\n{entryPoint.Description}\n\n" +
               _messages.Format("LuaHover_EntryPointCaller", caller, string.Join(", ", entryPoint.Kinds));
    }

    private string BuildFunctionHoverMarkdown(string name, string? description,
        IReadOnlyList<LuaParamAnnotation> parameters, string? returnType)
    {
        var sb = new StringBuilder();

        // Signature line with param names if available.
        if (parameters.Count > 0)
        {
            var paramList = string.Join(", ", parameters.Select(p => p.IsOptional ? p.Name + "?" : p.Name));
            sb.Append(_messages.Format("LuaHover_Function", name, paramList));
        }
        else
        {
            sb.Append(_messages.Format("LuaHover_FunctionBare", name));
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
            sb.Append(_messages.Format("LuaHover_Parameters"));
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
            sb.Append(_messages.Format("LuaHover_Returns", returnType));
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