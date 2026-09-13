// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Builds the edit that writes a missing child tag into an object, formatted the way the
///     document already formats its children.
/// </summary>
/// <remarks>
///     <para>
///         The layout is DERIVED, never chosen: the new line copies the indentation of the object's
///         last child and the line ending the file already uses. There is no formatting policy here
///         and there should not be one - the fix has to look like the author wrote it.
///     </para>
///     <para>
///         HAP is the parser and must not be the writer. It lower-cases <see cref="HtmlNode.Name" />,
///         so building the node through HAP and re-serializing the object would emit
///         <c>&lt;bomb_type&gt;</c> and reformat the surrounding markup. Nor can this lean on a
///         formatter afterwards: the server implements on-type formatting only, which a workspace
///         edit does not trigger.
///     </para>
/// </remarks>
internal static class TagInsertionFactory
{
    /// <summary>
    ///     An edit inserting <c>&lt;tag&gt;value&lt;/tag&gt;</c> as the object's last child, or null
    ///     where the formatting cannot be derived from what is already there.
    /// </summary>
    public static XmlDiagnosticEdit? InsertLastChild(
        HtmlNode objectNode, LineOffsetIndex lineIndex, string tag, string value)
    {
        var lastChild = objectNode.ChildNodes
            .LastOrDefault(n => n.NodeType == HtmlNodeType.Element);

        // Nothing to copy a layout from. Inventing an indent would be the policy decision this
        // whole approach exists to avoid, so the diagnostic simply carries no fix.
        if (lastChild is null) return null;

        var offset = lastChild.OuterStartIndex + lastChild.OuterHtml.Length;
        if (offset < 0) return null;

        var element = $"<{tag}>{value}</{tag}>";
        var inner = objectNode.InnerHtml;

        // A one-line object stays on one line: breaking it would reformat markup the author chose.
        var newText = inner.Contains('\n')
            ? LineEnding(inner) + ChildIndent(inner) + element
            : element;

        var (line, column) = lineIndex.GetPosition(offset);
        return new XmlDiagnosticEdit(line, column, 0, newText);
    }

    /// <summary>The line ending this object is already written with.</summary>
    private static string LineEnding(string inner)
    {
        return inner.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    /// <summary>
    ///     The leading whitespace of the last line that opens an element - the indentation this
    ///     object's children actually use, tabs or spaces as written.
    /// </summary>
    private static string ChildIndent(string inner)
    {
        foreach (var line in inner.Split('\n').Reverse())
        {
            var text = line.TrimEnd('\r');
            var content = text.TrimStart(' ', '\t');
            if (!content.StartsWith('<')) continue;

            return text[..(text.Length - content.Length)];
        }

        return string.Empty;
    }
}
