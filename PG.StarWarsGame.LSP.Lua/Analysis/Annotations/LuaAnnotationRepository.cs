// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Annotations;

public sealed class LuaAnnotationRepository : ILuaAnnotationRepository
{
    // name → { uri → annotation } - keeps all definitions so richest-wins can pick the best one.
    private readonly Dictionary<string, Dictionary<string, EmmyLuaAnnotations>> _functionAnnotationsMap =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, HashSet<string>> _functionsByUri =
        new(StringComparer.Ordinal);

    private readonly object _lock = new();

    private readonly Dictionary<string, ImmutableArray<EmmyLuaAnnotations>> _store =
        new(StringComparer.Ordinal);

    private ILuaTypeIndex _current = LuaTypeIndex.Empty;

    public void Update(string uri, ImmutableArray<EmmyLuaAnnotations> annotations)
    {
        lock (_lock)
        {
            _store[uri] = annotations;
        }
    }

    public void Remove(string uri)
    {
        lock (_lock)
        {
            _store.Remove(uri);
            RemoveFunctionAnnotationsForUri(uri);
        }
    }

    public void UpdateFunctionAnnotations(string uri, IReadOnlyList<(string Name, EmmyLuaAnnotations Ann)> functions)
    {
        lock (_lock)
        {
            RemoveFunctionAnnotationsForUri(uri);

            var names = new HashSet<string>(functions.Count, StringComparer.Ordinal);
            foreach (var (name, ann) in functions)
            {
                if (!_functionAnnotationsMap.TryGetValue(name, out var byUri))
                    _functionAnnotationsMap[name] =
                        byUri = new Dictionary<string, EmmyLuaAnnotations>(StringComparer.Ordinal);
                byUri[uri] = ann;
                names.Add(name);
            }

            _functionsByUri[uri] = names;
        }
    }

    public EmmyLuaAnnotations? GetFunctionAnnotation(string name)
    {
        lock (_lock)
        {
            if (!_functionAnnotationsMap.TryGetValue(name, out var byUri)) return null;

            // Return the first non-empty annotation (richest wins); fall back to any.
            EmmyLuaAnnotations? fallback = null;
            foreach (var ann in byUri.Values)
            {
                if (ann.Description is not null || !ann.Params.IsDefaultOrEmpty || !ann.Returns.IsDefaultOrEmpty)
                    return ann;
                fallback ??= ann;
            }

            return fallback;
        }
    }

    public IReadOnlyDictionary<string, ImmutableArray<EmmyLuaAnnotations>> All
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, ImmutableArray<EmmyLuaAnnotations>>(_store, StringComparer.Ordinal);
            }
        }
    }

    public ILuaTypeIndex Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public void RebuildIndex()
    {
        List<EmmyLuaAnnotations> snapshot;
        lock (_lock)
        {
            snapshot = [.. _store.Values.SelectMany(a => a)];
        }

        var newIndex = LuaTypeIndex.Build(snapshot);
        lock (_lock)
        {
            _current = newIndex;
        }
    }

    private void RemoveFunctionAnnotationsForUri(string uri)
    {
        if (!_functionsByUri.TryGetValue(uri, out var names)) return;
        foreach (var name in names)
            if (_functionAnnotationsMap.TryGetValue(name, out var byUri))
            {
                byUri.Remove(uri);
                if (byUri.Count == 0) _functionAnnotationsMap.Remove(name);
            }

        _functionsByUri.Remove(uri);
    }
}