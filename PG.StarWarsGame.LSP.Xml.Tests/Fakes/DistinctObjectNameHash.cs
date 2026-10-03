// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Tests.Fakes;

/// <summary>
///     A name hash that a test can make collide on demand: names listed in the table get their
///     number, every other name its own. The default instance makes no two names collide.
/// </summary>
internal sealed class DistinctObjectNameHash(params (string Name, uint Hash)[] table) : IObjectNameHash
{
    public static readonly DistinctObjectNameHash Instance = new();

    public uint Of(string name)
    {
        foreach (var (n, h) in table)
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                return h;
        return (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name) | 0x8000_0000u;
    }
}
