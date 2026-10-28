// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     Hands out the direct child tags of an object from an in-memory map, standing in for the
///     parsed-document tag source. Ids match case-insensitively, the way the real index addresses them.
///     Empty, it knows no object: every cross-object rule then finds nothing to say.
/// </summary>
public sealed class FakeVariantTagSource : IVariantTagSource
{
    private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<GameSymbol, IReadOnlyList<VariantTag>> _bySymbol = [];

    public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
    {
        return _byId.GetValueOrDefault(objectId);
    }

    /// <summary>The tags registered for this very symbol, else those registered for its id.</summary>
    public IReadOnlyList<VariantTag>? TryGetTags(GameSymbol symbol)
    {
        return _bySymbol.GetValueOrDefault(symbol) ?? TryGetTags(symbol.Id);
    }

    public FakeVariantTagSource With(string id, params VariantTag[] tags)
    {
        _byId[id] = tags;
        return this;
    }

    /// <summary>Tags for one symbol only, for objects of different kinds that share an id.</summary>
    public FakeVariantTagSource With(GameSymbol symbol, params VariantTag[] tags)
    {
        _bySymbol[symbol] = tags;
        return this;
    }
}