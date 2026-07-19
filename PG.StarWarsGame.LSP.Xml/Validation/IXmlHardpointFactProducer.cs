// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Cross-validates a hardpoint's bone tags against the models of the game objects that mount it
///     (#53): a bone the hardpoint names must exist on every model the mounting object declares.
///     <para>
///         Works in both directions because LSP publishes diagnostics per document - a producer running
///         for <c>Hardpoints.xml</c> cannot put a squiggle in <c>Spaceunitscapital.xml</c>. So when a
///         hardpoint file is validated the mounting objects are looked up, and when a unit file is
///         validated the hardpoints it mounts are looked up.
///     </para>
/// </summary>
public interface IXmlHardpointFactProducer
{
    IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index);
}
