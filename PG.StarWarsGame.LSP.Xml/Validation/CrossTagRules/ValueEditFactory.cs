// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

/// <summary>
///     Builds an edit over a tag's VALUE, for rules offering the engine's own correction.
/// </summary>
/// <remarks>
///     The offsets come from the parsed node and the document's offset index, which is the same
///     pair the diagnostic's own position was built from - so a repair cannot point somewhere the
///     diagnostic does not.
/// </remarks>
internal static class ValueEditFactory
{
    /// <summary>
    ///     An edit replacing <paramref name="node" />'s inner text with <paramref name="newText" />,
    ///     or null where that cannot be expressed as one single-line replacement.
    /// </summary>
    /// <remarks>
    ///     A value split across lines is refused rather than approximated. It is vanishingly rare
    ///     for the flags and numbers these rules touch, and a wrong edit is worse than no fix: the
    ///     author would accept a one-click correction and get a mangled file.
    /// </remarks>
    public static XmlDiagnosticEdit? ReplaceValue(HtmlNode? node, LineOffsetIndex lineIndex, string newText)
    {
        if (node is null) return null;

        var start = node.InnerStartIndex;
        var length = node.InnerLength;
        if (start < 0 || length < 0) return null;

        var text = node.InnerHtml;
        if (text.Contains('\n') || text.Contains('\r')) return null;

        var (line, column) = lineIndex.GetPosition(start);
        return new XmlDiagnosticEdit(line, column, length, newText);
    }
}
