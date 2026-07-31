// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

/// <summary>
///     Offers the four suppression scopes on any XML diagnostic that carries an id.
///     <para>
///         The actions themselves are built by <see cref="SuppressionCodeActionBuilder" />, shared
///         with the other languages. All that is XML's own is where each scope anchors: the line the
///         diagnostic sits on, the element that encloses it, and the root element.
///     </para>
/// </summary>
internal sealed class SuppressDiagnosticCodeActionProvider : IXmlCodeActionProvider
{
    private readonly IFileHelper _fileHelper;
    private readonly IXmlParseCache _parseCache;
    private readonly ISchemaProvider _schema;

    public SuppressDiagnosticCodeActionProvider(
        IXmlParseCache parseCache, IFileHelper fileHelper, ISchemaProvider schema)
    {
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _schema = schema;
    }

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        if (ctx.Diagnostic.Code?.String is not { } code || !DiagnosticId.TryParse(code, out var id))
            return [];

        var parsed = _parseCache.GetOrParse(_fileHelper.NormalizeUri(ctx.DocumentUri.ToString()));
        if (parsed is null) return [];

        var line = ctx.Diagnostic.Range.Start.Line;
        var points = new List<SuppressionInsertionPoint> { new(SuppressionScope.Node, line) };

        // Offered only where there is an object to attach it to - never a bogus fourth action.
        if (EnclosingObject(parsed.Html, line) is { } objectNode)
            points.Add(new SuppressionInsertionPoint(
                SuppressionScope.Object,
                XmlUtility.GetLine(objectNode) + 1,
                $"<{XmlUtility.GetOriginalTagName(objectNode, parsed.Text)}>"));

        points.Add(new SuppressionInsertionPoint(SuppressionScope.File, FirstBodyLine(parsed.Html)));

        return SuppressionCodeActionBuilder.Build(
            ctx.DocumentUri, ctx.Diagnostic, id, SuppressionCommentFormat.Xml, parsed.Lines, points);
    }

    /// <summary>
    ///     Line to put a file-scoped directive on: just inside the root element, so it sits with
    ///     the content it governs rather than above the XML declaration.
    /// </summary>
    private static int FirstBodyLine(HtmlDocument document)
    {
        var root = document.DocumentNode.ChildNodes
            .FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
        return root is null ? 0 : XmlUtility.GetLine(root) + 1;
    }

    private HtmlNode? EnclosingObject(HtmlDocument document, int line)
    {
        if (!XmlUtility.TryFindNode(document, line, out var node) || node is null) return null;

        for (var n = node; n is not null; n = n.ParentNode)
            if (n.NodeType == HtmlNodeType.Element && IsObjectNode(n))
                return n;

        return null;
    }

    private bool IsObjectNode(HtmlNode node)
    {
        return _schema.GetObjectType(node.Name) is not null
               || _schema.GetObjectType(XmlUtility.ToPascalCase(node.Name)) is not null;
    }
}
