// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

/// <summary>
///     The document a structural request is about, with its element spans, or null when it is not
///     an EaW XML file. Shared by folding, selection range and matching-tag highlight.
/// </summary>
internal static class XmlStructureRequest
{
    public static ParsedXmlDocument? Resolve(TextDocumentIdentifier doc, IXmlParseCache parseCache,
        IEaWXmlContext context, IFileHelper fileHelper)
    {
        var uri = fileHelper.NormalizeUri(doc.Uri.ToString());
        return context.IsEaWXmlFile(uri) ? parseCache.GetOrParse(uri) : null;
    }

    public static Position At(ParsedXmlDocument parsed, int offset)
    {
        var (line, col) = parsed.LineIndex.GetPosition(offset);
        return new Position(line, col);
    }

    public static LspRange Span(ParsedXmlDocument parsed, (int Start, int Length) span)
    {
        return new LspRange(At(parsed, span.Start), At(parsed, span.Start + span.Length));
    }

    public static int Offset(ParsedXmlDocument parsed, Position position)
    {
        return XmlUtility.PositionToOffset(parsed.Text, position.Line, position.Character);
    }

    public static bool Within((int Start, int Length) span, int offset)
    {
        return span.Start <= offset && offset <= span.Start + span.Length;
    }
}

/// <summary>Folds every element spanning several lines (to the line before its end tag) and every multi-line comment.</summary>
public sealed class XmlFoldingRangeHandler(IXmlParseCache parseCache, IEaWXmlContext context, IFileHelper fileHelper)
    : FoldingRangeHandlerBase
{
    public override Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request, CancellationToken ct)
    {
        var parsed = XmlStructureRequest.Resolve(request.TextDocument, parseCache, context, fileHelper);
        if (parsed is null) return Task.FromResult<Container<FoldingRange>?>(null);

        var folds = new List<FoldingRange>();
        foreach (var e in parsed.Spans.Elements)
        {
            if (e.EndTag is not { } end) continue;
            var startLine = XmlStructureRequest.At(parsed, e.Start).Line;
            var endLine = XmlStructureRequest.At(parsed, end.Start).Line - 1;
            if (endLine > startLine) folds.Add(new FoldingRange { StartLine = startLine, EndLine = endLine });
        }

        foreach (var c in parsed.Spans.Comments)
        {
            var startLine = XmlStructureRequest.At(parsed, c.Start).Line;
            var endLine = XmlStructureRequest.At(parsed, c.Start + c.Length).Line;
            if (endLine > startLine)
                folds.Add(new FoldingRange
                    { StartLine = startLine, EndLine = endLine, Kind = FoldingRangeKind.Comment });
        }

        return Task.FromResult<Container<FoldingRange>?>(new Container<FoldingRange>(folds));
    }

    protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
        FoldingRangeCapability capability, ClientCapabilities clientCapabilities)
    {
        return new FoldingRangeRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}

/// <summary>Grows a selection from a tag name to its element, then to each enclosing element.</summary>
public sealed class XmlSelectionRangeHandler(IXmlParseCache parseCache, IEaWXmlContext context, IFileHelper fileHelper)
    : SelectionRangeHandlerBase
{
    public override Task<Container<SelectionRange>?> Handle(SelectionRangeParams request, CancellationToken ct)
    {
        var parsed = XmlStructureRequest.Resolve(request.TextDocument, parseCache, context, fileHelper);
        if (parsed is null) return Task.FromResult<Container<SelectionRange>?>(null);

        var results = new List<SelectionRange>();
        foreach (var position in request.Positions)
        {
            var offset = XmlStructureRequest.Offset(parsed, position);
            var element = parsed.Spans.ElementAt(offset);

            // Built outermost first, so each range can point at its parent.
            SelectionRange? chain = null;
            var path = new List<XmlElementSpan>();
            for (var e = element; e is not null; e = e.Parent) path.Add(e);
            for (var i = path.Count - 1; i >= 0; i--)
                chain = new SelectionRange
                {
                    Range = new LspRange(XmlStructureRequest.At(parsed, path[i].Start),
                        XmlStructureRequest.At(parsed, path[i].End)),
                    Parent = chain! // the outermost range has no parent; OmniSharp types the property non-null
                };

            if (element is not null)
            {
                var name = XmlStructureRequest.Within(element.StartName, offset) ? element.StartName
                    : element.EndName is { } en && XmlStructureRequest.Within(en, offset) ? en
                    : ((int, int)?)null;
                if (name is { } n)
                    chain = new SelectionRange { Range = XmlStructureRequest.Span(parsed, n), Parent = chain! };
            }

            results.Add(chain ?? new SelectionRange { Range = new LspRange(position, position) });
        }

        return Task.FromResult<Container<SelectionRange>?>(new Container<SelectionRange>(results));
    }

    protected override SelectionRangeRegistrationOptions CreateRegistrationOptions(
        SelectionRangeCapability capability, ClientCapabilities clientCapabilities)
    {
        return new SelectionRangeRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}

/// <summary>On a start or end tag name, highlights both names of the element.</summary>
public sealed class XmlDocumentHighlightHandler(
    IXmlParseCache parseCache,
    IEaWXmlContext context,
    IFileHelper fileHelper)
    : DocumentHighlightHandlerBase
{
    public override Task<DocumentHighlightContainer?> Handle(DocumentHighlightParams request, CancellationToken ct)
    {
        var parsed = XmlStructureRequest.Resolve(request.TextDocument, parseCache, context, fileHelper);
        if (parsed is null) return Task.FromResult<DocumentHighlightContainer?>(null);

        var offset = XmlStructureRequest.Offset(parsed, request.Position);
        var element = parsed.Spans.Elements.FirstOrDefault(e =>
            XmlStructureRequest.Within(e.StartName, offset) ||
            (e.EndName is { } en && XmlStructureRequest.Within(en, offset)));
        if (element is null) return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer());

        var marks = new List<DocumentHighlight>
        {
            new() { Range = XmlStructureRequest.Span(parsed, element.StartName), Kind = DocumentHighlightKind.Text }
        };
        if (element.EndName is { } end)
            marks.Add(new DocumentHighlight
                { Range = XmlStructureRequest.Span(parsed, end), Kind = DocumentHighlightKind.Text });
        return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer(marks));
    }

    protected override DocumentHighlightRegistrationOptions CreateRegistrationOptions(
        DocumentHighlightCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentHighlightRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}