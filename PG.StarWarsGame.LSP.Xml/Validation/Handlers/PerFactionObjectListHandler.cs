// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class PerFactionObjectListHandler : SingleValueTypeHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.PerFactionObjectList;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var tokenList = XmlUtility.SplitListWithOffsets(fact.RawValue);

        if (tokenList.Count == 0)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'' is not a valid per-faction object list for <{fact.Tag.Tag}>. Expected: FactionName[, ObjectName, ...].")
            ];

        // Baseline absent → index not yet loaded, skip semantic faction check.
        // Game object existence is validated by the reference pipeline.
        if (ctx.Index.Baseline.Symbols.IsEmpty)
            return [];

        // Walk faction groups: each group is FactionName followed by ≥0 object names.
        // A token is a faction opener when its resolved symbol has TypeName "Faction".
        var results = new List<XmlDiagnosticResult>();
        var i = 0;
        while (i < tokenList.Count)
        {
            var (factionToken, factionOffset) = tokenList[i];
            if (!IsFaction(ctx, factionToken))
            {
                var (line, col) = TokenPosition(fact, factionOffset);
                results.Add(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{factionToken}' is not a known faction for <{fact.Tag.Tag}>.",
                    OverrideLine: line, OverrideColumn: col, OverrideLength: factionToken.Length));
                break;
            }

            i++;
            while (i < tokenList.Count && !IsFaction(ctx, tokenList[i].Token))
                i++;
        }

        return results;
    }

    private static bool IsFaction(DiagnosticsContext ctx, string token) =>
        ctx.Index.Resolve(token) is { TypeName: { } tn } &&
        tn.Equals("Faction", StringComparison.OrdinalIgnoreCase);

    private static (int Line, int Col) TokenPosition(XmlTagValueFact fact, int tokenOffset)
    {
        var raw = fact.RawValue;
        var lineInc = 0;
        var lastNewlineAt = -1;
        for (var i = 0; i < tokenOffset && i < raw.Length; i++)
        {
            if (raw[i] != '\n') continue;
            lineInc++;
            lastNewlineAt = i;
        }
        var col = lastNewlineAt < 0 ? fact.Column + tokenOffset : tokenOffset - lastNewlineAt - 1;
        return (fact.Line + lineInc, col);
    }
}