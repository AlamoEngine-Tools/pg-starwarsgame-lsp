// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaDefinitionHandler : DefinitionHandlerBase
{
    private readonly ILuaAnalyzer _analyzer;

    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaDefinitionHandler> _logger;
    private readonly ILuaParseCache _parseCache;
    private readonly ILuaSandboxProvider _sandbox;

    // The analyzer answers where we have nothing; the sandbox keeps its answer to the requesting
    // script's own state, as the game runs one per script (#249).
    public LuaDefinitionHandler(
        IGameIndexService indexService,
        ILuaParseCache parseCache,
        IFileHelper fileHelper,
        ILogger<LuaDefinitionHandler> logger,
        ILuaAnalyzer analyzer,
        ILuaSandboxProvider sandbox)
    {
        _indexService = indexService;
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _logger = logger;
        _analyzer = analyzer;
        _sandbox = sandbox;
    }

    public override async Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        var ours = await HandleOurs(request);
        if (ours is not null && ours.Any()) return ours;
        if (!request.TextDocument.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return ours;
        var theirs = await _analyzer.RequestAsync<LocationOrLocationLinks>("textDocument/definition", request, ct);
        if (theirs is null) return ours;

        // Only what the requesting script's own state holds: the game runs one per script (#249).
        var current = _sandbox.Current;
        var from = request.TextDocument.Uri.ToString();
        var kept = theirs.Where(l => current.ShareAState(from,
            (l.IsLocation ? l.Location!.Uri : l.LocationLink!.TargetUri).ToString())).ToList();
        return kept.Count > 0 ? new LocationOrLocationLinks(kept) : ours;
    }

    private Task<LocationOrLocationLinks?> HandleOurs(DefinitionParams request)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var line = request.Position.Line;
        var character = request.Position.Character;
        var index = _indexService.Current;

        // Path A: LuaGlobal symbol/reference via DocumentIndex.
        if (index.Documents.TryGetValue(uri, out var docIndex))
        {
            var hit = LuaPositionResolver.FindAtPosition(docIndex, line, character);
            if (hit is not null)
            {
                var links = ResolveInStates(index, hit.Value.Id, uri)
                    .Select(s => s.Origin)
                    .OfType<FileOrigin>()
                    .Select(fo => new LocationOrLocationLink(fo.ToLspLocationLink(hit.Value.Range)))
                    .ToList();
                if (links.Count == 0)
                {
                    _logger.LogDebug("Go-to-def: {Id} resolved to non-navigable origin", hit.Value.Id);
                    return Task.FromResult<LocationOrLocationLinks?>(null);
                }

                _logger.LogDebug("Go-to-def: {Id} -> {Count} definition(s)", hit.Value.Id, links.Count);
                return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(links));
            }
        }

        // Path B: require() argument - resolve via the shared parse.
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        // Path C: a state name - the handler registered under it (#252).
        if (StateHandlerLink(parsed.Tree.GetRoot(), uri, line, character, index) is { } stateLink)
            return Task.FromResult<LocationOrLocationLinks?>(
                new LocationOrLocationLinks(new LocationOrLocationLink(stateLink)));

        var resolved = TryResolveRequireAtPosition(parsed.Tree.GetRoot(), line, character, index.Documents, _fileHelper,
            uri);
        if (resolved is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var zeroRange = new LspRange(new Position(0, 0), new Position(0, 0));
        var targetLink = new LocationLink
        {
            TargetUri = resolved.Value.Target,
            TargetRange = zeroRange,
            TargetSelectionRange = zeroRange,
            OriginSelectionRange = resolved.Value.Origin
        };
        _logger.LogDebug("Go-to-def (require): -> {Uri}", resolved.Value.Target);
        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(new LocationOrLocationLink(targetLink)));
    }

    private LocationLink? StateHandlerLink(SyntaxNode root, string uri, int line, int character, GameIndex index)
    {
        var machine = LuaStateMachine.Read(root);
        if (machine.IsEmpty || machine.At(line, character) is not { Kind: LuaStateHitKind.STATE_NAME } hit ||
            hit.Registration?.Handler is not { } handler)
            return null;
        if (!LuaStateMachineContract.For(uri, index.Documents, _parseCache, _fileHelper).StateMachine) return null;

        static LspRange RangeOf(Loretta.CodeAnalysis.Location location)
        {
            var span = location.GetLineSpan();
            return new LspRange(new Position(span.StartLinePosition.Line, span.StartLinePosition.Character),
                new Position(span.EndLinePosition.Line, span.EndLinePosition.Character));
        }

        return new LocationLink
        {
            TargetUri = DocumentUri.From(uri),
            TargetRange = RangeOf(handler.NameLocation),
            TargetSelectionRange = RangeOf(handler.NameLocation),
            OriginSelectionRange = RangeOf(hit.Location)
        };
    }

    // The index holds every script's globals; one in another script's state is a different global
    // of the same name (#249). Every state holding the file gets its own definition, the highest layer
    // first as before: a script has one state (1:1), a library one per script requiring it (1:n), so an
    // entry point a library calls goes to every implementation. A definition in a file no state holds -
    // an XML object's - is the game's, reachable from every state.
    private IReadOnlyList<GameSymbol> ResolveInStates(GameIndex index, string id, string uri)
    {
        if (!index.WorkspaceDefinitions.TryGetValue(id, out var definitions))
            return index.Resolve(id) is { } resolved ? [resolved] : [];
        var current = _sandbox.Current;
        var states = current.StatesHolding(uri);
        if (states.Count == 0)
            return definitions
                .Where(s => s.Origin is not FileOrigin fo || current.ShareAState(uri, fo.Uri))
                .OrderByDescending(index.LayerRankOf)
                .Take(1)
                .ToList();

        return states
            .Select(state => definitions
                .Where(s => s.Origin is not FileOrigin fo || current.Holds(state, fo.Uri) ||
                            current.StatesHolding(fo.Uri).Count == 0)
                .OrderByDescending(index.LayerRankOf)
                .FirstOrDefault())
            .OfType<GameSymbol>()
            .Distinct()
            .ToList();
    }

    private static (string Target, LspRange Origin)? TryResolveRequireAtPosition(
        SyntaxNode root, int line, int character,
        IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper, string callerUri)
    {
        var found = LuaRequireCallLocator.TryFindAt(root, line, character);
        if (found is null)
            return null;

        var target = LuaRequireResolver.Resolve(found.Value.ArgText, documents, fileHelper, callerUri);
        if (target is null)
            return null;

        var origin = new LspRange(
            new Position(found.Value.StartLine, found.Value.StartChar),
            new Position(found.Value.EndLine, found.Value.EndChar));
        return (target, origin);
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("lua") };
    }
}