// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed class RegistryWalkStore : IRegistryWalkStore
{
    private volatile IReadOnlyList<RegistryWalk> _current = [];

    public IReadOnlyList<RegistryWalk> Current => _current;

    public void Replace(IReadOnlyList<RegistryWalk> walks)
    {
        _current = [.. walks];
    }
}