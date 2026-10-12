// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>The sandbox of the current index; rebuilt when the index changes, on first use.</summary>
public interface ILuaSandboxProvider
{
    LuaSandbox Current { get; }
}

/// <inheritdoc />
public sealed class LuaSandboxProvider(IGameIndexService index, IFileHelper fileHelper) : ILuaSandboxProvider
{
    private readonly Lock _gate = new();
    private GameIndex? _builtFrom;
    private LuaSandbox? _sandbox;

    public LuaSandbox Current
    {
        get
        {
            var current = index.Current;
            lock (_gate)
            {
                if (_sandbox is null || !ReferenceEquals(current, _builtFrom))
                {
                    _sandbox = LuaSandbox.Build(current.Documents, fileHelper);
                    _builtFrom = current;
                }

                return _sandbox;
            }
        }
    }
}

/// <summary>
///     Which scripts see each other's globals. The game runs every script instance in a Lua state of
///     its own, and a required library runs inside the requiring script's state, so a state is a
///     script plus its <c>require</c> closure. Two files share a state when some script's state
///     holds both (#249).
/// </summary>
/// <remarks>
///     The analyzer sees one global namespace over the whole workspace; its locations are filtered
///     through this. A file the index does not hold - a lower layer the analyzer reads as a library,
///     the game's own scripts - is kept, since nothing here can judge it.
/// </remarks>
public sealed class LuaSandbox
{
    private readonly Dictionary<string, HashSet<string>> _closures;
    private readonly Dictionary<string, HashSet<string>> _sharedWith = new(DocumentUris.Comparer);
    private readonly IFileHelper _fileHelper;
    private readonly Dictionary<string, List<string>> _statesOf;

    private LuaSandbox(Dictionary<string, HashSet<string>> closures, IFileHelper fileHelper)
    {
        _closures = closures;
        _fileHelper = fileHelper;
        _statesOf = new Dictionary<string, List<string>>(DocumentUris.Comparer);
        foreach (var (script, closure) in closures)
        foreach (var file in closure)
        {
            if (!_statesOf.TryGetValue(file, out var states)) _statesOf[file] = states = [];
            states.Add(script);
        }
    }

    /// <summary>One state per Lua document: the document plus everything it requires, transitively.</summary>
    public static LuaSandbox Build(IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper)
    {
        var closures = new Dictionary<string, HashSet<string>>(DocumentUris.Comparer);
        foreach (var (uri, doc) in documents)
        {
            if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
            var closure = new HashSet<string>(DocumentUris.Comparer) { uri };
            var queue = new Queue<DocumentIndex>([doc]);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.RequireArgs.IsDefaultOrEmpty) continue;
                foreach (var arg in current.RequireArgs)
                    if (LuaRequireResolver.Resolve(arg, documents, fileHelper, current.DocumentUri) is { } required
                        && closure.Add(required) && documents.TryGetValue(required, out var next))
                        queue.Enqueue(next);
            }

            closures[uri] = closure;
        }

        return new LuaSandbox(closures, fileHelper);
    }

    /// <summary>True when some script's state holds both files, or when either is not a known script.</summary>
    public bool ShareAState(string fromUri, string otherUri)
    {
        var from = _fileHelper.NormalizeUri(fromUri);
        var other = _fileHelper.NormalizeUri(otherUri);
        if (!_statesOf.ContainsKey(from) || !_statesOf.ContainsKey(other)) return true;
        return SharedWith(from).Contains(other);
    }

    /// <summary>
    ///     The files a rename of a global from <paramref name="fromUri" /> may edit: the states holding
    ///     it, and every state holding a file of theirs that names the global - transitively. A library
    ///     defining or calling the global is one text in many states, so renaming it there renames it in
    ///     all of them; a shared file that never names it widens nothing.
    /// </summary>
    /// <param name="fromUri">The file the rename starts in.</param>
    /// <param name="naming">Every file defining or referencing the global, in any state.</param>
    public Func<string, bool> RenameScope(string fromUri, IEnumerable<string> naming)
    {
        var from = _fileHelper.NormalizeUri(fromUri);
        if (!_statesOf.TryGetValue(from, out var start)) return _ => true;

        var namingFiles = naming.Select(_fileHelper.NormalizeUri).ToHashSet(DocumentUris.Comparer);
        var states = new HashSet<string>(DocumentUris.Comparer);
        var files = new HashSet<string>(DocumentUris.Comparer);
        var pending = new Queue<string>(start);
        while (pending.Count > 0)
        {
            var state = pending.Dequeue();
            if (!states.Add(state)) continue;
            foreach (var file in _closures[state])
                if (files.Add(file) && namingFiles.Contains(file))
                    foreach (var other in _statesOf[file])
                        pending.Enqueue(other);
        }

        return uri =>
        {
            var normalized = _fileHelper.NormalizeUri(uri);
            return !_statesOf.ContainsKey(normalized) || files.Contains(normalized);
        };
    }

    private HashSet<string> SharedWith(string file)
    {
        lock (_sharedWith)
        {
            if (_sharedWith.TryGetValue(file, out var shared)) return shared;
            shared = new HashSet<string>(DocumentUris.Comparer);
            foreach (var state in _statesOf[file])
                shared.UnionWith(_closures[state]);
            _sharedWith[file] = shared;
            return shared;
        }
    }
}