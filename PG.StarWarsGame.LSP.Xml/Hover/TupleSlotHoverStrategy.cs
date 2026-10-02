// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.HoverStrategies;

/// <summary>
///     Hover on one enum or asset item of a slotted tuple value: which slot it fills, and what that
///     slot is.
/// </summary>
/// <remarks>
///     Object items hover through <see cref="ReferenceHoverStrategy" />, which runs first - the
///     parser records them as references. Enum and asset items have no reference to hover, so they
///     come here; an asset item gets the same hover a whole tag of its kind gets.
/// </remarks>
internal sealed class TupleSlotHoverStrategy : IXmlHoverStrategy
{
    public Hover? Handle(HoverContext ctx)
    {
        if (ctx.IsOnTagName)
            return null;

        var tagDef = ctx.Schema.GetTag(ctx.Node.Name);
        if (tagDef is null || tagDef.Slots.Count == 0)
            return null;

        var lineIndex = new LineOffsetIndex(ctx.HapDoc.Text);
        // A ListMap key is found by what it is, so the index decides it where the schema cannot.
        var isKey = tagDef.ValueType == XmlValueType.ListMap ? ListMapKeys.FromIndex(tagDef, ctx.Index) : null;
        foreach (var item in TupleItems.Read(tagDef, ctx.Node.InnerText, isKey))
        {
            var (line, column) = lineIndex.GetPosition(ctx.Node.InnerStartIndex + item.Offset);
            if (line != ctx.Line || ctx.Character < column || ctx.Character >= column + item.Text.Length)
                continue;

            return item.Slot?.ReferenceKind switch
            {
                ReferenceKind.Enum => EnumHover(item, ctx.Locale, line, column),
                ReferenceKind.ModelFile or ReferenceKind.TextureFile or ReferenceKind.AudioFile
                    or ReferenceKind.MapFile => HoverUtility.BuildAssetReferenceHover(
                        tagDef with { ReferenceKind = item.Slot.ReferenceKind }, item.Text, ctx.Index.AssetFiles,
                        line, column, item.Text.Length),
                _ => null
            };
        }

        return null;
    }

    private static Hover EnumHover(TupleItem item, string locale, int line, int column)
    {
        var slot = item.Slot!;
        var lines = new List<string> { $"**{slot.Label}** - `{slot.Enum?.Name ?? "enum"}`" };

        var value = slot.Enum?.Values.FirstOrDefault(v => v.Name.Equals(item.Text, StringComparison.OrdinalIgnoreCase));
        var description = value is null
            ? null
            : value.Description.GetValueOrDefault(locale) ?? value.Description.GetValueOrDefault("en");
        if (value is not null)
            lines.Add(description is null ? $"`{value.Name}`" : $"`{value.Name}`: {description}");

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = string.Join("\n\n", lines)
            }),
            Range = new LspRange(line, column, line, column + item.Text.Length)
        };
    }
}