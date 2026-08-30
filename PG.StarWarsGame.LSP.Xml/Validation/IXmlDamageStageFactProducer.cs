// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Cross-validates an object's declared damage stages against its own model: a stage named in
///     <c>Land_Damage_Alternates</c> should have something in the model tagged <c>_ALT&lt;n&gt;</c>
///     for it, or the unit reaches that state and does not change.
///     <para>
///         Its own producer rather than a cross-tag rule because it needs the model catalogue.
///         <see cref="IXmlCrossTagRule" /> is deliberately document-local - it sees an object's child
///         tags and nothing else - and this question cannot be answered from the XML alone.
///     </para>
/// </summary>
public interface IXmlDamageStageFactProducer
{
    IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index);
}
