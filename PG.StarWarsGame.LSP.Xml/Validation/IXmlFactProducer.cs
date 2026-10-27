// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     One source of <see cref="XmlFact" />s for a document. The diagnostics publisher runs every
///     registered producer over one parse of the document and one index snapshot, in registration
///     order, and hands the facts to the handlers (#307). A new rule is a producer, a fact and a
///     handler, registered once - the publisher knows none of them by name.
/// </summary>
public interface IXmlFactProducer
{
    /// <summary>The phase name the publisher logs the producer's time under when a document is slow.</summary>
    string Name { get; }

    /// <summary>
    ///     Whether the producer has anything to say about this document at all, decided from its
    ///     canonical URI before the parse is looked at: the story producer reads story files only,
    ///     the hardcoded-set producer skips a file whose types carry no such tag.
    /// </summary>
    bool AppliesTo(string documentUri);

    /// <summary>
    ///     The facts of <paramref name="document" />, which is <paramref name="documentUri" />'s text
    ///     parsed once for every producer, against <paramref name="index" />, the one snapshot of the
    ///     workspace this run reads. A producer that reads only the index ignores the document, and
    ///     one that reads only the document ignores the index.
    /// </summary>
    IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index);
}