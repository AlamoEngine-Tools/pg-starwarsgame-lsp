// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlRenameHandler : IRenameProvider
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlRenameHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IDocumentTextSource _textSource;

    public XmlRenameHandler(
        IEaWXmlContext eaWXmlContext,
        IDocumentTextSource textSource,
        ISchemaProvider schema,
        ILogger<XmlRenameHandler> logger,
        IFileHelper fileHelper,
        IGameIndexService indexService)
    {
        _eaWXmlContext = eaWXmlContext;
        _textSource = textSource;
        _schema = schema;
        _logger = logger;
        _fileHelper = fileHelper;
        _indexService = indexService;
    }

    public bool Handles(string documentUri)
    {
        return documentUri.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    }

    public Task<WorkspaceEdit?> RenameAsync(RenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        return Task.FromResult(HandleRename(uri, request, _indexService.Current));
    }

    public Task<RangeOrPlaceholderRange?> PrepareAsync(PrepareRenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        return Task.FromResult(HandlePrepare(uri, request.Position.Line, request.Position.Character,
            _indexService.Current));
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

        // Grouping keys (Campaign_Set / SFXEvent Overlap_Test): the key has no single definition, so
        // the rename rewrites the shared value in place across every member occurrence.
        if (index.AllGroupMemberships.ContainsKey(hit.Value.Id))
            return GroupValueRenameBuilder.Build(hit.Value.Id, request.NewName, index, _logger);

        // Workspace-file references (plot manifests / threads / Lua scripts) rename the file on
        // disk and rewrite every reference's base name.
        if (index.Resolve(hit.Value.Id) is { Kind: GameSymbolKind.WorkspaceFile })
            return FileReferenceRenameBuilder.Build(hit.Value.Id, request.NewName, index, _textSource, _logger);

        if (StoryRenameGuard.IsStorySymbol(hit.Value.Id, index))
        {
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

        // Grouping keys rename the shared value in place: offer the tag-value span as the range.
        if (index.AllGroupMemberships.ContainsKey(hit.Value.Id))
            return new RangeOrPlaceholderRange(hit.Value.Range);

        // Workspace-file references rename only the base name (stem): offer just that sub-range,
        // keeping each reference's directory prefix and extension out of the edit.
        if (index.Resolve(hit.Value.Id) is { Kind: GameSymbolKind.WorkspaceFile })
        {
            if (!index.IsLeafOwned(hit.Value.Id)) return null;
            var stem = FileReferenceRenameBuilder.StemRange(uri, hit.Value.Range.Start.Line,
                hit.Value.Range.Start.Character,
                hit.Value.Range.End.Character - hit.Value.Range.Start.Character,
                WorkspaceFileKey.HasType(hit.Value.Id, WorkspaceFileKey.LuaScriptType), _textSource);
            return stem is null ? null : new RangeOrPlaceholderRange(stem);
        }

        if (StoryRenameGuard.IsStorySymbol(hit.Value.Id, index))
        {
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