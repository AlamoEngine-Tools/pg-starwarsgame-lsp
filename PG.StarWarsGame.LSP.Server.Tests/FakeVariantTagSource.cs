// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Tests;

/// <summary>
///     Hands out the direct child tags of an object from an in-memory map, standing in for the
///     parsed-document tag source. Ids match case-insensitively, the way the real index addresses them.
/// </summary>
internal sealed class FakeVariantTagSource : IVariantTagSource
{
    private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
    {
        return _byId.GetValueOrDefault(objectId);
    }

    public FakeVariantTagSource With(string id, params VariantTag[] tags)
    {
        _byId[id] = tags;
        return this;
    }
}
