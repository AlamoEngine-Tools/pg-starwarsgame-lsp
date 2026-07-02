// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlDefinitionHandler : DefinitionHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlDefinitionHandler> _logger;

    public XmlDefinitionHandler(IGameIndexService indexService, IFileHelper fileHelper,
        ILogger<XmlDefinitionHandler> logger, IEaWXmlContext eaWXmlContext)
    {
        _indexService = indexService;
        _fileHelper = fileHelper;
        _logger = logger;
        _eaWXmlContext = eaWXmlContext;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<LocationOrLocationLinks?>(null);
        var index = _indexService.Current;

        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var hit = XmlPositionResolver.FindAtPosition(docIndex, request.Position.Line, request.Position.Character);
        if (hit is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        // Enum value references: "enum:{EnumName}/{ValueName}" — look up in WorkspaceEnumValueDefinitions.
        if (hit.Value.Id.StartsWith("enum:", StringComparison.Ordinal))
            return Task.FromResult(ResolveEnumDefinition(hit.Value.Id, index));

        // Group keys have no canonical single definition — they link co-members, not a target symbol.
        if (index.AllGroupMemberships.ContainsKey(hit.Value.Id))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var symbol = index.Resolve(hit.Value.Id);
        if (symbol is null || symbol.Origin is not FileOrigin { IsNavigable: true } fo)
        {
            // Baseline symbols carry a game-relative path (DATA\XML\…) the editor cannot open;
            // resolving to one means there is no workspace definition to navigate to.
            _logger.LogDebug("Go-to-def: {Id} resolved to non-navigable origin {origin}", hit.Value.Id, symbol?.Origin);
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        _logger.LogDebug("Go-to-def: {Id} → {Uri}:{Line}", hit.Value.Id, fo.Uri, fo.Line);
        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(new LocationOrLocationLink(fo.ToLspLocation())));
    }

    private LocationOrLocationLinks? ResolveEnumDefinition(string id, GameIndex index)
    {
        // id format: "enum:{EnumName}/{ValueName}"
        var slash = id.IndexOf('/', "enum:".Length);
        if (slash < 0) return null;

        var enumName = id["enum:".Length..slash];
        var valueName = id[(slash + 1)..];

        if (!index.WorkspaceEnumValueDefinitions.TryGetValue(enumName, out var valueMap))
            return null;
        if (!valueMap.TryGetValue(valueName, out var origin) || !origin.IsNavigable)
            return null;

        _logger.LogDebug("Go-to-def (enum): {Id} → {Uri}:{Line}", id, origin.Uri, origin.Line);
        return new LocationOrLocationLinks(new LocationOrLocationLink(origin.ToLspLocation()));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}