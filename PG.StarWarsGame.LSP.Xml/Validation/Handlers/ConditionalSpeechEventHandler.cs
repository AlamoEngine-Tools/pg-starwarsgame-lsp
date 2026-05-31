// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class ConditionalSpeechEventHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    public override XmlValueType? HandledValueType => XmlValueType.ConditionalSpeechEvent;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ValueType != XmlValueType.ConditionalSpeechEvent)
            return [];

        var trimmed = fact.RawValue.Trim();
        var parts = trimmed.Split(',');
        // Needs at least condition + event name; all parts non-empty
        if (parts.Length < 2 || parts.Any(p => p.Trim().Length == 0))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{trimmed}' is not a valid conditional speech event for <{fact.Tag.Tag}>. Expected: UnitTypeCondition, SpeechEventName.")
            ];

        return [];
    }
}