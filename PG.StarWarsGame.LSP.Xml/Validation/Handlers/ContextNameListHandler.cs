// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Named handler (ID: <c>context-name-list</c>) for tags that hold multiple
///     (ContextName, ValueName) pairs in a single tag value — e.g.
///     <c>Land_Terrain_Model_Mapping</c>. Replaces the default TupleList
///     handler for that tag via <c>validationOverride</c> in YAML.
/// </summary>
public sealed class ContextNameListHandler : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    public string ValidationId => "context-name-list";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var trimmed = fact.RawValue.Trim();
        if (trimmed.Length == 0)
            return [Error(fact)];

        var parts = trimmed.Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        if (parts.Length == 0 || parts.Length % 2 != 0)
            return [Error(fact)];

        return [];
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact) =>
        new(XmlDiagnosticSeverity.Error,
            $"'{fact.RawValue.Trim()}' is not a valid context-name list for <{fact.Tag.Tag}>. Expected alternating ContextName, ValueName pairs.");
}
