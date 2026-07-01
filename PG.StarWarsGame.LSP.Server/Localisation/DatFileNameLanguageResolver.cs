// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;

namespace PG.StarWarsGame.LSP.Server.Localisation;

// DAT is one file per language with no self-describing language tag inside the binary — the
// language has to come from the file name. Resolves the "_<LANGUAGE>" suffix convention
// ExportLocalisationToDatHandler already writes (e.g. "MasterTextFile_ENGLISH.dat").
public static class DatFileNameLanguageResolver
{
    public static bool TryResolve(
        string path, ILanguageService langService, out IAlamoLanguageDefinition? language)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var underscoreIdx = name.LastIndexOf('_');
        if (underscoreIdx < 0 || underscoreIdx == name.Length - 1)
        {
            language = null;
            return false;
        }

        var identifier = name[(underscoreIdx + 1)..];
        return langService.TryGetByIdentifier(identifier, out language);
    }
}
