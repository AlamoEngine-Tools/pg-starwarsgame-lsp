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
            .Where(s => s.Origin is FileOrigin fo &&
                        !(string.Equals(fo.Uri, fact.DocumentUri, StringComparison.OrdinalIgnoreCase) &&
                          fo.Line == fact.Line))
            .ToList();

        if (others.Count == 0)
            return [];

        var othersText = string.Join(", ", others.Select(s => ((FileOrigin)s.Origin).Uri));
        // Duplicate detection is downgraded to Information for engine placeholders, which are
        // intentionally redefined across files (e.g. Default SFXEvent, Default TradeRouteLine).
        var severity = EnginePlaceholders.IsPlaceholder(fact.SymbolId)
            ? XmlDiagnosticSeverity.Information
            : XmlDiagnosticSeverity.Error;

        // Each editor-openable other definition rides along as a clickable related location;
        // baseline (game-relative) origins stay message-only.
        var related = others
            .Select(s => (FileOrigin)s.Origin)
            .Where(fo => fo.IsNavigable)
            .Select(fo => new XmlRelatedLocation(fo.Uri, fo.Line, fo.Column,
                $"Other definition of '{fact.SymbolId}'"))
            .ToList();

        return
        [
            new XmlDiagnosticResult(severity,
                $"Duplicate symbol '{fact.SymbolId}': also defined in {othersText}.",
                RelatedLocations: related.Count > 0 ? related : null)
        ];
    }
}