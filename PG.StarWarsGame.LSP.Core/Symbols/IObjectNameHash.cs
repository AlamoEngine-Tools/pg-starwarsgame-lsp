// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The number the engine files a game object under, and finds it by.
/// </summary>
/// <remarks>
///     Measured: the object lookup upper-cases the name, takes a standard CRC-32 of it and looks the
///     object up by that number alone, with no comparison of the name afterwards. Two different names
///     with the same hash are therefore one entry to the engine. Behind an interface so the
///     diagnostics can be tested with collisions on demand - real ones take a search to find.
/// </remarks>
public interface IObjectNameHash
{
    uint Of(string name);
}
