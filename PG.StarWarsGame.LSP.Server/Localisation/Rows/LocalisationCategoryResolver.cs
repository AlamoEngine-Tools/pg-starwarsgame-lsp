// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Resolves a file's category the way the loader already decided it.
///     <para>
///         Shared so the read and write paths cannot disagree: if one thought a file was credits
///         and the other text, a duplicate key would be rejected on save after being offered as
///         legal in the grid.
///     </para>
/// </summary>
public static class LocalisationCategoryResolver
{
    public static string Resolve(
        ILocalisationProjectRegistry registry, IFileHelper fileHelper, string filePath)
    {
        var normalised = fileHelper.NormalizeUri(filePath);

        foreach (var project in registry.Projects)
            if (string.Equals(fileHelper.NormalizeUri(project.FilePath), normalised,
                    StringComparison.OrdinalIgnoreCase))
                return project.Category;

        // Outside any text root: fall back to the naming convention rather than silently calling it
        // text, which would let a key-addressed assumption reach a credits file.
        return CreditsFileClassifier.Classify(filePath, null);
    }
}
