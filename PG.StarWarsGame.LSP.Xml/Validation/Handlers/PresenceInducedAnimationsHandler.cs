// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Named handler (ID: <c>presence-induced-animations</c>) for
///     <c>Presence_Induced_Animations</c> tags. Base-game entries use a single
///     animation state ID with a trailing comma (e.g. <c>Attention,</c>).
///     Replaces the default PerFactionObjectList handler via <c>validationOverride</c>.
/// </summary>
public sealed class PresenceInducedAnimationsHandler : XmlDiagnosticsHandler<XmlTagValueFact>,
    IXmlNamedDiagnosticsHandler
{
    public string ValidationId => "presence-induced-animations";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = fact.RawValue.Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        if (parts.Length == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid animation entry for <{fact.Tag.Tag}>. Expected at least one animation state ID.")
            ];

        return [];
    }
}
