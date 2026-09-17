// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <summary>
///     Reads the game's formatted call-stack entries, <c>source:line:what:namewhat:name</c>, into
///     frames. The source can itself contain a colon (a drive letter), so fields are taken from
///     the right.
/// </summary>
public interface ICallstackParser
{
    /// <summary>One entry, or null when it does not have the expected shape.</summary>
    CallstackFrame? ParseEntry(int level, string entry);

    /// <summary>
    ///     The whole stack as sent, outermost frame first, returned top frame first with each
    ///     frame's wire index as its level. Entries that do not parse are kept as frames with an
    ///     empty source and line 0, so the level numbering stays aligned with the game's.
    /// </summary>
    /// <param name="entries">The call-stack entries as the game sent them.</param>
    /// <param name="dropDuplicateOutermost">
    ///     Drop the outermost entry when it is identical to the one above it. The game's stack walk
    ///     is suspected of emitting the outermost frame twice; until that is measured, a duplicate
    ///     is the only shape that is dropped.
    /// </param>
    IReadOnlyList<CallstackFrame> ParseStack(IReadOnlyList<string> entries, bool dropDuplicateOutermost);
}