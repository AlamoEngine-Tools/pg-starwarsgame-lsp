// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.LSP.Core.Localisation;

namespace PG.StarWarsGame.LSP.Server.Localisation;

internal sealed class TranslationDatabaseLocalisationIndex : ILocalisationIndex
{
    private readonly IReadOnlyList<IKeyedTranslationDatabase> _databases;
    private readonly IAlamoLanguageDefinition _language;

    public TranslationDatabaseLocalisationIndex(
        IReadOnlyList<IKeyedTranslationDatabase> databases,
        IAlamoLanguageDefinition language)
    {
        _databases = databases;
        _language = language;
    }

    public bool ContainsKey(string key)
    {
        foreach (var db in _databases)
            if (db.ContainsKey(key))
                return true;
        return false;
    }

    /// <summary>
    ///     Every key across the layers, de-duplicated the way the engine addresses them.
    /// </summary>
    /// <remarks>
    ///     Ordinal, not case-insensitive. An entry is addressed by the CRC32 of its key bytes, and
    ///     that hash is case-sensitive, so <c>TEXT_A</c> and <c>text_a</c> are two entries the engine
    ///     reads independently - folding them together here hid one of them from completion and from
    ///     anything else that enumerates keys. Lookup (<see cref="ContainsKey" />,
    ///     <see cref="GetValue" />) was already ordinal, via the database's own comparer, so this
    ///     brings enumeration in line with it.
    /// </remarks>
    public IEnumerable<string> Keys =>
        _databases
            .SelectMany(db => db)
            .Select(e => e.Key)
            .Distinct(StringComparer.Ordinal);

    public string? GetValue(string key)
    {
        foreach (var db in _databases)
            if (db.TryGetEntry(key, out var entry) && entry is not null &&
                entry.TryGetTranslation(_language, out var value))
                return value;
        return null;
    }
}