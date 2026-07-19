// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Validates the mode half of a <see cref="TagSemanticType.PlanetModePairList" /> - repeated
///     (planet, mode) pairs flattened into one comma-separated list, as used by
///     <c>Campaign.Autoresolve_Exclusion_Locations</c>. Only handles tags with ValueType =
///     TypeReferenceList AND SemanticType = PlanetModePairList.
///     <para>
///         The planet half is indexed as an object reference by the parser, so the generic
///         unresolved-reference pipeline already covers it; this handler owns what that pipeline
///         cannot see - that the modes are real battle modes and that no planet was left without one.
///     </para>
/// </summary>
public sealed class PlanetModeExclusionListHandler : CommaSeparatedPairHandlerBase
{
    private const string ModeEnumName = "StoryBattleMode";

    protected override XmlValueType TargetType => XmlValueType.TypeReferenceList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.SemanticType != TagSemanticType.PlanetModePairList)
            return [];

        var tokens = XmlUtility.SplitListWithOffsets(fact.RawValue).ToList();
        if (tokens.Count == 0)
            return [];

        var results = new List<XmlDiagnosticResult>();
        var validModes = EnumValueSets.GetValidValues(ctx.Schema.GetEnum(ModeEnumName), ctx);

        for (var slot = 1; slot < tokens.Count; slot += 2)
        {
            var (mode, offset) = tokens[slot];
            if (validModes is null || validModes.Contains(mode)) continue;

            results.Add(AtToken(
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{mode}' is not a known {ModeEnumName} value. Each entry in <{fact.Tag.Tag}> is a "
                    + "planet followed by the mode it is excluded in."),
                fact, offset, mode.Length));
        }

        // An odd token count means the last planet has no mode, so the engine reads the pairs out of
        // step from there on - point at the planet that is missing its partner rather than the value.
        if (tokens.Count % 2 != 0)
        {
            var (planet, offset) = tokens[^1];
            results.Add(AtToken(
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{planet}' has no mode. <{fact.Tag.Tag}> is a flat list of (planet, mode) pairs, "
                    + "so every planet must be followed by the mode it is excluded in."),
                fact, offset, planet.Length));
        }

        return results;
    }
}
