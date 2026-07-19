// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Named handler (ID: <c>context-name-pair</c>) for tags that hold a single
///     (ContextName, ValueName) pair - e.g. <c>Music_Event_List_Ambient</c> and
///     <c>Music_Event_List_Battle</c>. Replaces the default TupleList
///     handler for those tags via <c>validationOverride</c> in YAML.
/// </summary>
public sealed class ContextNamePairHandler : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    public string ValidationId => "context-name-pair";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var idx = fact.RawValue.IndexOf(',');
        if (idx < 0)
            return [Error(fact)];

        var context = fact.RawValue[..idx].Trim();
        var name = fact.RawValue[(idx + 1)..].Trim();

        if (context.Length == 0 || name.Length == 0)
            return [Error(fact)];

        var index = ctx.Index;
        if ((index.Baseline.Symbols.Count > 0 || index.WorkspaceDefinitions.Count > 0)
            && index.Resolve(name) is null)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{name}' could not be resolved as a music event for <{fact.Tag.Tag}>.")
            ];

        return [];
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact)
    {
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"'{fact.RawValue.Trim()}' is not a valid context-name pair for <{fact.Tag.Tag}>. Expected: ContextName, ValueName.");
    }
}