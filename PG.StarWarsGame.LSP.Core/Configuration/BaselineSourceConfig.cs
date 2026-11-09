// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record BaselineSourceConfig
{
    public BaselineSourceType Type { get; init; } = BaselineSourceType.Http;

    /// <summary>
    ///     URL of the baseline <c>.aet</c> file. Empty (the default) means the newest release of
    ///     <see cref="Repository" /> whose baseline this server reads; the icon sidecar is read from
    ///     beside it either way.
    /// </summary>
    public string Url { get; init; } = "";

    /// <summary><c>owner/name</c> of the repository whose releases are resolved when <see cref="Url" /> is empty.</summary>
    public string Repository { get; init; } = "AlamoEngine-Tools/eaw-baseline";

    /// <summary>
    ///     The baseline file inside that repository, at the resolved tag; also the file a local folder
    ///     is read from. Set by <see cref="ForGame" />.
    /// </summary>
    public string FilePath { get; init; } = "foc/aet-pg-swg-lsp-foc-baseline.aet";

    /// <summary>
    ///     A local <c>.aet</c> file, or a folder laid out like the baseline repository with one
    ///     sub-folder per game.
    /// </summary>
    public string? LocalPath { get; init; }

    /// <summary>
    ///     This source for <paramref name="game" />: the game's file in the repository layout. A URL or a
    ///     local path named outright stays what it is here; <see cref="LocalFile" /> decides what a local
    ///     path means for the game.
    /// </summary>
    public BaselineSourceConfig ForGame(TargetGame game)
    {
        var folder = TargetGameNames.Of(game);
        return this with { FilePath = $"{folder}/aet-pg-swg-lsp-{folder}-baseline.aet" };
    }

    /// <summary>
    ///     The local <c>.aet</c> this game's server reads. <see cref="FilePath" /> inside
    ///     <see cref="LocalPath" /> when that names a folder; this game's sibling when it names the OTHER
    ///     game's file of the same layout and this game's exists; otherwise <see cref="LocalPath" /> itself,
    ///     the file the user named. Null when no local path is set.
    /// </summary>
    /// <remarks>
    ///     The sibling rule exists because the setting is one per window, and a window may hold an Empire
    ///     at War project and a Forces of Corruption project: with the FoC file named, the EaW project's
    ///     server loaded it too and knew every FoC-only object as the game's (#288, measured 2026-10-22).
    ///     A file outside the layout, or with no sibling for this game, is read as named: the user chose
    ///     it, and a guess would load nothing.
    /// </remarks>
    public string? LocalFile(IFileSystem fileSystem)
    {
        if (string.IsNullOrWhiteSpace(LocalPath)) return null;

        var path = fileSystem.Path;
        var layoutFile = FilePath.Replace('/', path.DirectorySeparatorChar);
        if (fileSystem.Directory.Exists(LocalPath)) return path.Combine(LocalPath, layoutFile);

        // "<root>/<game>/aet-pg-swg-lsp-<game>-baseline.aet": two levels up is the root of the layout.
        if (!LayoutFileName.IsMatch(path.GetFileName(LocalPath))) return LocalPath;
        var root = path.GetDirectoryName(path.GetDirectoryName(LocalPath));
        if (root is null) return LocalPath;

        var sibling = path.Combine(root, layoutFile);
        return !string.Equals(sibling, LocalPath, StringComparison.OrdinalIgnoreCase) && fileSystem.File.Exists(sibling)
            ? sibling
            : LocalPath;
    }

    private static readonly Regex LayoutFileName =
        new("^aet-pg-swg-lsp-[a-z0-9]+-baseline\\.aet$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}