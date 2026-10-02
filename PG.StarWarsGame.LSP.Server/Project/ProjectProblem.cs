// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Why a workspace's mod project could not be used, as a fixed category.
/// </summary>
/// <remarks>
///     The user-facing message stays the detail; this is the part a bug report can carry, since the
///     message names the file and its path.
/// </remarks>
public enum ProjectProblem
{
    None,
    /// <summary>No <c>.pgproj</c> under any workspace root.</summary>
    Missing,
    /// <summary>More than one <c>.pgproj</c> under one root.</summary>
    Ambiguous,
    /// <summary>Not readable as the JSON object a project file is.</summary>
    Unparseable,
    /// <summary>A project file format this server refuses.</summary>
    UnsupportedVersion,
    /// <summary>Parsed, but its contents fail validation.</summary>
    Invalid,
    /// <summary>The project loaded and indexing was refused because no baseline is loaded.</summary>
    BaselineRefused
}
