// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Resolves a <see cref="WorkspaceConfiguration" /> per root project found under the given roots
///     (each following its own project references). The pgproj is the only way to declare
///     directories, so a root without one contributes nothing to scan.
/// </summary>
public interface IProjectConfigurationResolver
{
    /// <summary>
    ///     One configuration per discovered root project, in root order. A project that fails to load
    ///     is reported to the user and omitted - the healthy projects in a multi-root workspace are
    ///     still resolved.
    /// </summary>
    IReadOnlyList<WorkspaceConfiguration> ResolveAll(IReadOnlyList<string> roots);

    /// <summary>
    ///     The first resolved configuration, for the single-project call sites. Prefer
    ///     <see cref="ResolveAll" /> - this discards every project but one in a multi-root workspace.
    /// </summary>
    WorkspaceConfiguration? Resolve(IReadOnlyList<string> roots)
    {
        return ResolveAll(roots).FirstOrDefault();
    }
}
