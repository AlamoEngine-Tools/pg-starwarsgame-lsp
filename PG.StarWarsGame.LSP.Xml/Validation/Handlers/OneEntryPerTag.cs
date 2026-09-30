// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     The campaign tuple readers take a fixed number of tokens per tag and drop the rest.
/// </summary>
/// <remarks>
///     Measured in the 2018 build: the game's XML reader takes two tokens for a per-faction value or
///     planet and three for a force deployment, and ignores anything after them. So <c>Rebel, 1000, Empire, 2000</c> sets Rebel's credits and
///     silently loses Empire's - repeating the tag is the only way to give a second entry.
/// </remarks>
internal static class OneEntryPerTag
{
    /// <summary>
    ///     A warning when <paramref name="parts" /> holds more non-empty tokens than the engine reads,
    ///     or null when it does not.
    /// </summary>
    public static XmlDiagnosticResult? ExtraTokens(string tag, IReadOnlyList<string> parts, int read)
    {
        var dropped = parts.Skip(read).Where(p => p.Length > 0).ToList();
        if (dropped.Count == 0) return null;

        var kept = string.Join(", ", parts.Take(read));
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
            $"Only '{kept}' is read - '{string.Join(", ", dropped)}' is dropped. Each <{tag}> holds one "
            + "entry; further entries need their own tag.");
    }
}