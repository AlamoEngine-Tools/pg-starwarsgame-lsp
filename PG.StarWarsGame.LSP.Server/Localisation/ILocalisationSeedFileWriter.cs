// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Data;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public interface ILocalisationSeedFileWriter
{
    // Serializes db in the given format and writes it to the conventional file name for that
    // format under targetDir (creating the directory if needed). Returns the written path, or
    // null if the format has no generator (caller should warn) — does not check for an existing
    // file at the target path; callers decide overwrite policy themselves.
    Task<string?> WriteAsync(IKeyedTranslationDatabase db, string format, string targetDir, CancellationToken ct);
}
