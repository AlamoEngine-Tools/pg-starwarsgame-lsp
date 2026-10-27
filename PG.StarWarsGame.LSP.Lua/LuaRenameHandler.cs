// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Rename;
using PG.StarWarsGame.LSP.Lua.Schema;
using PG.StarWarsGame.LSP.Lua.Util;
using LorLocation = Loretta.CodeAnalysis.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaRenameHandler : IRenameProvider
{
    private readonly ILuaAnalyzer _analyzer;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly IProjectLayerMap _layers;
    private readonly ILogger<LuaRenameHandler> _logger;
    private readonly ILuaApiSchemaProvider _luaSchema;
    private readonly IMessageCatalog _messages;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaSandboxProvider _sandbox;
    private readonly ISchemaProvider _schema;
    private readonly IDocumentTextSource _textSource;

    // textSource stays alongside the parse cache: XmlObjectRenameBuilder edits XML documents,
    // which are outside the Lua parse cache's world. The Lua schema claims the names the engine
    // binds, so the analyzer never renames them; the sandbox scopes a Lua global's rename to the
    // script's own state; the file helper decides whether the script's state machine library is
    // the game's. What we do not claim (locals, fields) is the analyzer's to rename - within the
    // script's own state, and within the project only, checked against the layers.
    public LuaRenameHandler(
        ILuaParseCache parseCache,
        IDocumentTextSource textSource,
        ISchemaProvider schema,
        ILogger<LuaRenameHandler> logger,
        ILuaApiSchemaProvider luaSchema,
        ILuaSandboxProvider sandbox,
        IFileHelper fileHelper,
        IGameIndexService indexService,
        ILuaAnalyzer analyzer,
        IProjectLayerMap layers,
        IMessageCatalog messages)
    {
        _parseCache = parseCache;
        _textSource = textSource;
        _schema = schema;
        _logger = logger;
        _luaSchema = luaSchema;
        _sandbox = sandbox;
        _fileHelper = fileHelper;
        _indexService = indexService;
        _analyzer = analyzer;
        _layers = layers;
        _messages = messages;
    }

    public bool Handles(string documentUri)
    {
        return documentUri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WorkspaceEdit?> RenameAsync(RenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var index = _indexService.Current;
        if (Claims(uri, request.Position.Line, request.Position.Character, index))
            return HandleRename(uri, request, index);

        var edit = await _analyzer.RequestAsync<WorkspaceEdit>("textDocument/rename", request, ct);
        if (edit is null) return null;
        // Another script's state first: its file is no part of this rename, wherever it lives.
        edit = LuaAnalyzerRename.KeepWithinState(edit, _sandbox.Current, uri);
        LuaAnalyzerRename.EnsureWithinProject(edit, _layers, _messages);
        return edit;
    }

    public async Task<RangeOrPlaceholderRange?> PrepareAsync(PrepareRenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var index = _indexService.Current;
        var (line, character) = (request.Position.Line, request.Position.Character);
        if (Claims(uri, line, character, index))
            return HandlePrepare(uri, line, character, index);
        return await _analyzer.RequestAsync<RangeOrPlaceholderRange>("textDocument/prepareRename", request, ct);
    }

    public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
    {
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null) return null;

        // Try game-object reference first (cursor on a string literal whose value is a known XmlObject).
        var xmlObjectId = FindXmlObjectAtCursor(parsed.Tree, request.Position.Line, request.Position.Character, index);
        if (xmlObjectId is not null)
        {
            if (StoryRenameGuard.IsStorySymbol(xmlObjectId, index) &&
                StoryRenameGuard.Check(xmlObjectId, request.NewName, index) is { } objection)
                throw new InvalidOperationException(objection);
            return XmlObjectRenameBuilder.Build(xmlObjectId, request.NewName, index, _schema, _textSource, _logger);
        }

        // A state name: the library looks states up by the string, so every literal of it moves (#252).
        if (StateNameAt(uri, parsed.Tree, request.Position.Line, request.Position.Character, index) is
            { } state)
            return state.Machine is { } machine ? RenameState(uri, machine, state.Hit.State, request.NewName) : null;

        // Fall back to Lua global (cursor on an identifier that is a known LuaGlobal).
        var luaGlobalId = FindLuaGlobalAtCursor(parsed.Tree, request.Position.Line, request.Position.Character,
            index);
        // The game calls an engine entry point by name: renamed, it is never called again.
        if (luaGlobalId is not null && IsEngineEntryPoint(luaGlobalId)) return null;
        if (luaGlobalId is not null)
            return LuaGlobalRenameBuilder.Build(luaGlobalId, request.NewName, index, _parseCache, _logger,
                InScope(uri, luaGlobalId, index));

        return null;
    }

    // The state name under the position, where the script's library keeps the game's contract.
    // Machine is null for a story event's state: its name is the story XML's, not the script's.
    private (LuaStateHit Hit, LuaStateMachine? Machine)? StateNameAt(string uri, SyntaxTree tree,
        int line, int character, GameIndex index)
    {
        var machine = LuaStateMachine.Read(tree.GetRoot());
        if (machine.IsEmpty || machine.At(line, character) is not { Kind: LuaStateHitKind.STATE_NAME } hit) return null;
        if (!LuaStateMachineContract.For(uri, index.Documents, _parseCache, _fileHelper).StateMachine) return null;
        var storyEvent = machine.Registrations.Any(r => r.Name == hit.State &&
                                                        r.Source == LuaStateSource.STORY_MODE_EVENTS);
        return (hit, storyEvent ? null : machine);
    }

    private static WorkspaceEdit RenameState(string uri, LuaStateMachine machine, string state, string newName)
    {
        var edits = machine.Registrations.Where(r => r.Name == state).Select(r => r.NameLocation)
            .Concat(machine.Transitions.Where(t => t.Name == state).Select(t => t.NameLocation))
            .Select(location => location.GetLineSpan())
            .OrderBy(span => span.StartLinePosition.Line).ThenBy(span => span.StartLinePosition.Character)
            .Select(span => new TextEdit
            {
                NewText = newName,
                Range = new LspRange(
                    new Position(span.StartLinePosition.Line, span.StartLinePosition.Character),
                    new Position(span.EndLinePosition.Line, span.EndLinePosition.Character))
            })
            .ToList();
        return new WorkspaceEdit
        {
            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [DocumentUri.From(uri)] = edits }
        };
    }

    private bool IsEngineEntryPoint(string name)
    {
        return _luaSchema.GetEntryPoint(name) is { Caller: LuaEntryPointCaller.ENGINE };
    }

    // The files a rename of <id> from <uri> may edit (#249): the script's state, joined with every
    // state holding a shared file that names <id>.
    private Func<string, bool> InScope(string uri, string id, GameIndex index)
    {
        var definitions = index.WorkspaceDefinitions.TryGetValue(id, out var defs)
            ? defs.Select(s => s.Origin).OfType<FileOrigin>().Select(o => o.Uri)
            : [];
        var references = index.WorkspaceReferences.TryGetValue(id, out var refs)
            ? refs.Select(r => r.DocumentUri)
            : [];
        return _sandbox.Current.RenameScope(uri, definitions.Concat(references));
    }

    public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
    {
        if (_parseCache.GetOrParse(uri) is { } stateParse &&
            StateNameAt(uri, stateParse.Tree, line, character, index) is { } state)
        {
            if (state.Machine is null) return null;
            var span = state.Hit.Location.GetLineSpan();
            return new RangeOrPlaceholderRange(new LspRange(
                new Position(span.StartLinePosition.Line, span.StartLinePosition.Character),
                new Position(span.EndLinePosition.Line, span.EndLinePosition.Character)));
        }

        // LuaGlobal path - requires document in the index.
        if (index.Documents.TryGetValue(uri, out var docIndex))
        {
            var hit = LuaPositionResolver.FindAtPosition(docIndex, line, character);
            if (hit is not null)
            {
                if (IsEngineEntryPoint(hit.Value.Id)) return null;
                // An XML file is in no script's state, so an XML object's definitions all count.
                if (!LuaGlobalRenameBuilder.IsLeafOwned(hit.Value.Id, index,
                        InScope(uri, hit.Value.Id, index) ?? (_ => true)))
                    return null;

                var range = hit.Value.Range;
                // Zero-length means cursor is on a declaration; extend by the symbol name length.
                if (range.Start == range.End)
                    range = new LspRange(range.Start,
                        new Position(range.Start.Line, range.Start.Character + hit.Value.Id.Length));

                return new RangeOrPlaceholderRange(range);
            }
        }

        // XmlObject string literal path - requires the parsed document.
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null) return null;

        return FindXmlObjectStringRange(parsed.Tree, line, character, index);
    }

    /// <summary>
    ///     Whether the position names something this server renames - an XML object, a Lua global,
    ///     a state name, an engine name - so its answer stands even when it is a refusal. Anything
    ///     else (a local, a field) is the Lua analyzer's to rename.
    /// </summary>
    public bool Claims(string uri, int line, int character, GameIndex index)
    {
        if (_parseCache.GetOrParse(uri) is { } stateParse &&
            StateNameAt(uri, stateParse.Tree, line, character, index) is not null)
            return true;
        if (index.Documents.TryGetValue(uri, out var docIndex) &&
            LuaPositionResolver.FindAtPosition(docIndex, line, character) is not null)
            return true;

        // Nothing parsed: nothing to hand on either - ours, answered as a refusal.
        if (_parseCache.GetOrParse(uri) is not { } parsed) return true;
        var tree = parsed.Tree;
        if (FindXmlObjectAtCursor(tree, line, character, index) is not null) return true;
        if (FindLuaGlobalAtCursor(tree, line, character, index) is not null) return true;

        // An engine name: the game binds it, and renaming it breaks every script that calls it.
        return tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()
            .Any(id => LocationContainsPosition(id.GetLocation(), line, character) &&
                       _luaSchema.DeclaredGlobalNames.Contains(id.Name));
    }

    private RangeOrPlaceholderRange? FindXmlObjectStringRange(
        SyntaxTree tree, int line, int character, GameIndex index)
    {
        var root = tree.GetRoot();

        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;

            var loc = lit.GetLocation().GetLineSpan();
            var startLine = loc.StartLinePosition.Line;
            var startChar = loc.StartLinePosition.Character;

            if (startLine != line) continue;

            var value = lit.Token.ValueText;
            var innerStart = startChar + 1;
            var innerEnd = innerStart + value.Length;

            if (character < innerStart || character > innerEnd) continue;

            if (!IsKnownXmlObject(value, index)) continue;
            if (!index.IsLeafOwned(value)) return null;

            return new RangeOrPlaceholderRange(new LspRange(
                new Position(line, innerStart),
                new Position(line, innerEnd)));
        }

        return null;
    }

    private static string? FindXmlObjectAtCursor(SyntaxTree tree, int line, int character, GameIndex index)
    {
        var root = tree.GetRoot();

        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            if (!LocationContainsPosition(lit.GetLocation(), line, character)) continue;

            var value = lit.Token.ValueText;
            if (IsKnownXmlObject(value, index)) return value;
        }

        return null;
    }

    private static string? FindLuaGlobalAtCursor(SyntaxTree tree, int line, int character, GameIndex index)
    {
        var root = tree.GetRoot();

        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!LocationContainsPosition(id.GetLocation(), line, character)) continue;
            if (IsKnownLuaGlobal(id.Name, index)) return id.Name;
        }

        foreach (var funcDecl in root.DescendantNodes().OfType<FunctionDeclarationStatementSyntax>())
        {
            if (funcDecl.Name is not SimpleFunctionNameSyntax simpleName) continue;
            if (!LocationContainsPosition(simpleName.Name.GetLocation(), line, character)) continue;
            var name = simpleName.Name.Text;
            if (IsKnownLuaGlobal(name, index)) return name;
        }

        return null;
    }

    private static bool IsKnownXmlObject(string name, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(name, out var defs) &&
               defs.Any(s => s.Kind == GameSymbolKind.XmlObject);
    }

    private static bool IsKnownLuaGlobal(string name, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(name, out var defs) &&
               defs.Any(s => s.Kind == GameSymbolKind.LuaGlobal);
    }

    private static bool LocationContainsPosition(LorLocation location, int line, int character)
    {
        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        if (line < start.Line || line > end.Line) return false;
        if (line == start.Line && character < start.Character) return false;
        if (line == end.Line && character > end.Character) return false;
        return true;
    }
}