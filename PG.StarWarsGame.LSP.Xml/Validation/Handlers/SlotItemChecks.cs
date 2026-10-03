// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     The checks one item of a tuple value gets from its slot: an enum item against its enum, an
///     asset item against the asset catalog. Shared by the tuple slot handlers and the
///     <see cref="ListMapHandler" />, so a model item is judged the same wherever it sits.
/// </summary>
/// <remarks>
///     Object items are not here: the parser records them as references, and the reference pipeline
///     owns whether they exist.
/// </remarks>
internal static class SlotItemChecks
{
    /// <summary>
    ///     The enum check, or null when the item is a value of its enum or there is nothing to check
    ///     it against - no enum, or a dynamic one nobody indexed.
    /// </summary>
    public static XmlDiagnosticResult? Enum(XmlTagDefinition tag, TupleSlotDefinition slot, string value,
        DiagnosticsContext ctx)
    {
        if (slot.ReferenceKind != ReferenceKind.Enum)
            return null;

        var valid = EnumValueSets.GetValidValues(slot.Enum, ctx);
        if (valid is null || valid.Contains(value))
            return null;

        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"'{value}' is not a {slot.Enum!.Name} value, which the {slot.Label} of <{tag.Tag}> has to be.",
            Id: DiagnosticIds.TupleSlotEnumValue);
    }

    /// <summary>
    ///     The asset check, by the rules a whole tag of that kind gets, or null when the item resolves
    ///     or names no asset.
    /// </summary>
    public static XmlDiagnosticResult? Asset(XmlTagDefinition tag, TupleSlotDefinition slot, string value,
        DiagnosticsContext ctx)
    {
        if (AssetKindRules.For(slot.ReferenceKind) is not { } rule)
            return null;

        // A model is the one kind whose format the engine depends on outright: the name is loaded as
        // an .alo whatever it says.
        if (slot.ReferenceKind == ReferenceKind.ModelFile &&
            !value.EndsWith(".alo", StringComparison.OrdinalIgnoreCase))
            return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"'{value}' is not a valid model filename for the {slot.Label} of <{tag.Tag}>. Expected a .alo file.",
                Id: DiagnosticIds.TupleSlotModelFileFormat);

        if (AssetFileLookup.Resolves(ctx.Index.AssetFiles, value, rule.AllowedExtensions,
                rule.InterchangeableExtensions))
            return null;

        if (rule.ResolvesFromMegaTexture && ctx.IconNames?.Contains(value) == true)
            return null;

        return new XmlDiagnosticResult(rule.MissingSeverity,
            $"{rule.Noun} file '{value}' was not found in the game data or workspace asset files.",
            Id: slot.ReferenceKind == ReferenceKind.ModelFile
                ? DiagnosticIds.TupleSlotModelFileExistence
                : DiagnosticIds.TupleSlotAssetFileExistence);
    }
}
