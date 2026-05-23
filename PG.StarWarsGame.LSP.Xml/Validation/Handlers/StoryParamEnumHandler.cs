// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class StoryParamEnumHandler : XmlDiagnosticsHandler<StoryParamFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryParamFact fact, DiagnosticsContext ctx)
    {
        if (fact.Def is null || fact.Def.ValueType != XmlValueType.DynamicEnumValue || fact.RawValue.Length == 0)
            return [];

        var enumDef = fact.Def.Enum;
        if (enumDef is null)
            return [];

        foreach (var token in fact.RawValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!enumDef.Values.Any(v => string.Equals(v.Name, token, StringComparison.OrdinalIgnoreCase)))
                return
                [
                    new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                        $"'{token}' is not a valid {enumDef.Name} value.")
                ];

        return [];
    }
}