// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

/// <summary>
///     Marks each line of a <c>Variant_Of_Existing_Type</c> object that redefines an inherited tag
///     with what it displaced - "overrides 99", "adds to 3 inherited" (#73). Phrased like the summary
///     hint on the variant declaration line.
///     <para>
///         The marker is informational; the full untruncated value, and the base tag itself, are in the
///         effective-object expansion. Making it navigable was tried and dropped - see the comment in
///         <see cref="Handle" />.
///     </para>
/// </summary>
internal sealed class VariantOverrideInlayHintProvider : IXmlInlayHintProvider
{
    // Inherited values can be long comma-separated lists; the full text belongs in the expansion.
    private const int MaxValueLength = 32;

    // Resolving an effective object walks and merges the whole base chain, and Handle runs per tag
    // node - so without memoisation a variant with 50 tags would resolve 50 times. Derived from the
    // GameIndex snapshot so a re-index drops the whole table rather than serving stale merges (#311).
    private const string EffectiveObjectsKey = "inlayEffectiveObjects";

    private readonly IVariantTagSource _tagSource;

    public VariantOverrideInlayHintProvider(IVariantTagSource tagSource)
    {
        _tagSource = tagSource;
    }

    public IEnumerable<InlayHint> Handle(InlayHintContext ctx)
    {
        // The declaration line carries the summary hint; annotating it again would be noise.
        if (ctx.TagDef.SemanticType == TagSemanticType.VariantParent)
            return [];

        var objectNode = ctx.Node.ParentNode;
        if (objectNode is null)
            return [];

        // Cheap gate first: only objects that actually declare a variant parent can override
        // anything, and they are a small minority of a typical file.
        var objectId = VariantObjectId(objectNode, ctx.Schema);
        if (objectId is null)
            return [];

        var effective = ResolveCached(ctx, objectId);
        if (!effective.Found || effective.Cyclic)
            return [];

        var tag = effective.DisplacingTag(ctx.Node.Name);

        if (tag is null)
            return [];

        var text = Label(tag, effective, ctx.Messages);
        if (text is null)
            return [];

        // Informational only. Making the marker navigable was tried and abandoned: a label part's
        // Location is not a jump target in VS Code (it runs go-to-definition at that position, which
        // finds nothing on a base tag), and routing a Command through the client did not land on the
        // right place either. The effective-object expansion is where the base is inspected.
        return
        [
            new InlayHint
            {
                Position = new Position(ctx.Line, ctx.LineEndCharacter),
                Label = text!,
                Kind = InlayHintKind.Type,
                PaddingLeft = true
            }
        ];
    }

    /// <summary>
    ///     Mirrors the phrasing of the summary hint on the <c>Variant_Of_Existing_Type</c> line
    ///     ("inherits 12 · overrides 3 · adds 1"): a verb plus what it acted on.
    /// </summary>
    private static string? Label(EffectiveTag tag, EffectiveObject effective, IMessageCatalog messages)
    {
        switch (tag.Provenance)
        {
            case VariantProvenance.Overridden:
                return tag.BaseValue is null
                    ? messages.Format("VariantOverrideInlayHint_OverridesBase")
                    : messages.Format("VariantOverrideInlayHint_Overrides", Truncate(tag.BaseValue));

            case VariantProvenance.Merged:
                // A repeatable additive tag contributes one entry per element, so the useful figure
                // is how many the object inherits - naming a single one of them would be arbitrary.
                var inherited = effective.Tags.Count(t =>
                    t.TagName.Equals(tag.TagName, StringComparison.OrdinalIgnoreCase)
                    && t.Provenance == VariantProvenance.Inherited);
                if (inherited > 0)
                    return messages.Format("VariantOverrideInlayHint_AddsInherited", inherited);
                return tag.BaseValue is null
                    ? messages.Format("VariantOverrideInlayHint_AddsBase")
                    : messages.Format("VariantOverrideInlayHint_Merges", Truncate(tag.BaseValue));

            default:
                return null;
        }
    }

    private static string Truncate(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[])[' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxValueLength ? collapsed : collapsed[..MaxValueLength] + "...";
    }

    private EffectiveObject ResolveCached(InlayHintContext ctx, string objectId)
    {
        // Keyed by type as well: one id may name objects of several types.
        var here = ctx.DefinitionHere(objectId);
        var key = here?.TypeName is { } type ? objectId + "|" + type : objectId;
        var perIndex = ctx.Index.Derived(EffectiveObjectsKey, _ => new Dictionary<string, EffectiveObject>());
        lock (perIndex)
        {
            if (perIndex.TryGetValue(key, out var cached))
                return cached;
        }

        var resolver = new EffectiveObjectResolver(ctx.Index, ctx.Schema, _tagSource);
        var resolved = here is not null ? resolver.Resolve(here) : resolver.Resolve(objectId);

        lock (perIndex)
        {
            perIndex[key] = resolved;
        }

        return resolved;
    }

    /// <summary>
    ///     The enclosing object's id, but only when it declares a variant parent. Null otherwise.
    /// </summary>
    private static string? VariantObjectId(HtmlNode objectNode, ISchemaProvider schema)
    {
        var declaresVariant = objectNode.ChildNodes.Any(n =>
            n.NodeType == HtmlNodeType.Element &&
            schema.GetTag(n.Name)?.SemanticType == TagSemanticType.VariantParent);
        if (!declaresVariant)
            return null;

        var attr = objectNode.Attributes.FirstOrDefault(a =>
            a.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
        var value = attr?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}