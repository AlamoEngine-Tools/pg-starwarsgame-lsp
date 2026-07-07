// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Flags '|' (OR) usage in reference-list tags that don't support it. The engine only
///     understands OR-expressions on tags explicitly marked <see cref="TagSemanticType.PrerequisiteExpression" />
///     (e.g. Required_Special_Structures) - everywhere else a '|' is silently treated as just
///     another separator by the reference splitter, so it looks like OR but behaves as AND with
///     no warning. Offers a quick fix that replaces '|' with ',' to nudge modders towards the
///     syntax that actually works.
/// </summary>
public sealed class DisallowedOrOperatorHandler : XmlDiagnosticsHandler<XmlTagValueFact>
{
    // Must mirror the "multiValue" set in XmlGameDocumentParser.SplitReferenceNames - these are
    // the value types whose reference splitter treats '|' as a plain separator instead of OR.
    private static readonly XmlValueType[] TargetTypes =
    [
        XmlValueType.GameObjectTypeReferenceList,
        XmlValueType.TypeReferenceList,
        XmlValueType.NameReferenceList,
        XmlValueType.PerFactionObjectList
    ];

    public override IEnumerable<XmlValueType> HandledValueTypes => TargetTypes;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (Array.IndexOf(TargetTypes, fact.Tag.ValueType) < 0)
            return [];

        if (fact.Tag.SemanticType == TagSemanticType.PrerequisiteExpression)
            return [];

        if (!fact.RawValue.Contains('|'))
            return [];

        var corrected = fact.RawValue.Replace('|', ',');
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"<{fact.Tag.Tag}> does not support '|' (OR) - every listed value is required (AND only). " +
                "This engine only supports OR-expressions on tags explicitly documented for it " +
                "(e.g. Required_Special_Structures).",
                SuggestedFix: corrected)
        ];
    }
}
