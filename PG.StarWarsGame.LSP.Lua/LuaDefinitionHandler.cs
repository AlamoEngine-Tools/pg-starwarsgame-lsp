// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;
using PG.StarWarsGame.LSP.Lua.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaDefinitionHandler : DefinitionHandlerBase
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);
    private readonly ILspConfigurationProvider _config;

    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaDefinitionHandler> _logger;
    private readonly ILuaParseCache _parseCache;

    public LuaDefinitionHandler(
        IGameIndexService indexService,
        ILuaParseCache parseCache,
        IFileHelper fileHelper,
        ILogger<LuaDefinitionHandler> logger,
        ILspConfigurationProvider config)
    {
        _indexService = indexService;
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _logger = logger;
        _config = config;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Lua.GoToDefinition)
            return Task.FromResult<LocationOrLocationLinks?>(null);

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
                var symbol = index.Resolve(hit.Value.Id);
                if (symbol is null || symbol.Origin is not FileOrigin fo)
                {
                    _logger.LogDebug("Go-to-def: {Id} resolved to non-navigable origin", hit.Value.Id);
                    return Task.FromResult<LocationOrLocationLinks?>(null);
                }

                _logger.LogDebug("Go-to-def: {Id} → {Uri}:{Line}", hit.Value.Id, fo.Uri, fo.Line);
                return Task.FromResult<LocationOrLocationLinks?>(
                    new LocationOrLocationLinks(new LocationOrLocationLink(fo.ToLspLocationLink(hit.Value.Range))));
            }
        }

        // Path B: require() argument - resolve via the shared parse.
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

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
        _logger.LogDebug("Go-to-def (require): → {Uri}", resolved.Value.Target);
        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(new LocationOrLocationLink(targetLink)));
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