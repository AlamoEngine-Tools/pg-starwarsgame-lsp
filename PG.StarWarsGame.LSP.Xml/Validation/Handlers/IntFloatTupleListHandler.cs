// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class IntFloatTupleListHandler : CommaSeparatedPairHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.IntFloatTupleList;

    protected override XmlValueType TargetType => XmlValueType.IntFloatTupleList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var tokens = XmlUtility.SplitListWithOffsets(fact.RawValue);
        if (tokens.Count == 0 || tokens.Count % 2 != 0)
            return [Error(fact, fact.RawValue.Trim(), ctx.Messages)];

        var results = new List<XmlDiagnosticResult>();
        for (var i = 0; i < tokens.Count; i += 2)
        {
            var (intToken, intOffset) = tokens[i];
            var (floatToken, floatOffset) = tokens[i + 1];

            if (!LenientIntParser.TryParse(intToken, out var intValue, out var wasFloat))
            {
                results.Add(AtToken(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        ctx.Messages.Format("IntFloatTupleList_IsNotAValidInteger", intToken, fact.Tag.Tag)),
                    fact, intOffset, intToken.Length));
            }
            else if (wasFloat)
            {
                // Consistent int-slot policy: floats are accepted (the game truncates) but warned.
                var corrected = intValue.ToString();
                results.Add(AtToken(new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                        ctx.Messages.Format("IntFloatTupleList_IsAFloatButExpects", intToken, fact.Tag.Tag, corrected),
                        SuggestedFix: corrected, Id: DiagnosticIds.IntFloatTupleListFloatWhereIntExpected),
                    fact, intOffset, intToken.Length));
            }

            if (!EngineNumber.TryParseFloat(floatToken, out _))
                results.Add(AtToken(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        ctx.Messages.Format("IntFloatTupleList_IsNotAValidFloat", floatToken, fact.Tag.Tag)),
                    fact, floatOffset, floatToken.Length));
        }

        return results;
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact, string trimmed, IMessageCatalog messages)
    {
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            messages.Format("IntFloatTupleList_IsNotAValidInt", trimmed, fact.Tag.Tag));
    }
}