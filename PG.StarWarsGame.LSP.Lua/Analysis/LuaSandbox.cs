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
///     The analyzer is given one script instance at a time (<see cref="Instances.LuaAnalyzerInstances" />),
///     so its locations lie in one state already; they still pass through this, which keeps them so.
///     A file the index does not hold is kept, since nothing here can judge it.
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
        foreach (var uri in documents.Keys)
        {
            if (!uri.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) continue;
            closures[uri] = new HashSet<string>(LuaRequireTree.Of(uri, documents, fileHelper).Files,
                DocumentUris.Comparer);
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
    ///     True when a state other than the file's own holds it: a library some script requires, whose
    ///     use is downstream. A script nobody requires lives in its own state alone.
    /// </summary>
    public bool IsShared(string uri)
    {
        var file = _fileHelper.NormalizeUri(uri);
        return _statesOf.TryGetValue(file, out var states) &&
               states.Any(state => !DocumentUris.Same(state, file));
    }

    /// <summary>
    ///     The states holding the file, each named by its script: the file's own, and every script
    ///     requiring it. Empty for a file the index does not hold.
    /// </summary>
    public IReadOnlyList<string> StatesHolding(string uri)
    {
        return _statesOf.TryGetValue(_fileHelper.NormalizeUri(uri), out var states) ? states : [];
    }

    /// <summary>Whether the state of <paramref name="state" /> holds <paramref name="file" />.</summary>
    public bool Holds(string state, string file)
    {
        return _closures.TryGetValue(_fileHelper.NormalizeUri(state), out var closure) &&
               closure.Contains(_fileHelper.NormalizeUri(file));
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