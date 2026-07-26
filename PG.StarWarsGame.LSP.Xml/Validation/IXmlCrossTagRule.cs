// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     A validation that needs an object's whole child set rather than one tag value. Evaluated by
///     <see cref="XmlDocumentFactProducer" /> once per object node.
///     <paramref name="lineIndex" /> is the document's line index, so a rule can anchor a fact on a
///     leaf VALUE via <c>XmlUtility.GetValuePosition</c> / <c>GetInnerOffsetValuePosition</c>
///     instead of only on the opening tag - never derive value positions from HAP's per-node
///     line/column.
/// </summary>
public interface IXmlCrossTagRule
{
    IEnumerable<XmlFact> Evaluate(
        HtmlNode objectNode,
        IReadOnlyDictionary<string, IReadOnlyList<HtmlNode>> childrenByName,
        string documentUri,
        LineOffsetIndex lineIndex);
}
