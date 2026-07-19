// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlRenameHandler : IXmlRenameProvider
{
    private readonly ILspConfigurationProvider? _configProvider;
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly ILogger<XmlRenameHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IDocumentTextSource _textSource;

    // A null configProvider (test convenience) means every feature flag reads as enabled.
    public XmlRenameHandler(
        IEaWXmlContext eaWXmlContext,
        IDocumentTextSource textSource,
        ISchemaProvider schema,
        ILogger<XmlRenameHandler> logger,
        ILspConfigurationProvider? configProvider = null)
    {
        _eaWXmlContext = eaWXmlContext;
        _textSource = textSource;
        _schema = schema;
        _logger = logger;
        _configProvider = configProvider;
    }

    public WorkspaceEdit? HandleRename(string uri, RenameParams request, GameIndex index)
    {
        if (!_eaWXmlContext.IsEaWXmlFile(uri)) return null;
        if (!index.Documents.TryGetValue(uri, out var docIndex)) return null;

        var hit = XmlPositionResolver.FindAtPosition(docIndex, request.Position.Line, request.Position.Character);
        if (hit is null) return null;

        if (TryParseEnumValueId(hit.Value.Id, out var enumName, out var valueName))
            return DynamicEnumValueRenameBuilder.Build(enumName, valueName, request.NewName, index, _textSource,
                _logger);

        if (StoryRenameGuard.IsStorySymbol(hit.Value.Id, index))
        {
            if (!(_configProvider?.Current.Features.Story.Rename ?? true)) return null;
            if (StoryRenameGuard.Check(hit.Value.Id, request.NewName, index) is { } objection)
                throw new InvalidOperationException(objection);
        }

        return XmlObjectRenameBuilder.Build(hit.Value.Id, request.NewName, index, _schema, _textSource, _logger);
    }

    public RangeOrPlaceholderRange? HandlePrepare(string uri, int line, int character, GameIndex index)
    {
        if (!_eaWXmlContext.IsEaWXmlFile(uri)) return null;
        if (!index.Documents.TryGetValue(uri, out var docIndex)) return null;

        var hit = XmlPositionResolver.FindAtPosition(docIndex, line, character);
        if (hit is null) return null;

        if (StoryRenameGuard.IsStorySymbol(hit.Value.Id, index))
        {
            if (!(_configProvider?.Current.Features.Story.Rename ?? true)) return null;
            if (StoryRenameGuard.Check(hit.Value.Id, null, index) is { } objection)
                throw new InvalidOperationException(objection);
        }

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

    // id format: "enum:{EnumName}/{ValueName}" - see XmlGameDocumentParser.CollectEnumReferences.
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