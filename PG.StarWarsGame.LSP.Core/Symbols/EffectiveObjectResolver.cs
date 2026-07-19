// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Merges an object with its <c>Variant_Of_Existing_Type</c> base chain into a single effective object.
///     Source-agnostic: workspace tags come from the injected <see cref="IVariantTagSource" /> and shadow
///     baseline tags from <see cref="BaselineIndex.ObjectTags" />. Detects cycles in the base chain.
/// </summary>
public sealed class EffectiveObjectResolver
{
    private static readonly char[] ListSeparators = [',', ' ', '\t', '\n', '\r'];

    private readonly GameIndex _index;
    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _workspaceSource;

    public EffectiveObjectResolver(GameIndex index, ISchemaProvider schema, IVariantTagSource workspaceSource)
    {
        _index = index;
        _schema = schema;
        _workspaceSource = workspaceSource;
    }

    public EffectiveObject Resolve(string objectId)
    {
        var root = _index.Resolve(objectId);
        if (root is null)
            return new EffectiveObject(objectId, null, false, false, null,
                ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

        var (chain, cyclic, cycleAt) = BuildChain(root);

        // Apply innermost base first so derived layers override; preserve first-seen tag order.
        var state = new Dictionary<string, Accum>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var layer = chain[i];
            var isRootLayer = i == chain.Count - 1;
            foreach (var tag in TagsFor(layer))
            {
                var schemaTag = _schema.GetTag(tag.TagName);
                if (schemaTag?.SemanticType == TagSemanticType.VariantParent)
                    continue; // the variant-declaration tag is metadata, not part of the effective object

                var mode = schemaTag?.VariantMode ?? VariantMode.Replace;

                // An additive tag that may appear several times (Death_Clone and friends) is a list
                // of independent elements, not one list-valued element: each occurrence carries its
                // own tuple. Unioning their tokens would drop a repeated leading token - e.g. a
                // second Damage_Fire - as a "duplicate" and detach the rest of that entry from it.
                // Keep every occurrence as its own tag instead.
                if (mode == VariantMode.Merge && (schemaTag?.MultipleAllowed ?? false))
                {
                    AppendOccurrence(state, order, layer.Id, tag);
                    continue;
                }

                ApplyTag(state, order, layer.Id, tag, mode, isRootLayer);
            }
        }

        var single = chain.Count == 1;
        var tags = order
            .SelectMany(key => state[key].Occurrences is { } occurrences
                ? occurrences.Select(o => ToEffectiveTag(o, root.Id, single))
                : [ToEffectiveTag(state[key], root.Id, single)])
            .ToImmutableArray();

        return new EffectiveObject(root.Id, root.TypeName, true, cyclic, cycleAt,
            chain.Select(c => c.Id).ToImmutableArray(), tags);
    }

    private (List<GameSymbol> Chain, bool Cyclic, string? CycleAt) BuildChain(GameSymbol root)
    {
        var chain = new List<GameSymbol>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var current = root;
        while (current is not null)
        {
            if (!visited.Add(current.Id))
                return (chain, true, current.Id);

            chain.Add(current);
            if (string.IsNullOrEmpty(current.VariantBaseId))
                break;
            current = _index.Resolve(current.VariantBaseId);
        }

        return (chain, false, null);
    }

    private IReadOnlyList<VariantTag> TagsFor(GameSymbol symbol)
    {
        var workspace = _workspaceSource.TryGetTags(symbol.Id);
        if (workspace is not null)
            return workspace;

        if (!_index.Baseline.ObjectTags.TryGetValue(symbol.Id, out var baselineTags))
            return [];

        return baselineTags
            .Select(bt => new VariantTag(bt.TagName, bt.Value, bt.Fragment, bt.StartLine,
                TagOrigin(symbol.Origin, bt.StartLine)))
            .ToList();
    }

    private static SymbolOrigin? TagOrigin(SymbolOrigin objectOrigin, int startLine)
    {
        return objectOrigin switch
        {
            FileOrigin fo => fo with { Line = startLine, Column = null },
            MegArchiveOrigin mo => mo with { Line = startLine, Column = null },
            _ => objectOrigin
        };
    }

