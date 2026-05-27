// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Converts a specific <see cref="XmlFact" /> into zero or more diagnostic observations.
///     Implementations are discovered via DI and registered in <see cref="IXmlDiagnosticsHandlerRegistry" />.
/// </summary>
public interface IXmlDiagnosticsHandler
{
    Type FactType { get; }

    /// <summary>
    ///     The <see cref="XmlValueType" /> this handler validates, or <c>null</c> if the handler
    ///     is not scoped to a single value type (e.g. structural or named-override handlers).
    ///     Used by coverage checks to verify that every value type has at least one validator.
    /// </summary>
    XmlValueType? HandledValueType => null;

    IEnumerable<XmlDiagnosticResult> Handle(XmlFact fact, DiagnosticsContext ctx);
}