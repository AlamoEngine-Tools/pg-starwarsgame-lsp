// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

// Targeted, key-addressed edits to an existing CSV/XML/.properties localisation file - never a
// full re-parse-and-re-export, so adding one key doesn't reformat or reorder the rest of the file.
//
// Its only caller is the create-key quick fix, which genuinely is key-addressed ("add this key if
// it is absent"). The editor goes through ILocalisationDocumentEditor instead: it addresses rows by
// position, which a keyed writer cannot express, and commits a whole batch under one hash check.
public interface ILocalisationEntryWriter
{
    Task<bool> ExistsAsync(string filePath, string key, CancellationToken ct);

    // Updates key's translations if it already exists, otherwise appends a new entry. Returns
    // false only when the file's format has no writer (unrecognised extension).
    Task<bool> UpsertAsync(
        string filePath, string key, IReadOnlyDictionary<string, string>? translations, CancellationToken ct);
}
