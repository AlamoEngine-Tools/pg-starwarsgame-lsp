// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Resolves the single <see cref="WorkspaceConfiguration" /> for the session from the
///     <c>.pgproj</c> found under the given roots (following project references). Returns
///     <see langword="null" /> when no project file exists - the pgproj is the only way to declare
///     directories, so without one there is nothing to scan.
/// </summary>
public interface IProjectConfigurationResolver
{
    WorkspaceConfiguration? Resolve(IReadOnlyList<string> roots);
}