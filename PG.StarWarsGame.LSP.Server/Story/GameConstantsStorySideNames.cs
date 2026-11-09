// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The sides the project's effective GameConstants names - a mod's own over the game's - which
///     the dedicated campaign story tags attach their plots to (#192).
/// </summary>
/// <remarks>
///     Derived from the index snapshot, so a re-index drops the answer rather than serving a stale
///     one, and every reader of one snapshot - the chain scan, the attachment check, the manifest
///     writer - gets the same three names.
/// </remarks>
internal sealed class GameConstantsStorySideNames(
    IGameIndexService index,
    ISchemaProvider schema,
    IVariantTagSource tags) : IStorySideNamesSource
{
    private const string Key = "storySideNames";

    public StorySideNames Current =>
        index.Current.Derived(Key, snapshot =>
        {
            // A singleton: its id is its type.
            var constants =
                new EffectiveObjectResolver(snapshot, schema, tags).Resolve(EncyclopediaTags.GameConstantsId);
            if (!constants.Found) return StorySideNames.Shipped;

            // Last occurrence wins, as the game's field mapper writes a repeated single value.
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in constants.Tags) values[tag.TagName] = tag.Value;
            return StorySideNames.From(values);
        });
}