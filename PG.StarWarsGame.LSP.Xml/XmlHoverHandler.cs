// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlHoverHandler : HoverHandlerBase
{
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly ILogger<XmlHoverHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlHoverHandler(
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        ILspConfigurationProvider config,
        ILogger<XmlHoverHandler> logger,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper)
    {
        _workspaceHost = workspaceHost;
        _schema = schema;
        _config = config;
        _logger = logger;
        _fileTypeRegistry = fileTypeRegistry;
        _fileHelper = fileHelper;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        _logger.LogDebug("Hover request at {Line}:{Character}",
            request.Position.Line, request.Position.Character);

        var uri = request.TextDocument.Uri.ToString();
        if (!_workspaceHost.TryGet(uri, out var doc))
            return Task.FromResult<Hover?>(null);
        var text = doc.Text;
        var hapDoc = XmlUtility.CreateHtmlDocument(doc.Text);

        // The document root element is always a file container, never a field tag.
        if (!XmlUtility.TryGetRootNode(hapDoc, out var rootNode) ||
            XmlUtility.GetLine(rootNode) == request.Position.Line)
            return Task.FromResult<Hover?>(null);

        // TODO: ensure that we don't hover over node content, but only the node tag itself should emit hovers.
        // TODO: inject hover over references here, so hovers over values don't show the tag but the peek of the target.

        var lines = text.Split('\n');
        var lineIndex = request.Position.Line;
        if (lineIndex >= lines.Length)
            return Task.FromResult<Hover?>(null);

        if (!XmlUtility.TryFindNode(hapDoc, lineIndex, out var node))
        {
            _logger.LogWarning(
                "Hover request at {Line}:{Character} produced no result, because the tag could not be found.",
                lineIndex, request.Position.Character);
            return Task.FromResult<Hover?>(null);
        }

        Debug.Assert(node != null, nameof(node) + " != null");

        var locale = _config.Current.Locale;

        // Try element-name-based type lookup first (works when element name = type name).
        var typeDef = _schema.GetObjectType(node.Name);

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
            var typedTagDef = _schema.GetTagsForType(typeDef.TypeName).FirstOrDefault(t =>
                string.Equals(t.Tag, node.Name, StringComparison.OrdinalIgnoreCase));

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
            request.Position.Line, request.Position.Character);
        return Task.FromResult<Hover?>(null);
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}