// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class InaccuracyMapHandler : CommaSeparatedPairHandlerBase
{
    protected override XmlValueType TargetType => XmlValueType.InaccuracyMap;

    protected override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var parts = SplitOnFirstComma(fact.RawValue.Trim());
        if (parts.Length != 2 || parts[0].Trim().Length == 0 ||
            !LenientFloatParser.TryParse(parts[1].Trim(), out _))
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{fact.RawValue.Trim()}' is not a valid inaccuracy map entry for <{fact.Tag.Tag}>. Expected: Category, Float.")
            ];

        // Slot 0 is a GameObjectCategoryType member (Fighter, Bomber, ...) - validate it against
        // the merged baseline+workspace value set, precisely positioned at the category token.
        var category = parts[0].Trim();
        var valid = EnumValueSets.GetValidValues(ctx.Schema.GetEnum("GameObjectCategoryType"), ctx);
        if (valid is not null && !valid.Contains(category))
            return
            [
                AtPairSlot(new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                        $"'{category}' is not a known GameObjectCategoryType value for <{fact.Tag.Tag}>."),
                    fact, 0)
            ];

        return [];
    }
}