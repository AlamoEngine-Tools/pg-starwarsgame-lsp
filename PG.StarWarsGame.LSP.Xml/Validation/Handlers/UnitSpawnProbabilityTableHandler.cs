// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class UnitSpawnProbabilityTableHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.UnitSpawnProbabilityTable;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        // Pairs are separated by commas AND/OR whitespace incl. newlines - the vanilla
        // Destruction_Survivors format puts one "Name, Float" pair per line. Tokenize with
        // original-offset tracking so every diagnostic points at its exact token.
        var tokens = XmlUtility.SplitListWithOffsets(fact.RawValue);
        if (tokens.Count == 0 || tokens.Count % 2 != 0)
            return [Error(fact, fact.RawValue.Trim())];

        var results = new List<XmlDiagnosticResult>();
        for (var i = 0; i < tokens.Count; i += 2)
        {
            var (name, nameOffset) = tokens[i];
            var (prob, probOffset) = tokens[i + 1];

            if (!LenientFloatParser.TryParse(prob, out var probability) ||
                probability < 0.0f || probability > 1.0f)
            {
                results.Add(AtToken(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{prob}' is not a valid spawn probability for <{fact.Tag.Tag}>. Expected a float in [0.0, 1.0]."),
                    fact, probOffset, prob.Length));
                continue;
            }

            var d = TryValidateGameObjectName(name, fact.Tag.Tag, ctx.Index);
            if (d is not null)
                results.Add(AtToken(d, fact, nameOffset, name.Length));
        }

        return results;
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact, string trimmed)
    {
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"'{trimmed}' is not a valid spawn probability table for <{fact.Tag.Tag}>. Expected pairs of UnitTypeName, Float [0.0, 1.0].");
    }
}