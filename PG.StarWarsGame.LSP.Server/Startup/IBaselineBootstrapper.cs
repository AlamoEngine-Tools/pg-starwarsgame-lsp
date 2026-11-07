// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Loads the shipped-game baseline index and baseline localisation keys from the configured
///     source and applies them to the <see cref="Core.Symbols.IGameIndexService" />. Degrades to an
///     empty baseline on failure rather than aborting startup.
/// </summary>
public interface IBaselineBootstrapper
{
    /// <summary>The game whose baseline is loaded, or null while none has loaded.</summary>
    TargetGame? LoadedGame => null;

    /// <summary>Loads <paramref name="game" />'s baseline unless it is the one already loaded.</summary>
    Task LoadAsync(TargetGame game, CancellationToken ct);
}