    /// <summary>
    ///     Records one occurrence of a repeatable additive tag, preserving the order every layer
    ///     contributed them in (base's first, then the variant's).
    /// </summary>
    private static void AppendOccurrence(Dictionary<string, Accum> state, List<string> order,
        string layerId, VariantTag tag)
    {
        if (!state.TryGetValue(tag.TagName, out var existing))
        {
            order.Add(tag.TagName);
            existing = new Accum(tag.TagName, tag.Value, tag.Fragment, layerId, tag.Origin,
                VariantMode.Merge, false, null) { Occurrences = [] };
            state[tag.TagName] = existing;
        }

        // What this occurrence is being added to: the first entry an earlier layer contributed. An
        // additive entry displaces nothing of its own, so without this it would report no base at all.
        var occurrences = existing.Occurrences!;
        var firstExisting = occurrences.FirstOrDefault();

        occurrences.Add(new Accum(tag.TagName, tag.Value, tag.Fragment, layerId, tag.Origin,
            VariantMode.Merge, occurrences.Count > 0, firstExisting?.Value));
    }

    private static void ApplyTag(Dictionary<string, Accum> state, List<string> order, string layerId,
        VariantTag tag, VariantMode mode, bool isRootLayer)
    {
        var hadPrev = state.TryGetValue(tag.TagName, out var prev);

        // Ignored tags can be inherited from a base but a variant layer can neither override nor add them.
        if (mode == VariantMode.Ignored && !isRootLayer)
            return;

        string value;
        string fragment;
        if (mode == VariantMode.Merge && hadPrev)
        {
            value = MergeValues(prev!.Value, tag.Value);
            fragment = $"<{tag.TagName}>{value}</{tag.TagName}>";
        }
        else
        {
            value = tag.Value;
            fragment = tag.Fragment;
        }

        if (!hadPrev)
            order.Add(tag.TagName);

        // The value being displaced by this layer. Deliberately the immediately-preceding one, not
        // the chain's oldest: for A > B > C the UX must report what A actually inherited (B's), and
        // a layer that adds a tag no earlier layer had displaces nothing.
        var displaced = hadPrev ? prev!.Value : null;

        state[tag.TagName] = new Accum(tag.TagName, value, fragment, layerId, tag.Origin, mode,
            hadPrev || (prev?.EverInBase ?? false), displaced);
    }

    private static EffectiveTag ToEffectiveTag(Accum a, string topId, bool single)
    {
        VariantProvenance provenance;
        if (single)
            provenance = VariantProvenance.Own;
        else if (string.Equals(a.OriginObjectId, topId, StringComparison.OrdinalIgnoreCase))
            provenance = a.Mode == VariantMode.Merge && a.EverInBase ? VariantProvenance.Merged
                : a.EverInBase ? VariantProvenance.Overridden
                : VariantProvenance.Added;
        else
            provenance = VariantProvenance.Inherited;

        // Only the provenances that actually displaced something carry the old value.
        var baseValue = provenance is VariantProvenance.Overridden or VariantProvenance.Merged
            ? a.DisplacedValue
            : null;

        return new EffectiveTag(a.TagName, a.Value, a.Fragment, provenance, a.OriginObjectId, a.Origin,
            baseValue);
    }

    private static string MergeValues(string baseValue, string variantValue)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = new List<string>();
        foreach (var token in baseValue.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
                     .Concat(variantValue.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)))
            if (seen.Add(token))
                tokens.Add(token);

        return string.Join(", ", tokens);
    }

    private sealed record Accum(
        string TagName,
        string Value,
        string Fragment,
        string OriginObjectId,
        SymbolOrigin? Origin,
        VariantMode Mode,
        bool EverInBase,
        string? DisplacedValue
    )
    {
        /// <summary>
        ///     Set only on the placeholder entry for a repeatable additive tag; holds one Accum per
        ///     occurrence, in contribution order. Null for ordinary single-valued tags.
        /// </summary>
        public List<Accum>? Occurrences { get; init; }
    }
}