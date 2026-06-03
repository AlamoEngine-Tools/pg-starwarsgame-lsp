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

    public IEnumerable<string> Keys =>
        _databases
            .SelectMany(db => db)
            .Select(e => e.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public string? GetValue(string key)
    {
        foreach (var db in _databases)
            if (db.TryGetEntry(key, out var entry) && entry is not null &&
                entry.TryGetTranslation(_language, out var value))
                return value;
        return null;
    }
}