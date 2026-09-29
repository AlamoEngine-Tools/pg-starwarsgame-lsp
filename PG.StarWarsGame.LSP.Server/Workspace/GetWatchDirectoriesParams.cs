// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Workspace;

/// <summary>
///     Asks where this workspace's content actually lives, so the client can watch all of it.
/// </summary>
/// <remarks>
///     The client builds its own file watchers, and VS Code resolves a bare glob against open
///     workspace FOLDERS only. A project reached through <c>projectReferences</c> normally sits
///     outside every folder, so a change to it reached nobody: no re-index, no asset re-glob, no
///     localisation reload, and no reaction to editing the dependency's <c>.pgproj</c> itself. The
///     server resolves the layer graph, so it is the only side that can answer this.
/// </remarks>
[Method("aet/getWatchDirectories", Direction.ClientToServer)]
public sealed record GetWatchDirectoriesParams : IRequest<GetWatchDirectoriesResult>;

/// <param name="Directories">
///     Absolute, normalised directories to watch recursively, with any directory that another one
///     already contains removed. The client still decides which of these its workspace folders
///     cover already.
/// </param>
public sealed record GetWatchDirectoriesResult(IReadOnlyList<string> Directories);
