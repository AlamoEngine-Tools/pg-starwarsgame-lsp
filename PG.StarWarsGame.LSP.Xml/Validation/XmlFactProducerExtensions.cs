// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Convenience overloads that parse the raw text before producing facts. Production code that
///     runs several producers over the same document (the diagnostics publisher) parses once via
///     <see cref="ParsedXmlDocument.Parse" /> and calls the interface method directly.
/// </summary>
public static class XmlFactProducerExtensions
{
    /// <summary>Facts of <paramref name="xmlText" /> against an empty index, for a producer that reads the document.</summary>
    public static IReadOnlyList<XmlFact> Produce(this IXmlFactProducer producer, string xmlText, string documentUri)
    {
        return producer.Produce(documentUri, ParsedXmlDocument.Parse(xmlText), GameIndex.Empty);
    }

    /// <summary>Facts of <paramref name="text" /> against <paramref name="index" />.</summary>
    public static IReadOnlyList<XmlFact> Produce(
        this IXmlFactProducer producer, string documentUri, string text, GameIndex index)
    {
        return producer.Produce(documentUri, ParsedXmlDocument.Parse(text), index);
    }

    /// <summary>Facts against <paramref name="index" /> alone, for a producer that reads only the index.</summary>
    public static IReadOnlyList<XmlFact> Produce(this IXmlFactProducer producer, string documentUri, GameIndex index)
    {
        return producer.Produce(documentUri, ParsedXmlDocument.Parse(string.Empty), index);
    }
}