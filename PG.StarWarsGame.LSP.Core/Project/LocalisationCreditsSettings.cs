// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

/// <summary>
///     Which files in a project's localisation directory are ordered credits files rather than
///     keyed text files.
///     <para>
///         Credits files allow duplicate keys and their row order is significant, so the two kinds
///         cannot share an editing model. They live in the same directory and format as the text
///         files, which is why this refines the existing localisation node instead of adding a
///         second one.
///     </para>
/// </summary>
/// <param name="Detection">
///     One of <see cref="Convention" />, <see cref="Explicit" /> or <see cref="None" />.
/// </param>
/// <param name="Files">
///     Filenames to treat as credits, with or without their extension. Required under
///     <see cref="Explicit" />, additive under <see cref="Convention" />, ignored under
///     <see cref="None" />.
/// </param>
public sealed record LocalisationCreditsSettings(string Detection, IReadOnlyList<string> Files)
{
    /// <summary>Engine naming: a file whose name begins with "credits". The default.</summary>
    public const string Convention = "convention";

    /// <summary>Only the files named in <see cref="Files" />.</summary>
    public const string Explicit = "explicit";

    /// <summary>Nothing is credits - the opt-out for a project whose text file is named like one.</summary>
    public const string None = "none";
}
