// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

public sealed class LuaAnnotationRepository : ILuaAnnotationRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ImmutableArray<EmmyLuaAnnotations>> _store =
        new(StringComparer.Ordinal);
    private ILuaTypeIndex _current = LuaTypeIndex.Empty;

    public void Update(string uri, ImmutableArray<EmmyLuaAnnotations> annotations)
    {
        lock (_lock) _store[uri] = annotations;
    }

    public void Remove(string uri)
    {
        lock (_lock) _store.Remove(uri);
    }

    public IReadOnlyDictionary<string, ImmutableArray<EmmyLuaAnnotations>> All
    {
        get
        {
            lock (_lock)
                return new Dictionary<string, ImmutableArray<EmmyLuaAnnotations>>(_store, StringComparer.Ordinal);
        }
    }

    public ILuaTypeIndex Current
    {
        get { lock (_lock) return _current; }
    }

    public void RebuildIndex()
    {
        List<EmmyLuaAnnotations> snapshot;
        lock (_lock) snapshot = [.. _store.Values.SelectMany(a => a)];
        var newIndex = LuaTypeIndex.Build(snapshot);
        lock (_lock) _current = newIndex;
    }
}
