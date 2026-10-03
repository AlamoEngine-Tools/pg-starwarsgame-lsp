// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.Commons.Hashing;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Symbols;

/// <summary>
///     <see cref="IObjectNameHash" /> through PG.Commons' CRC-32 - the same service the localisation
///     rows hash their keys with.
/// </summary>
/// <remarks>
///     ASCII, because the engine hashes the name's bytes as it read them and object names are ASCII
///     in every shipped file. A name with characters outside it is upper-cased here by the invariant
///     culture, where the engine's own upper-casing is not measured for them.
/// </remarks>
public sealed class EngineObjectNameHash : IObjectNameHash
{
    private readonly ICrc32HashingService _hashing;

    public EngineObjectNameHash(ICrc32HashingService hashing)
    {
        _hashing = hashing;
    }

    public uint Of(string name)
    {
        return (uint)_hashing.GetCrc32Upper(name.AsSpan(), Encoding.ASCII);
    }
}