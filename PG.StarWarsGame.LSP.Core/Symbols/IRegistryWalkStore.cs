// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The registry walks of the last metafile scan, replaced wholesale on every scan.
/// </summary>
public interface IRegistryWalkStore
{
    IReadOnlyList<RegistryWalk> Current { get; }

    void Replace(IReadOnlyList<RegistryWalk> walks);
}