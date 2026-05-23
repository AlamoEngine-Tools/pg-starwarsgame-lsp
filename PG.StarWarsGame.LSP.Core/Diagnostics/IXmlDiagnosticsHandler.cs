// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Converts a specific <see cref="XmlFact" /> into zero or more diagnostic observations.
///     Implementations are discovered via DI and registered in <see cref="IXmlDiagnosticsHandlerRegistry" />.
/// </summary>
public interface IXmlDiagnosticsHandler
{
    Type FactType { get; }

    IEnumerable<XmlDiagnosticResult> Handle(XmlFact fact, DiagnosticsContext ctx);
}