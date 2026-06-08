// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlHoverHandler : IXmlHoverProvider
{
    private readonly ILspConfigurationProvider _config;
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlHoverHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlHoverHandler(
        IGameWorkspaceHost workspaceHost,
        IGameIndexService indexService,
        ISchemaProvider schema,
        ILspConfigurationProvider config,
        ILogger<XmlHoverHandler> logger,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper,
        IEaWXmlContext eaWXmlContext)
    {
        _workspaceHost = workspaceHost;
        _indexService = indexService;
        _schema = schema;
        _config = config;
        _logger = logger;
        _fileTypeRegistry = fileTypeRegistry;
        _fileHelper = fileHelper;
        _eaWXmlContext = eaWXmlContext;
    }

    public Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        _logger.LogDebug("Hover request at {Line}:{Character}",
            request.Position.Line, request.Position.Character);

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<Hover?>(null);
        if (!_workspaceHost.TryGetOrReadFromDisk(_fileHelper, uri, out var doc))
            return Task.FromResult<Hover?>(null);
        var hapDoc = XmlUtility.CreateHtmlDocument(doc.Text);

        // The document root element is always a file container, never a field tag.
        if (!XmlUtility.TryGetRootNode(hapDoc, out var rootNode) ||
            XmlUtility.GetLine(rootNode) == request.Position.Line)
            return Task.FromResult<Hover?>(null);

        var lineIndex = request.Position.Line;
        var charPos = request.Position.Character;

        // Find the element whose opening or closing tag is on this line.
        if (!XmlUtility.TryFindNode(hapDoc, lineIndex, out var node) &&
            !XmlUtility.TryFindNodeByClosingLine(hapDoc, lineIndex, out node))
        {
            _logger.LogWarning(
                "Hover request at {Line}:{Character} produced no result, because the tag could not be found.",
                lineIndex, charPos);
            return Task.FromResult<Hover?>(null);
        }

        var locale = _config.Current.Locale;

        // Cursor is not on a tag name — check if it is on a reference value.
        if (!XmlUtility.IsOnTagName(node!, lineIndex, charPos))
        {
            if (TryBuildReferenceHover(uri, lineIndex, charPos, locale, out var refHover))
                return Task.FromResult<Hover?>(refHover);
            if (TryBuildAssetHover(node!, lineIndex, charPos, out var assetHover))
                return Task.FromResult(assetHover);
            return Task.FromResult<Hover?>(null);
        }

        // Cursor is on a tag name — show tag or type hover.
        var typeDef = _schema.GetObjectType(node!.Name);

        // PascalCase lookup: ability class elements are snake_case in XML (e.g., lucky_shot_attack_ability)
        // but registered as PascalCase schema types (LuckyShotAttackAbility).
        typeDef ??= _schema.GetObjectType(XmlUtility.ToPascalCase(node.Name));

        // Ability sub-object field: walk parent chain to find the containing ability type.
        if (typeDef is null)
            if (TryResolveContainingAbilityType(node, out var abilityTypeName))
                typeDef = _schema.GetObjectType(abilityTypeName);

        // Registry-based fallback: for files with arbitrary element names, look up the type
        // via the registry and confirm the cursor is on a depth-1 type-container element.
        if (typeDef is null)
        {
            var fileTypes = _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(uri));
            if (!fileTypes.IsEmpty)
            {
                var registeredType = fileTypes
                    .Select(t => _schema.GetObjectType(t))
                    .FirstOrDefault(t => t?.NameTag is not null);
                if (registeredType is not null && XmlUtility.GetDepth(node) > 0)
                    typeDef = registeredType;
            }
        }

        if (typeDef is not null)
        {
            // single-node context; full ancestor walk lives in XmlDocumentFactProducer
            var hoverContext = new TagResolutionContext(typeDef.TypeName, XmlUtility.GetDepth(node), node);
            var typedTagDef = XmlTagResolver.Resolve(_schema, node.Name, hoverContext);

            if (typedTagDef is not null)
            {
                _logger.LogDebug("Hover resolved: type {TagName}::{tag}", typeDef.TypeName, typedTagDef.Tag);
                return Task.FromResult<Hover?>(HoverUtility.BuildTagHover(typeDef, typedTagDef, node, locale));
            }

            // we assume we're hovering over an XmlObject definition.
            if (node.ParentNode == rootNode)
                _logger.LogDebug("Hover resolved: type {TagName}::{id}", typeDef.TypeName,
                    XmlUtility.GetXmlObjectId(typeDef, node));
            return Task.FromResult<Hover?>(HoverUtility.BuildTypeHover(typeDef, node, locale));
        }

        // fallback if no type could be resolved.
        var tagDef = _schema.GetTag(node.Name);
        if (tagDef is not null)
        {
            _logger.LogDebug("Hover resolved: tag {TagName}", node.Name);
            return Task.FromResult<Hover?>(HoverUtility.BuildTagHover(tagDef, node, locale));
        }

        _logger.LogWarning(
            "Hover request at {Line}:{Character} produced no result, because the tag could not be found.",
            request.Position.Line, charPos);
        return Task.FromResult<Hover?>(null);
    }

    private bool TryResolveContainingAbilityType(HtmlNode node, out string? abilityTypeName)
    {
        for (var n = node.ParentNode; n?.ParentNode != null; n = n.ParentNode)
        {
            var parentTag = _schema.GetTag(n.ParentNode.Name);
            // AbilityDefinitionSubObjectList: child tag name IS the ability class → PascalCase is the schema type name
            if (parentTag?.ValueType == XmlValueType.AbilityDefinitionSubObjectList)
            {
                abilityTypeName = XmlUtility.ToPascalCase(n.Name);
                return true;
            }

            // GuiActivatedAbilityDefinitionSubObjectList: all children are Unit_Ability → fixed schema type UnitAbility
            if (parentTag?.ValueType == XmlValueType.GuiActivatedAbilityDefinitionSubObjectList)
            {
                abilityTypeName = "UnitAbility";
                return true;
            }
        }

        abilityTypeName = null;
        return false;
    }

    private bool TryBuildAssetHover(HtmlNode node, int line, int character, out Hover? hover)
    {
        var tagDef = _schema.GetTag(node.Name);
        if (tagDef is null || tagDef.ReferenceKind is not (
                ReferenceKind.TextureFile or ReferenceKind.ModelFile or
                ReferenceKind.AudioFile or ReferenceKind.MapFile))
        {
            hover = null;
            return false;
        }

        var value = node.InnerText.Trim();
        if (string.IsNullOrEmpty(value))
        {
            hover = null;
            return false;
        }

        hover = HoverUtility.BuildAssetReferenceHover(
            tagDef, value, _indexService.Current.AssetFiles, line, character, value.Length);
        return hover is not null;
    }

    private bool TryBuildReferenceHover(string uri, int line, int character, string locale, out Hover? hover)
    {
        var normalizedUri = _fileHelper.NormalizeUri(uri);
        var index = _indexService.Current;
        if (!index.Documents.TryGetValue(normalizedUri, out var docIndex))
        {
            hover = null;
            return false;
        }

        var reference = docIndex.References.FirstOrDefault(r =>
            r.Line == line && character >= r.Column && character < r.Column + r.Length);
        if (reference is null)
        {
            hover = null;
            return false;
        }

        var symbol = index.Resolve(reference.TargetId);
        if (symbol?.TypeName is null)
        {
            hover = null;
            return false;
        }

        var typeDef = _schema.GetObjectType(symbol.TypeName);
        if (typeDef is null)
        {
            hover = null;
            return false;
        }

        _logger.LogDebug("Hover resolved: reference {Id} → {Type}", reference.TargetId, typeDef.TypeName);
        hover = HoverUtility.BuildReferenceHover(typeDef, symbol.Id, reference, locale, symbol.Origin);
        return true;
    }
}