// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlDiagnosticsHandlerRegistry : IXmlDiagnosticsHandlerRegistry
{
    private readonly ILookup<Type, IXmlDiagnosticsHandler> _byFactType;

    public XmlDiagnosticsHandlerRegistry(IEnumerable<IXmlDiagnosticsHandler> handlers)
    {
        _byFactType = handlers.ToLookup(h => h.FactType);
    }

    public IEnumerable<XmlDiagnosticResult> Dispatch(XmlFact fact, DiagnosticsContext ctx)
    {
        return _byFactType[fact.GetType()].SelectMany(h => h.Handle(fact, ctx));
    }

    public IEnumerable<XmlDiagnosticResult> DispatchAll(IEnumerable<XmlFact> facts, DiagnosticsContext ctx)
    {
        return facts.SelectMany(f => Dispatch(f, ctx));
    }
}