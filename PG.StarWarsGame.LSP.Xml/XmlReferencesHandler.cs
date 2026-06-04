// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
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

        if (index.AllGroupMemberships.TryGetValue(id, out var members) && members.Length > 0)
        {
            foreach (var m in members)
            {
                if (m.MemberOrigin is not FileOrigin fo) continue;
                var col = fo.Column ?? 0;
                locations.Add(new Location
                {
                    Uri = fo.Uri,
                    Range = new LspRange(new Position(fo.Line, col), new Position(fo.Line, col))
                });
            }

            _logger.LogDebug("Find-refs (group): {Id} → {Count} member(s)", id, locations.Count);
            return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
        }

        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
                locations.Add(new Location
                {
                    Uri = r.DocumentUri,
                    Range = new LspRange(
                        new Position(r.Line, r.Column),
                        new Position(r.Line, r.Column + r.Length))
                });

        if (request.Context.IncludeDeclaration && index.WorkspaceDefinitions.TryGetValue(id, out var defs))
            foreach (var s in defs)
            {
                if (s.Origin is not FileOrigin fo) continue;
                locations.Add(new Location
                {
                    Uri = fo.Uri,
                    Range = new LspRange(new Position(fo.Line, fo.Column ?? 0), new Position(fo.Line, fo.Column ?? 0))
                });
            }

        _logger.LogDebug("Find-refs: {Id} → {Count} location(s)", id, locations.Count);
        return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}