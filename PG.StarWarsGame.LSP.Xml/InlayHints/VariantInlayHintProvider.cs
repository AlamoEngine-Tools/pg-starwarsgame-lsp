// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

/// <summary>
///     Annotates the <c>Variant_Of_Existing_Type</c> line with a one-line summary of the effective object:
///     how many tags are inherited, overridden/merged, and added. Resolves the enclosing object once.
/// </summary>
internal sealed class VariantInlayHintProvider : IXmlInlayHintProvider
{
    private readonly IVariantTagSource _tagSource;

    public VariantInlayHintProvider(IVariantTagSource tagSource)
    {
        _tagSource = tagSource;
    }

    public IEnumerable<InlayHint> Handle(InlayHintContext ctx)
    {
        if (ctx.TagDef.SemanticType != TagSemanticType.VariantParent)
            return [];

        var objectId = ParentObjectId(ctx.Node);
        if (objectId is null)
            return [];

        var effective = new EffectiveObjectResolver(ctx.Index, ctx.Schema, _tagSource).Resolve(objectId);
        if (!effective.Found)
            return [];

        var label = effective.Cyclic ? "cyclic inheritance" : Summary(effective);
        if (label is null)
            return [];

        return
        [
            new InlayHint
            {
                Position = new Position(ctx.Line, int.MaxValue),
                Label = label!,
                Kind = InlayHintKind.Type,
                PaddingLeft = true
            }
        ];
    }

    private static string? Summary(EffectiveObject effective)
    {
        int inherited = 0, overridden = 0, added = 0;
        foreach (var tag in effective.Tags)
            switch (tag.Provenance)
            {
                case VariantProvenance.Inherited:
                    inherited++;
                    break;
                case VariantProvenance.Overridden:
                case VariantProvenance.Merged:
                    overridden++;
                    break;
                case VariantProvenance.Added:
                    added++;
                    break;
            }

        var parts = new List<string>(3);
        if (inherited > 0) parts.Add($"inherits {inherited}");
        if (overridden > 0) parts.Add($"overrides {overridden}");
        if (added > 0) parts.Add($"adds {added}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string? ParentObjectId(HtmlNode variantTagNode)
    {
        var parent = variantTagNode.ParentNode;
        var attr = parent?.Attributes.FirstOrDefault(a =>
            a.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
        var value = attr?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
