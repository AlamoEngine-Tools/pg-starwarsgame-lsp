// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Suppression;

/// <summary>
///     Finds suppression directives in a document's XML comments and resolves each to the lines it
///     covers.
///     <para>
///         The grammar itself lives in <see cref="SuppressionDirectiveParser" />, shared with every
///         other language. All that is XML-specific is stripping <c>&lt;!--</c> and <c>--&gt;</c>
///         and walking the tree to find what a scope keyword points at.
///     </para>
///     <para>
///         Ranges are resolved here, while the DOM is in hand, so applying a suppression later is a
///         line comparison instead of a second tree walk on every publish.
///     </para>
/// </summary>
public static class SuppressionCommentParser
{
    /// <summary>
    ///     Parses every directive in the document. <paramref name="isObjectNode" /> decides which
    ///     elements count as an "object" for <c>-object</c> scope; the caller supplies it because
    ///     what counts as an object is a schema question, not a syntax one.
    /// </summary>
    public static SuppressionScan Parse(
        HtmlDocument document, Func<HtmlNode, bool> isObjectNode)
    {
        var ranges = new List<SuppressionRange>();
        var problems = new List<SuppressionCommentProblem>();

        foreach (var comment in document.DocumentNode.DescendantsAndSelf()
                     .Where(n => n.NodeType == HtmlNodeType.Comment))
        {
            var result = SuppressionDirectiveParser.Parse(CommentBody(comment));
            if (!result.IsDirective) continue;

            var directiveLine = XmlUtility.GetLine(comment);
            problems.AddRange(result.Problems.Select(p => p.At(directiveLine)));

            if (result.Directive is not { } directive) continue;

            var (start, end) = directive.Scope switch
            {
                SuppressionScope.File => SuppressionSpans.File,
                SuppressionScope.Object => SuppressionSpans.ForTarget(
                    SpanOf(EnclosingObject(comment, isObjectNode)), directiveLine),
                _ => SuppressionSpans.ForTarget(SpanOf(NextElement(comment)), directiveLine)
            };

            // One range per id: they share everything but which diagnostics they name, so the rest
            // of the pipeline never has to know a directive could carry a list.
            foreach (var matcher in directive.Matchers)
                ranges.Add(new SuppressionRange(
                    matcher, directive.Scope, start, end, directiveLine, directive.Reason));
        }

        return new SuppressionScan(ranges, problems);
    }

    /// <summary>Comment text without its delimiters, which is what the shared grammar expects.</summary>
    private static string CommentBody(HtmlNode comment)
    {
        var text = comment.OuterHtml.AsSpan().Trim();
        if (text.StartsWith("<!--")) text = text[4..];
        if (text.EndsWith("-->")) text = text[..^3];
        return text.ToString();
    }

    private static (int Start, int End)? SpanOf(HtmlNode? target)
    {
        return target is null ? null : (XmlUtility.GetLine(target), XmlUtility.GetEndLine(target));
    }

    private static HtmlNode? NextElement(HtmlNode comment)
    {
        for (var n = comment.NextSibling; n is not null; n = n.NextSibling)
            if (n.NodeType == HtmlNodeType.Element)
                return n;

        return null;
    }

    private static HtmlNode? EnclosingObject(HtmlNode comment, Func<HtmlNode, bool> isObjectNode)
    {
        for (var n = comment.ParentNode; n is not null; n = n.ParentNode)
            if (n.NodeType == HtmlNodeType.Element && isObjectNode(n))
                return n;

        return null;
    }
}
