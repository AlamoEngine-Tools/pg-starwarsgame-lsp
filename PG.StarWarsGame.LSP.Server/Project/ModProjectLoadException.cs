// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Thrown when a <c>.pgproj</c> file cannot be loaded. Carries a human-readable message
///     (file name, source location, and a corrective hint) suitable for showing directly to the
///     user as an editor notification - never a raw <see cref="System.Text.Json.JsonException" />.
/// </summary>
public sealed class ModProjectLoadException : Exception
{
    public ModProjectLoadException(string message, Exception? innerException = null,
        ProjectProblem problem = ProjectProblem.Invalid)
        : base(message, innerException)
    {
        Problem = problem;
    }

    /// <summary>
    ///     What went wrong, as a category. The message names the file and its path; this is the part
    ///     a bug report can carry.
    /// </summary>
    public ProjectProblem Problem { get; }
}