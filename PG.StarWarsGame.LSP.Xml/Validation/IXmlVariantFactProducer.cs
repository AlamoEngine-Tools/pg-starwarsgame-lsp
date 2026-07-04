// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Produces facts about variant inheritance (<c>Variant_Of_Existing_Type</c>): circular chains,
///     overrides of ignored tags, and redundant overrides. Needs the parsed document (for tag
///     positions) and the index (for chain resolution via <see cref="EffectiveObjectResolver" />).
/// </summary>
public interface IXmlVariantFactProducer
{
    IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index);
}