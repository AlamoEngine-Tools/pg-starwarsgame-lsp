// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class DuplicateSymbolHandler : XmlDiagnosticsHandler<XmlSymbolFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlSymbolFact fact, DiagnosticsContext ctx)
    {
        var others = fact.AllDefinitions
            .Where(s => s.Origin is FileOrigin fo && !(fo.Uri == fact.DocumentUri && fo.Line == fact.Line))
            .ToList();

        if (others.Count == 0)
            return [];

        var othersText = string.Join(", ", others.Select(s => ((FileOrigin)s.Origin).Uri));
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"Duplicate symbol '{fact.SymbolId}': also defined in {othersText}.")
        ];
    }
}