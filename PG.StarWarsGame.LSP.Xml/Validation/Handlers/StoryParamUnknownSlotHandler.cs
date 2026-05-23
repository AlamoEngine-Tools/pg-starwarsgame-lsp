// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class StoryParamUnknownSlotHandler : XmlDiagnosticsHandler<StoryParamFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryParamFact fact, DiagnosticsContext ctx)
    {
        if (fact.Def is not null || fact.RawValue.Length == 0)
            return [];

        var prefix = fact.IsReward ? "Reward_Param" : "Event_Param";
        var tagName = $"{prefix}{fact.SlotPosition + 1}";
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"'{tagName}' is not used by {fact.EventType}.")
        ];
    }
}