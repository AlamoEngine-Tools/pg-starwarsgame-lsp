// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

// Targeted row/element-level edits to an existing CSV/XML/.properties localisation file - never a
// full re-parse-and-re-export, so an edit to one key doesn't reformat or reorder the rest of the
// file. Shared by the create-key quick-fix and the live editor grid's per-cell writes.
public interface ILocalisationEntryWriter
{
    Task<bool> ExistsAsync(string filePath, string key, CancellationToken ct);

    // Updates key's translations if it already exists, otherwise appends a new entry. Returns
    // false only when the file's format has no writer (unrecognised extension).
    Task<bool> UpsertAsync(
        string filePath, string key, IReadOnlyDictionary<string, string>? translations, CancellationToken ct);

    // Returns false when key was not found (nothing removed) or the format is unrecognised.
    Task<bool> DeleteAsync(string filePath, string key, CancellationToken ct);

    // Adds a new, empty language column/element across every existing entry. Returns false
    // without modifying the file when the language is already present, or the format doesn't
    // support multiple languages (.properties is inherently single-language).
    Task<bool> AddLanguageAsync(string filePath, string language, CancellationToken ct);
}