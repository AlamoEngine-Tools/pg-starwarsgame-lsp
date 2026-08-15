// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Assets.ShipNames;

/// <summary>
///     One object's pool of individual ship names.
/// </summary>
/// <param name="SourcePath">The path as written in <c>ShipNameTextFiles</c>, for reporting.</param>
/// <param name="FileFound">
///     Whether the file was readable. Distinguishes "wired up but the file is missing" - an
///     authoring mistake worth showing - from "wired up and simply empty".
/// </param>
/// <param name="Names">The names in file order.</param>
/// <remarks>
///     Deliberately has no "pick one" method. The engine picks at random and remembers which names
///     it has spent; a preview has nothing to remember across, and WHICH name to show turned out to
///     be a presentation choice with a lifetime - the panel holds its pick so the card does not
///     reshuffle on every refresh. That lives on the client; this side only reports the pool.
/// </remarks>
public sealed record ShipNamePool(string SourcePath, bool FileFound, IReadOnlyList<string> Names);

/// <summary>
///     Every object that draws an individual ship name instead of showing its class.
/// </summary>
public sealed class ShipNameCatalog
{
    private readonly IReadOnlyDictionary<string, ShipNamePool> _pools;

    private ShipNameCatalog(IReadOnlyDictionary<string, ShipNamePool> pools)
    {
        _pools = pools;
    }

    public static ShipNameCatalog Empty { get; } =
        new(new Dictionary<string, ShipNamePool>(StringComparer.OrdinalIgnoreCase));

    /// <summary>All objects with a pool, for reporting the wiring as a whole.</summary>
    public IReadOnlyDictionary<string, ShipNamePool> Pools => _pools;

    /// <summary>
    ///     Builds the catalog from the raw <c>ShipNameTextFiles</c> value.
    /// </summary>
    /// <param name="rawTagValue">The tag's text, as written in GameConstants.</param>
    /// <param name="fileReader">
    ///     Resolves a path as written in the tag to its bytes, or <see langword="null" /> when it
    ///     cannot be read. Kept as a callback so this stays free of any notion of where the game
    ///     lives on disk.
    /// </param>
    public static ShipNameCatalog Build(string? rawTagValue, Func<string, byte[]?> fileReader)
    {
        var pairs = ShipNameTextFiles.ParsePairs(rawTagValue);
        if (pairs.Count == 0)
            return Empty;

        // Cached per path: the mapping is many-to-one, and re-reading one file per object that
        // shares it would read the Star Destroyer list three times over.
        var byPath = new Dictionary<string, ShipNamePool>(StringComparer.OrdinalIgnoreCase);
        var pools = new Dictionary<string, ShipNamePool>(StringComparer.OrdinalIgnoreCase);

        foreach (var (objectId, path) in pairs)
        {
            if (!byPath.TryGetValue(path, out var pool))
            {
                var content = fileReader(path);
                pool = new ShipNamePool(path, content is not null, ShipNameTextFiles.ReadNames(content));
                byPath[path] = pool;
            }

            pools[objectId] = pool;
        }

        return new ShipNameCatalog(pools);
    }

    /// <summary>
    ///     The pool for <paramref name="objectId" />, or <see langword="null" /> when it is not
    ///     registered for custom names - which is the overwhelming majority of objects.
    /// </summary>
    public ShipNamePool? For(string objectId)
    {
        return _pools.GetValueOrDefault(objectId);
    }
}
