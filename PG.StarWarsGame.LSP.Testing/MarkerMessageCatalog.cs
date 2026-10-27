// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     A catalogue that answers every key with <c>[key]</c>. A text that comes out of it proves the
///     code took the catalogue it was given rather than one it built; a text that comes out in English
///     did not.
/// </summary>
public sealed class MarkerMessageCatalog : IMessageCatalog
{
    public string Format(string key, params object?[] args)
    {
        return $"[{key}]";
    }
}