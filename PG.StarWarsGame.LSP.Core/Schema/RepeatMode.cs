// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     What a second occurrence of a tag does when the game parses it without the variant overlay:
///     a tag repeated inside one object, or a second definition parsed into the same object, as a
///     targeting set of an existing name is. Follows from the engine's field code, measured on both
///     2024 x64 builds.
/// </summary>
public enum RepeatMode
{
    /// <summary>A single value: the later occurrence replaces the earlier one.</summary>
    REPLACE = 0,

    /// <summary>A list: the later occurrence's entries are added after the earlier ones.</summary>
    ADD
}