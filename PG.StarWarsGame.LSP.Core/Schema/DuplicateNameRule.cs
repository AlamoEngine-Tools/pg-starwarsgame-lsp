// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     What the game does with a name two definitions in one registry's files share. The files load
///     in the registry's list order and the definitions in each file in node order; this says which
///     of them the game then uses. Measured per registry, in each manager's loader.
/// </summary>
public enum DuplicateNameRule
{
    /// <summary>The first definition keeps the name; a later one is never found by it.</summary>
    FIRST_WINS,

    /// <summary>A later definition replaces the earlier one under the name.</summary>
    LAST_WINS,

    /// <summary>A later definition is parsed onto the earlier one, as if its tags were repeated there.</summary>
    MERGE,

    /// <summary>Both definitions exist; a lookup by name finds the first.</summary>
    FIRST_BY_NAME
}