// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Story.Sim;

namespace PG.StarWarsGame.LSP.Story.Tests;

/// <summary>
///     A world in which every planet, unit type and faction an author names exists. The simulator's
///     symbol source is required (#305); a test about its clock, its flags or its battles hands it
///     this one, so the test is not also a test about the index.
/// </summary>
internal sealed class AnyWorldSymbols : IStoryWorldSymbols
{
    public bool Exists(string kind, string name)
    {
        return true;
    }
}