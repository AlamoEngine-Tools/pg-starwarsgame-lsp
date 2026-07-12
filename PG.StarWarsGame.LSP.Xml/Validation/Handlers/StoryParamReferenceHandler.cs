// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class StoryParamReferenceHandler : XmlDiagnosticsHandler<StoryParamFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryParamFact fact, DiagnosticsContext ctx)
    {
        if (fact.Def is null || fact.RawValue.Length == 0)
            return [];

        // The raw referenceType covers params whose target is not a types.yaml object type
        // (e.g. Planet without an explicit referenceKind). Story-scoped references resolve
        // campaign-wide, not index-wide — their existence checks belong to the story graph
        // diagnostics, so they are skipped here.
        var refType = fact.Def.ObjectType?.TypeName ?? fact.Def.ReferenceTypeName;
        if (refType is null || StoryReferenceTypes.IsStoryScoped(refType))
            return [];

        if (fact.Def.ValueType == XmlValueType.NameReference)
        {
            if (ctx.Index.Resolve(fact.RawValue) is null)
                return
                [
                    new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{fact.RawValue}' is not a recognized {refType}.")
                ];
            return [];
        }

        if (fact.Def.ValueType == XmlValueType.NameReferenceList)
        {
            foreach (var token in ListValueConstants.PrepareValueForSplit(fact.RawValue)
                         .Split(ListValueConstants.GetListSeparators(), StringSplitOptions.RemoveEmptyEntries))
                if (ctx.Index.Resolve(token) is null)
                    return
                    [
                        new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                            $"'{token}' is not a recognized {refType}.")
                    ];
            return [];
        }

        return [];
    }
}