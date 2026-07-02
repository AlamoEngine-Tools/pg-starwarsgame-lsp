// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Util;
using Location = OmniSharp.Extensions.LanguageServer.Protocol.Models.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Lua;

public sealed class LuaDefinitionHandler : DefinitionHandlerBase
{
    private static readonly LuaParseOptions s_parseOptions = new(LuaSyntaxOptions.Lua51);

    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<LuaDefinitionHandler> _logger;
    private readonly IGameWorkspaceHost _workspaceHost;

    public LuaDefinitionHandler(
        IGameIndexService indexService,
        IGameWorkspaceHost workspaceHost,
        IFileHelper fileHelper,
        ILogger<LuaDefinitionHandler> logger)
    {
        _indexService = indexService;
        _workspaceHost = workspaceHost;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
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
                var symbol = index.Resolve(hit.Value.Id);
                if (symbol is null || symbol.Origin is not FileOrigin fo)
                {
                    _logger.LogDebug("Go-to-def: {Id} resolved to non-navigable origin", hit.Value.Id);
                    return Task.FromResult<LocationOrLocationLinks?>(null);
                }

                _logger.LogDebug("Go-to-def: {Id} → {Uri}:{Line}", hit.Value.Id, fo.Uri, fo.Line);
                return Task.FromResult<LocationOrLocationLinks?>(
                    new LocationOrLocationLinks(new LocationOrLocationLink(fo.ToLspLocation())));
            }
        }

        // Path B: require() argument — parse AST and resolve.
        if (!_workspaceHost.TryGetOrReadFromDisk(_fileHelper, uri, out var doc))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var tree = LuaSyntaxTree.ParseText(doc.Text, s_parseOptions);
        var resolved = TryResolveRequireAtPosition(tree.GetRoot(), line, character, index.Documents, _fileHelper, uri);
        if (resolved is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var targetLocation = new Location
        {
            Uri = resolved,
            Range = new LspRange(new Position(0, 0), new Position(0, 0))
        };
        _logger.LogDebug("Go-to-def (require): → {Uri}", resolved);
        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(new LocationOrLocationLink(targetLocation)));
    }

    private static string? TryResolveRequireAtPosition(
        SyntaxNode root, int line, int character,
        IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper, string callerUri)
    {
        var found = LuaRequireCallLocator.TryFindAt(root, line, character);
        return found is null
            ? null
            : LuaRequireResolver.Resolve(found.Value.ArgText, documents, fileHelper, callerUri);
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("lua") };
    }
}