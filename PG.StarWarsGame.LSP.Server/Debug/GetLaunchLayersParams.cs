// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Debug;

/// <summary>
///     Asks for every project layer as a debug session needs it: the script roots to map the game's
///     paths onto workspace files, and whether the layer's project directory can be handed to the
///     game as a mod. Layers come highest precedence first, which is also the order the game's
///     command line wants them in.
/// </summary>
[Method("aet/getLaunchLayers", Direction.ClientToServer)]
public sealed record GetLaunchLayersParams : IRequest<GetLaunchLayersResult>;

/// <param name="Enabled">False when the Lua debugger feature is off; the layers are then empty.</param>
/// <param name="Layers">Highest precedence first: the mod, then its dependencies.</param>
public sealed record GetLaunchLayersResult(bool Enabled, IReadOnlyList<LaunchLayer> Layers);

/// <param name="ProjectPath">The layer's <c>.pgproj</c>, normalized, or null for a layer without one.</param>
/// <param name="ProjectDirectory">The directory holding that file, normalized with forward slashes.</param>
/// <param name="ScriptRoots">The layer's Lua script directories, absolute and normalized.</param>
/// <param name="ModPath">
///     The directory to pass the game as this layer's mod, when the project is laid out as one;
///     null otherwise, with <paramref name="NotRunnableReason" /> saying why.
/// </param>
public sealed record LaunchLayer(
    string Name,
    int Rank,
    string? ProjectPath,
    string? ProjectDirectory,
    IReadOnlyList<string> ScriptRoots,
    string? ModPath,
    string? NotRunnableReason);