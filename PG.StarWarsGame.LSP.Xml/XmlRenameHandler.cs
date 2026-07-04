// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlRenameHandler : IXmlRenameProvider
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<XmlRenameHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlRenameHandler(
        IEaWXmlContext eaWXmlContext,
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IFileHelper fileHelper,
        ILogger<XmlRenameHandler> logger)
    {
        _eaWXmlContext = eaWXmlContext;
        _workspaceHost = workspaceHost;
        _schema = schema;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
    {
        if (!_eaWXmlContext.IsEaWXmlFile(uri)) return null;
        if (!index.Documents.TryGetValue(uri, out var docIndex)) return null;

        var hit = XmlPositionResolver.FindAtPosition(docIndex, request.Position.Line, request.Position.Character);
        if (hit is null) return null;

        if (TryParseEnumValueId(hit.Value.Id, out var enumName, out var valueName))
            return DynamicEnumValueRenameBuilder.Build(enumName, valueName, request.NewName, index, _workspaceHost,
                _fileHelper, _logger);

        return XmlObjectRenameBuilder.Build(hit.Value.Id, request.NewName, index, _schema, _workspaceHost, _fileHelper,
            _logger);
    }

    public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
    {
        if (!_eaWXmlContext.IsEaWXmlFile(uri)) return null;
        if (!index.Documents.TryGetValue(uri, out var docIndex)) return null;

        var hit = XmlPositionResolver.FindAtPosition(docIndex, line, character);
        if (hit is null) return null;

        if (TryParseEnumValueId(hit.Value.Id, out var enumName, out var valueName))
        {
            if (!index.WorkspaceEnumValueDefinitions.TryGetValue(enumName, out var valueMap) ||
                !valueMap.TryGetValue(valueName, out var origin) || !origin.IsNavigable)
                return null;
            if (index.LayerRankOfUri(origin.Uri) != index.LeafLayerRank)
                return null;
            return new RangeOrPlaceholderRange(hit.Value.Range);
        }

        if (!index.IsLeafOwned(hit.Value.Id)) return null;

        return new RangeOrPlaceholderRange(hit.Value.Range);
    }

    // id format: "enum:{EnumName}/{ValueName}" — see XmlGameDocumentParser.CollectEnumReferences.
    private static bool TryParseEnumValueId(string id, out string enumName, out string valueName)
    {
        enumName = "";
        valueName = "";
        if (!id.StartsWith("enum:", StringComparison.Ordinal)) return false;

        var slash = id.IndexOf('/', "enum:".Length);
        if (slash < 0) return false;

        enumName = id["enum:".Length..slash];
        valueName = id[(slash + 1)..];
        return true;
    }
}