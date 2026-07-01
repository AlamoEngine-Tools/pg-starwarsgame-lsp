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
    ///     covers multiple types or is not scoped to a single value type.
    ///     Prefer <see cref="HandledValueTypes" /> for coverage checks.
    /// </summary>
    XmlValueType? HandledValueType => null;

    /// <summary>
    ///     All <see cref="XmlValueType" />s this handler validates. Defaults to
    ///     a single-element array when <see cref="HandledValueType" /> is non-null, or empty otherwise.
    ///     Override in handlers that cover more than one value type.
    /// </summary>
    IEnumerable<XmlValueType> HandledValueTypes => HandledValueType.HasValue ? [HandledValueType.Value] : [];

    IEnumerable<XmlDiagnosticResult> Handle(XmlFact fact, DiagnosticsContext ctx);
}