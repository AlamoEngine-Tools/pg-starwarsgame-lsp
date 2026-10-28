// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlReferencesHandler : ReferencesHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlReferencesHandler> _logger;

    public XmlReferencesHandler(IGameIndexService indexService, ILogger<XmlReferencesHandler> logger,
        IEaWXmlContext eaWXmlContext, IFileHelper fileHelper)
    {
        _indexService = indexService;
        _logger = logger;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
    }

    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<LocationContainer?>(null);
        var index = _indexService.Current;

        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return Task.FromResult<LocationContainer?>(null);

        var hit = XmlPositionResolver.FindAtPosition(docIndex, request.Position.Line, request.Position.Character);
        if (hit is null)
            return Task.FromResult<LocationContainer?>(null);

        var id = hit.Value.Id;
        var locations = new List<Location>();
        // Every type is an id space of its own, so the list is the type's under the cursor. A story
        // symbol is one element indexed under two types and lists as one.
        var typeName = StoryRenameGuard.IsStorySymbol(id, index) ? null : index.TypeMeant(id, hit.Value.TypeName);

        if (index.AllGroupMemberships.TryGetValue(id, out var members) && members.Length > 0)
        {
            foreach (var m in members)
            {
                // Baseline members carry a non-openable game-relative path - skip them.
                if (m.MemberOrigin is not FileOrigin { IsNavigable: true } fo) continue;
                locations.Add(fo.ToLspLocation());
            }

            _logger.LogDebug("Find-refs (group): {Id} -> {Count} member(s)", id, locations.Count);
            return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
        }

        foreach (var r in index.ReferencesTo(id, typeName))
            locations.Add(new Location
            {
                Uri = r.DocumentUri,
                Range = new LspRange(
                    new Position(r.Line, r.Column),
                    new Position(r.Line, r.Column + r.Length))
            });

        if (request.Context.IncludeDeclaration)
            foreach (var s in index.DefinitionsOf(id, typeName))
            {
                if (s.Origin is not FileOrigin { IsNavigable: true } fo) continue;
                locations.Add(fo.ToLspLocation());
            }

        _logger.LogDebug("Find-refs: {Id} -> {Count} location(s)", id, locations.Count);
        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}