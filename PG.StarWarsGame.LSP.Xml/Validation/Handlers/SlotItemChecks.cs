// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
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
            ctx.Messages.Format("SlotItemChecks_IsNotAValueWhich", value, slot.Enum!.Name, slot.Label, tag.Tag),
            Id: DiagnosticIds.TupleSlotEnumValue);
    }

    /// <summary>
    ///     The asset check, by the rules a whole tag of that kind gets, or null when the item resolves
    ///     or names no asset. A texture resolving to a PNG is the information a whole tag gets, and one
    ///     opened from below a layer's own <c>.dds</c> the warning; an item carries one result, so the
    ///     warning stands before the information (#420).
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
                ctx.Messages.Format("SlotItemChecks_IsNotAValidModel", value, slot.Label, tag.Tag),
                Id: DiagnosticIds.TupleSlotModelFileFormat);

        if (AssetFileLookup.Resolve(ctx.Index.AssetFiles, value, rule) is { } found)
        {
            if (rule.TextureLookup &&
                AssetFileLookup.UnreachedDds(ctx.Index.AssetFiles, value, found, rule.AllowedExtensions) is { } dds)
                return new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    ctx.Messages.Format("TextureDdsNotReached_OpensTheNameAsWritten", value, found, dds),
                    Id: DiagnosticIds.TextureDdsNotReached);

            return rule.TextureLookup && GameTextureName.IsPng(found)
                ? new XmlDiagnosticResult(XmlDiagnosticSeverity.Information,
                    ctx.Messages.Format("TextureIsPng_ResolvesToAPngFile", value), Id: DiagnosticIds.TextureIsPng)
                : null;
        }

        if (rule.ResolvesFromMegaTexture && ctx.IconNames?.Contains(value) == true)
            return null;

        return new XmlDiagnosticResult(rule.MissingSeverity,
            ctx.Messages.Format("SlotItemChecks_FileWasNotFoundIn", ctx.Messages.Format(rule.NounKey), value)
            + (rule.TextureLookup
                ? AssetFileLookup.UnsearchedTgaHint(ctx.Index.AssetFiles, value, rule.AllowedExtensions, ctx.Messages)
                : string.Empty),
            Id: slot.ReferenceKind == ReferenceKind.ModelFile
                ? DiagnosticIds.TupleSlotModelFileExistence
                : DiagnosticIds.TupleSlotAssetFileExistence);
    }
}