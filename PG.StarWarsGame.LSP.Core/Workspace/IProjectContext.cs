// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     The one project a piece of state belongs to. Anything that persists under a project's
///     <c>.aetswg/</c> directory, or that is scoped by a project's declared directories, takes this
///     rather than reaching for "the" workspace configuration - in a multi-root workspace there is no
///     such thing, and reaching for the first project silently gives every other project the first
///     one's settings, suppressions and layouts.
/// </summary>
public interface IProjectContext
{
    /// <summary>This project's resolved configuration.</summary>
    WorkspaceConfiguration Configuration { get; }

    /// <summary>
    ///     This project's <c>.aetswg/</c> directory ('/'-separated, no trailing slash), or
    ///     <see langword="null" /> when the project has no <c>.pgproj</c> and therefore nowhere to
    ///     persist to.
    /// </summary>
    string? AetswgDirectory { get; }
}
