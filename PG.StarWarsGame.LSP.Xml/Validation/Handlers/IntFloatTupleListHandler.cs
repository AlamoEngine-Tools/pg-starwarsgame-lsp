// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class IntFloatTupleListHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.IntFloatTupleList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var tokens = XmlUtility.SplitListWithOffsets(fact.RawValue);
        if (tokens.Count == 0 || tokens.Count % 2 != 0)
            return [Error(fact, fact.RawValue.Trim())];

        var results = new List<XmlDiagnosticResult>();
        for (var i = 0; i < tokens.Count; i += 2)
        {
            var (intToken, intOffset) = tokens[i];
            var (floatToken, floatOffset) = tokens[i + 1];

            if (!LenientIntParser.TryParse(intToken, out var intValue, out var wasFloat))
            {
                results.Add(AtToken(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{intToken}' is not a valid integer for <{fact.Tag.Tag}>. Expected alternating integer and float pairs."),
                    fact, intOffset, intToken.Length));
            }
            else if (wasFloat)
            {
                // Consistent int-slot policy: floats are accepted (the game truncates) but warned.
                var corrected = intValue.ToString();
                results.Add(AtToken(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                        $"'{intToken}' is a float but <{fact.Tag.Tag}> expects an integer. Did you mean {corrected}?",
                        SuggestedFix: corrected),
                    fact, intOffset, intToken.Length));
            }

            if (!LenientFloatParser.TryParse(floatToken, out _))
                results.Add(AtToken(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{floatToken}' is not a valid float for <{fact.Tag.Tag}>. Expected alternating integer and float pairs."),
                    fact, floatOffset, floatToken.Length));
        }

        return results;
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact, string trimmed)
    {
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"'{trimmed}' is not a valid int-float tuple list for <{fact.Tag.Tag}>. Expected alternating integer and float pairs.");
    }
}