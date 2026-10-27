// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public interface IXmlDiagnosticsHandlerRegistry
{
    IEnumerable<XmlDiagnosticResult> Dispatch(XmlFact fact, DiagnosticsContext ctx);

    IEnumerable<XmlDiagnosticResult> DispatchAll(IEnumerable<XmlFact> facts, DiagnosticsContext ctx);
}