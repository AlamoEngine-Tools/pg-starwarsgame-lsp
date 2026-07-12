// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml;

/// <summary>
///     On-demand access to the XML diagnostics pipeline for a single document, WITHOUT
///     publishing. The push path (<see cref="XmlDiagnosticsPublisher" />) only runs for open
///     documents; consumers like the story graph need diagnostics for a campaign's thread files
///     whether or not any editor has them open.
/// </summary>
public interface IXmlDiagnosticsCollector
{
    IReadOnlyList<Diagnostic> Collect(string uri, string text, GameIndex index);
}
