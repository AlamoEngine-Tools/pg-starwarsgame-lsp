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
                ApplyTag(state, order, layer.Id, tag, mode, isRootLayer);
            }
        }

        var single = chain.Count == 1;
        var tags = order
            .Select(name => ToEffectiveTag(state[name], root.Id, single))
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

        state[tag.TagName] = new Accum(tag.TagName, value, fragment, layerId, tag.Origin, mode,
            hadPrev || (prev?.EverInBase ?? false));
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

        return new EffectiveTag(a.TagName, a.Value, a.Fragment, provenance, a.OriginObjectId, a.Origin);
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
        bool EverInBase
    );
}