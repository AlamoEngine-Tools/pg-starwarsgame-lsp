// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Strongly-typed base for <see cref="IXmlDiagnosticsHandler" /> implementations.
///     Subclasses implement <see cref="Handle(TFact, DiagnosticsContext)" />; the non-generic
///     dispatch in <see cref="IXmlDiagnosticsHandler.Handle" /> filters by type automatically.
/// </summary>
public abstract class XmlDiagnosticsHandler<TFact> : IXmlDiagnosticsHandler
    where TFact : XmlFact
{
    public Type FactType => typeof(TFact);

    public IEnumerable<XmlDiagnosticResult> Handle(XmlFact fact, DiagnosticsContext ctx)
    {
        return fact is TFact typed ? Handle(typed, ctx) : [];
    }

    protected abstract IEnumerable<XmlDiagnosticResult> Handle(TFact fact, DiagnosticsContext ctx);
}