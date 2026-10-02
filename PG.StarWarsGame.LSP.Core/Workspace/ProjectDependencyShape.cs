// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     The shape of a project's dependency tree, as counts.
/// </summary>
/// <param name="Direct">References the root project names that loaded.</param>
/// <param name="Total">Every dependency project in the tree, each counted once.</param>
/// <param name="Depth">
///     The longest chain below the root along the order the tree was walked; 0 for a project with
///     no dependencies.
/// </param>
/// <param name="Unresolved">References anywhere in the tree that could not be loaded and were skipped.</param>
public sealed record ProjectDependencyShape(int Direct, int Total, int Depth, int Unresolved);
