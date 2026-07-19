// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Rename;

/// <summary>
///     Story-specific rename guard rails. Story renames are index-wide text operations, so an
///     event name that several events share (legal across threads/campaigns) must not be renamed
///     blindly - every holder and every reference would change. Flag names additionally carry the
///     engine's 31-character limit.
/// </summary>
public static class StoryRenameGuard
{
    private const int MaxFlagNameLength = 31;

    /// <summary>Whether the id names at least one story symbol (event, flag, notification).</summary>
    public static bool IsStorySymbol(string id, GameIndex index)
    {
        return index.WorkspaceDefinitions.TryGetValue(id, out var defs)
               && defs.Any(d => StoryReferenceTypes.IsStorySymbolType(d.TypeName));
    }

    /// <summary>
    ///     Returns the user-facing rejection message, or <c>null</c> when the rename may proceed.
    ///     <paramref name="newName" /> is <c>null</c> during prepare (ambiguity check only).
    /// </summary>
    public static string? Check(string id, string? newName, GameIndex index)
    {
        if (!index.WorkspaceDefinitions.TryGetValue(id, out var defs)) return null;

        var eventDefinitions = defs.Count(d =>
            string.Equals(d.TypeName, StoryReferenceTypes.EventSymbol, StringComparison.OrdinalIgnoreCase));
        if (eventDefinitions > 1)
            return $"'{id}' names {eventDefinitions} story events in this workspace - renaming would " +
                   "change all of them and their references. Disambiguate the event names first.";

        if (newName is not null && newName.Length > MaxFlagNameLength && defs.Any(d =>
                string.Equals(d.TypeName, StoryReferenceTypes.FlagSymbol, StringComparison.OrdinalIgnoreCase)))
            return $"'{newName}' is {newName.Length} characters long - story flag names are limited to " +
                   $"{MaxFlagNameLength} (the engine truncates longer names).";

        return null;
    }
}