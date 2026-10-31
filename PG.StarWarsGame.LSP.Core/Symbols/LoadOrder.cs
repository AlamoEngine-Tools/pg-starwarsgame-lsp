// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The files the game loads, where each sits in its registry's list, and which shipped files it
///     never opens - built from the metafile scan's <see cref="RegistryWalk" />s. The game has no object
///     layers: which of two definitions of one name counts follows from these positions and the
///     registry's <see cref="DuplicateNameRule" />, and a shipped definition counts only when the game
///     opens its file.
/// </summary>
public sealed class LoadOrder
{
    /// <summary>No scan yet: every shipped definition counts and the layers decide, as before.</summary>
    public static readonly LoadOrder Unknown = new(
        ImmutableDictionary.Create<string, LoadPosition>(DocumentUris.Comparer),
        ImmutableDictionary<string, LoadPosition>.Empty, [], [],
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase), ImmutableHashSet<string>.Empty);

    private readonly ImmutableDictionary<string, LoadPosition> _layerFiles;
    private readonly ImmutableArray<string> _registries;
    private readonly ImmutableArray<DuplicateNameRule> _rules;
    private readonly ImmutableHashSet<string> _shadowedShipped;
    private readonly ImmutableDictionary<string, LoadPosition> _shippedFiles;
    private readonly ConditionalWeakTable<BaselineIndex, ImmutableDictionary<string, GameSymbol>> _visible = new();
    private readonly ImmutableHashSet<string> _walkedTypes;

    private LoadOrder(ImmutableDictionary<string, LoadPosition> layerFiles,
        ImmutableDictionary<string, LoadPosition> shippedFiles, ImmutableArray<DuplicateNameRule> rules,
        ImmutableArray<string> registries, ImmutableHashSet<string> walkedTypes,
        ImmutableHashSet<string> shadowedShipped)
    {
        _layerFiles = layerFiles;
        _shippedFiles = shippedFiles;
        _rules = rules;
        _registries = registries;
        _walkedTypes = walkedTypes;
        _shadowedShipped = shadowedShipped;
    }

    private bool IsKnown => !_rules.IsEmpty || !_shadowedShipped.IsEmpty;

    /// <param name="walks">Each file registry as the game loads it.</param>
    /// <param name="metafiles">The schema's metafiles, for each registry's rule and types.</param>
    /// <param name="shadowedShippedFiles">
    ///     The direct-content files a project layer ships, XML-directory-relative in key form: the
    ///     game opens that copy, never the shipped one.
    /// </param>
    public static LoadOrder From(IEnumerable<RegistryWalk> walks, IEnumerable<MetafileDefinition> metafiles,
        IEnumerable<string> shadowedShippedFiles)
    {
        var registries = new Dictionary<string, MetafileDefinition>(StringComparer.Ordinal);
        foreach (var def in metafiles.Where(d => d.MetafileType == MetafileType.FileRegistry))
            registries.TryAdd(GamePaths.Key(def.Path), def);

        var layerFiles = ImmutableDictionary.CreateBuilder<string, LoadPosition>(DocumentUris.Comparer);
        var shippedFiles = ImmutableDictionary.CreateBuilder<string, LoadPosition>(StringComparer.Ordinal);
        var rules = ImmutableArray.CreateBuilder<DuplicateNameRule>();
        var registryPaths = ImmutableArray.CreateBuilder<string>();
        var walkedTypes = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var walk in walks)
        {
            if (!registries.TryGetValue(walk.RegistryPath, out var def)) continue;

            var registry = rules.Count;
            rules.Add(def.DuplicateNames ?? DuplicateNameRule.FIRST_WINS);
            registryPaths.Add(walk.RegistryPath);
            walkedTypes.UnionWith(def.Types);
            for (var i = 0; i < walk.Entries.Length; i++)
            {
                var entry = walk.Entries[i];
                if (entry.LayerUri is { } uri)
                    Place(layerFiles, uri, registry, i);
                else
                    Place(shippedFiles, entry.Key, registry, i);
            }
        }

        return new LoadOrder(layerFiles.ToImmutable(), shippedFiles.ToImmutable(), rules.ToImmutable(),
            registryPaths.ToImmutable(), walkedTypes.ToImmutable(), [.. shadowedShippedFiles]);
    }

    // A file listed twice loads twice, so it spans its first and its last place. A file two
    // registries list keeps the place of the first: one file feeds one id space in practice.
    private static void Place(ImmutableDictionary<string, LoadPosition>.Builder files, string file, int registry,
        int index)
    {
        if (!files.TryGetValue(file, out var known))
            files[file] = new LoadPosition(registry, index, index);
        else if (known.Registry == registry)
            files[file] = known with { Last = index };
    }

    /// <summary>
    ///     Where the game loads <paramref name="symbol" />'s file, or null when no walked registry
    ///     lists it.
    /// </summary>
    public LoadPosition? PositionOf(GameSymbol symbol)
    {
        if (symbol.Origin is not FileOrigin file) return null;
        if (IsLayerUri(file.Uri))
            return _layerFiles.TryGetValue(file.Uri, out var layer) ? layer : null;
        return _shippedFiles.TryGetValue(XmlKey(file.Uri), out var shipped) ? shipped : null;
    }

    /// <summary>The duplicate-name rule of the registry a <see cref="LoadPosition" /> lies in.</summary>
    public DuplicateNameRule RuleOf(LoadPosition position)
    {
        return _rules[position.Registry];
    }

    /// <summary>The game path, in key form, of the registry a <see cref="LoadPosition" /> lies in.</summary>
    public string RegistryOf(LoadPosition position)
    {
        return _registries[position.Registry];
    }

    /// <summary>
    ///     The shipped definitions the game loads: those in a file a walked registry lists and no layer
    ///     copy replaces, and those of types no walked registry holds, unless a layer ships their
    ///     direct-content file. Computed once per baseline.
    /// </summary>
    public ImmutableDictionary<string, GameSymbol> VisibleOf(BaselineIndex baseline)
    {
        return IsKnown ? _visible.GetValue(baseline, Visible) : baseline.Symbols;
    }

    private ImmutableDictionary<string, GameSymbol> Visible(BaselineIndex baseline)
    {
        var visible = baseline.Symbols.ToBuilder();
        foreach (var (id, symbol) in baseline.Symbols)
            if (!Loads(symbol))
                visible.Remove(id);
        return visible.ToImmutable();
    }

    private bool Loads(GameSymbol shipped)
    {
        if (shipped.Origin is not FileOrigin file) return true;

        var key = XmlKey(file.Uri);
        if (_shippedFiles.ContainsKey(key)) return true;
        if (shipped.TypeName is { } type && _walkedTypes.Contains(type)) return false;
        return !_shadowedShipped.Contains(key);
    }

    private static bool IsLayerUri(string origin)
    {
        return origin.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A game path as the walks key it: XML-directory-relative, <c>DATA\XML\Units.xml</c> as
    ///     <c>units.xml</c>.
    /// </summary>
    public static string XmlKey(string gamePath)
    {
        const string xmlPrefix = "data/xml/";
        var key = GamePaths.Key(gamePath);
        return key.StartsWith(xmlPrefix, StringComparison.Ordinal) ? key[xmlPrefix.Length..] : key;
    }
}

/// <summary>
///     Where a file loads: its registry, in the order of <see cref="LoadOrder.From" />'s walks, and its
///     first and last place in that registry's list.
/// </summary>
public readonly record struct LoadPosition(int Registry, int First, int Last);