// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Engine placeholder object names. The game treats these as a valid "no object" value in any
///     object-reference position (e.g. <c>&lt;Land_Damage_SFX&gt;null,…&lt;/…&gt;</c>), and ships
///     files that intentionally redefine them — so reference validation must accept them
///     everywhere and duplicate detection must not treat them as errors.
/// </summary>
public static class EnginePlaceholders
{
    public static readonly IReadOnlySet<string> Names =
        new HashSet<string>(["Default", "Null", "None"], StringComparer.OrdinalIgnoreCase);

    public static bool IsPlaceholder(string id)
    {
        return Names.Contains(id);
    }
}