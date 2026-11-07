// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
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
    ///     local file named outright stays what it is - the user chose that file.
    /// </summary>
    public BaselineSourceConfig ForGame(TargetGame game)
    {
        var folder = TargetGameNames.Of(game);
        return this with { FilePath = $"{folder}/aet-pg-swg-lsp-{folder}-baseline.aet" };
    }

    /// <summary>
    ///     The local <c>.aet</c> to read: <see cref="LocalPath" /> itself, or <see cref="FilePath" />
    ///     inside it when it names a folder. Null when no local path is set.
    /// </summary>
    public string? LocalFile(IFileSystem fileSystem)
    {
        if (string.IsNullOrWhiteSpace(LocalPath)) return null;
        return fileSystem.Directory.Exists(LocalPath) ? fileSystem.Path.Combine(LocalPath, FilePath) : LocalPath;
    }
}