// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A per-tech-level respawn table the engine will not accept.
/// </summary>
/// <remarks>
///     <para>
///         <c>Validate_Respawn_Times</c>, shared by <c>SlicerAbilityClass</c> (<c>00f1ed76</c>) and
///         <c>BlackMarketAbilityClass</c> (<c>00f213d6</c>), makes three checks in sequence and
///         returns false at the first failure. The caller then CLEARS both the min and max lists, so
///         one bad entry costs the whole table and the ability silently loses its respawn feature.
///     </para>
///     <para>
///         All three under one rule and one id, because that is how the engine gates them: any
///         failure discards the same thing. Reporting them separately would invite suppressing one
///         while the table stays just as unusable.
///     </para>
///     <para>
///         The zero check is a latch in the original: a zero at index 0 arms it, a zero later
///         without it fails, and a non-zero with it armed fails. That is all-or-nothing, and the
///         engine's own message says as much - the feature is switched off by zeroing every entry.
///     </para>
///     <para>
///         No quick fix. The engine's repair is to clear both lists, and putting the deletion of an
///         author's respawn table one keystroke away is not a service.
///     </para>
/// </remarks>
public sealed class RespawnTimeListHandler : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    /// <summary>One entry per tech level, and the engine hardcodes five of them.</summary>
    private const int TechLevels = 5;

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.RespawnTimeList;

    public string ValidationId => "respawn-time-list";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var tokens = XmlUtility.SplitList(fact.RawValue);
        if (tokens.Count == 0) return [];

        var values = new List<float>(tokens.Count);
        foreach (var token in tokens)
        {
            // A mistyped token is the FloatList handler's to report; there is no table to judge.
            if (!LenientFloatParser.TryParse(token.Trim(), out var value)) return [];
            values.Add(value);
        }

        if (Problem(fact.Tag.Tag, values) is not { } message) return [];

        return [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, message)];
    }

    /// <summary>The first failing check, in the engine's own order, or null when the table passes.</summary>
    private static string? Problem(string tag, IReadOnlyList<float> values)
    {
        if (values.Count != TechLevels)
            return $"<{tag}> needs one entry per tech level - {TechLevels} values, but has "
                   + $"{values.Count.ToString(CultureInfo.InvariantCulture)}. The engine discards "
                   + "the whole respawn table, min and max together";

        if (values.Any(v => v < 0.0f))
            return $"<{tag}> entries must be 0 or greater. The engine discards the whole respawn "
                   + "table, min and max together";

        var zeros = values.Count(v => v == 0.0f);
        if (zeros > 0 && zeros != values.Count)
            return $"<{tag}> uses zero as a respawn time, which is only allowed when every entry is "
                   + "zero - that is how the respawn feature is switched off. The engine discards "
                   + "the whole respawn table, min and max together";

        return null;
    }
}