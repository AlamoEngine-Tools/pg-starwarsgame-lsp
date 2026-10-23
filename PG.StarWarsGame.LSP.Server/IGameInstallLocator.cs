// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     Where the game is installed, for a game path the editor leaves unset. Each answer is the folder
///     the game runs from - the one holding its executable and its <c>Data</c> - or null when no
///     install is found.
/// </summary>
public interface IGameInstallLocator
{
    /// <summary>Star Wars: Empire at War.</summary>
    string? FindBaseGame();

    /// <summary>Star Wars: Empire at War: Forces of Corruption.</summary>
    string? FindExpansion();
}