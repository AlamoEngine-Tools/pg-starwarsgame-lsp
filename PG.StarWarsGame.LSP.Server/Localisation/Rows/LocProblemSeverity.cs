// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Severity strings shared by both validators and the client. The two editors report different
///     things, but they grade them the same way.
/// </summary>
public static class LocProblemSeverity
{
    public const string Error = "error";
    public const string Warning = "warning";

    /// <summary>
    ///     Worth surfacing, but nothing is wrong. Used where the file is valid and the note is only
    ///     there so a reader is not surprised - a heading with no entries under it, say, which the
    ///     crawl renders perfectly well.
    /// </summary>
    public const string Info = "info";
}
