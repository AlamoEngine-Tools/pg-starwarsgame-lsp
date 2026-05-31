// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlDiagnosticsHandlerRegistry : IXmlDiagnosticsHandlerRegistry
{
    private readonly ILookup<Type, IXmlDiagnosticsHandler> _byFactType;
    private readonly Dictionary<string, List<IXmlDiagnosticsHandler>> _byValidationId;

    public XmlDiagnosticsHandlerRegistry(IEnumerable<IXmlDiagnosticsHandler> handlers)
    {
        var handlerList = handlers.ToList();
        _byFactType = handlerList
            .Where(h => h is not IXmlNamedDiagnosticsHandler)
            .ToLookup(h => h.FactType);

        _byValidationId = new Dictionary<string, List<IXmlDiagnosticsHandler>>(StringComparer.OrdinalIgnoreCase);
        foreach (var named in handlerList.OfType<IXmlNamedDiagnosticsHandler>())
        {
            if (!_byValidationId.TryGetValue(named.ValidationId, out var list))
                _byValidationId[named.ValidationId] = list = [];
            list.Add(named);
        }
    }

    public IEnumerable<XmlDiagnosticResult> Dispatch(XmlFact fact, DiagnosticsContext ctx)
    {
        if (fact is XmlTagValueFact tvf && tvf.Tag.ValidationOverride is { } ov)
        {
            var defaultHandlers = _byFactType[fact.GetType()];
            _byValidationId.TryGetValue(ov.ValidationId, out var namedList);
            var customHandlers = (namedList ?? [])
                .Where(h => h.FactType == fact.GetType());

            var sequence = ov.Mode switch
            {
                ValidationOverrideMode.Replace => customHandlers,
                ValidationOverrideMode.Additive when ov.Order == ValidationOverrideOrder.Prepend =>
                    customHandlers.Concat(defaultHandlers),
                _ => defaultHandlers.Concat(customHandlers)
            };

            return sequence.SelectMany(h => h.Handle(fact, ctx));
        }

        return _byFactType[fact.GetType()].SelectMany(h => h.Handle(fact, ctx));
    }

    public IEnumerable<XmlDiagnosticResult> DispatchAll(IEnumerable<XmlFact> facts, DiagnosticsContext ctx)
    {
        return facts.SelectMany(f => Dispatch(f, ctx));
    }
}