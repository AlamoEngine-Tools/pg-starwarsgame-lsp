// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Convenience overloads that parse the raw text before producing facts. Production code that
///     runs several producers over the same document (the diagnostics publisher) should parse once
///     via <see cref="ParsedXmlDocument.Parse" /> and call the interface methods directly.
/// </summary>
public static class XmlFactProducerExtensions
{
    public static IReadOnlyList<XmlFact> Produce(
        this IXmlDocumentFactProducer producer, string xmlText, string documentUri)
    {
        return producer.Produce(ParsedXmlDocument.Parse(xmlText), documentUri);
    }

    public static IReadOnlyList<XmlFact> Produce(
        this IStoryFactProducer producer, string xmlText, string documentUri)
    {
        return producer.Produce(ParsedXmlDocument.Parse(xmlText), documentUri);
    }

    public static IReadOnlyList<XmlFact> Produce(
        this IXmlVariantFactProducer producer, string documentUri, string text, GameIndex index)
    {
        return producer.Produce(documentUri, ParsedXmlDocument.Parse(text), index);
    }

    public static IReadOnlyList<XmlFact> Produce(
        this IXmlLayerShadowFactProducer producer, string documentUri, string text, GameIndex index)
    {
        return producer.Produce(documentUri, ParsedXmlDocument.Parse(text), index);
    }
}
