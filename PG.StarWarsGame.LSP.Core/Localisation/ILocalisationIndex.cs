// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Localisation;

/// <summary>
///     Merged (baseline ∪ workspace) localisation key set used for existence validation
///     and completion of <see cref="Schema.ReferenceKind.LocalisationKey" /> references.
/// </summary>
public interface ILocalisationIndex
{
    IEnumerable<string> Keys { get; }
    bool ContainsKey(string key);

    /// <summary>Returns the translated value for <paramref name="key" />, or <see langword="null" /> if absent.</summary>
    string? GetValue(string key);
}