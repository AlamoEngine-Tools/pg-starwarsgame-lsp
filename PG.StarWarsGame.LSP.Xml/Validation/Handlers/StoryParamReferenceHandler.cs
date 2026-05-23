// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class StoryParamReferenceHandler : XmlDiagnosticsHandler<StoryParamFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryParamFact fact, DiagnosticsContext ctx)
    {
        if (fact.Def is null || fact.RawValue.Length == 0)
            return [];

        var refType = fact.Def.ObjectType?.TypeName;
        if (refType is null)
            return [];

        if (fact.Def.ValueType == XmlValueType.NameReference)
        {
            if (ctx.Index.Resolve(fact.RawValue) is null)
                return
                [
                    new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                        $"'{fact.RawValue}' is not a recognized {refType}.")
                ];
            return [];
        }

        if (fact.Def.ValueType == XmlValueType.NameReferenceList)
        {
            foreach (var token in fact.RawValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (ctx.Index.Resolve(token) is null)
                    return
                    [
                        new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                            $"'{token}' is not a recognized {refType}.")
                    ];
            return [];
        }

        return [];
    }
}