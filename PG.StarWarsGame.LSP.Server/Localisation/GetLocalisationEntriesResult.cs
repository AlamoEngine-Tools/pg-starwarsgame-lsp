// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

// ContentHash must be echoed back on every subsequent write request for this file (see
// LocalisationConcurrencyGuard) — it's how the server detects the file changed on disk since the
// client last fetched it.
public sealed record GetLocalisationEntriesResult(
    IReadOnlyList<LocalisationEntryDto> Entries,
    IReadOnlyList<string> Languages,
    string ContentHash,
    string? Error = null);
