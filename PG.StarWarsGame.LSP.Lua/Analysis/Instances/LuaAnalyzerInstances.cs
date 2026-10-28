// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis.Generated;

namespace PG.StarWarsGame.LSP.Lua.Analysis.Instances;

/// <summary>
///     The analyzer as the game sees scripts: one analyzer per open script, holding that script's
///     instance and nothing else. The game runs every script instance in a Lua state of its own, with
///     the libraries it requires inside it, so a script sees its own globals, its libraries' and the
///     engine's - never another script's.
/// </summary>
/// <remarks>
///     <para>
///         Each instance's analyzer is pointed at a folder of its own and the engine stubs, and sent
///         its files: the script and its <c>require</c> tree resolved across the layers, every file at
///         a place below the folder (<see cref="LuaInstanceMirror" />), written there as well. The
///         analyzer infers a document when it arrives and keeps that when a file it requires comes
///         later (measured, 0.25.1); from the folder its startup scan reads them all first, which held
///         15 runs of 15 where sending alone lost the library's types in 5 of 8. A file read from disk
///         is sent with the annotations this server knows written into it, so a library a layer ships
///         carries its stub's types; the stub itself is read only for a library the tree requires and
///         no layer ships. A file open in the editor is sent as the editor has it, to every instance
///         holding it.
///     </para>
///     <para>
///         Requests go to the analyzer of the script they are about and come back with every place
///         mapped to its file; findings come back for the instance's own script only - a library file
///         open in the editor is an instance of its own. The index decides the trees: a change to it
///         (a require edited, a file changed on disk) re-sends what changed.
///     </para>
///     <para>
///         A script's analyzer starts once the script has stayed open for
///         <see cref="LuaAnalyzerInstanceTiming.Settle" />, and is kept for
///         <see cref="LuaAnalyzerInstanceTiming.Grace" /> after it closes, silent, for the script
///         reopened.
///     </para>
/// </remarks>
public sealed class LuaAnalyzerInstances : ILuaAnalyzer, ILuaAnalyzerLayers, IAsyncDisposable, IDisposable
{
    private const string RootFolder = "root";
    private const string Lenses = "textDocument/codeLens";
    private const string LensResolve = "codeLens/resolve";
    private const string LensScript = "aet.script";
    private const string LensData = "data";
    private static readonly JsonSerializer s_json = new LspSerializer().JsonSerializer;

    private readonly string _directory;
    private readonly IFileHelper _fileHelper;
    private readonly Lock _gate = new();
    private readonly IGameIndexService _index;
    private readonly Dictionary<string, Instance> _instances = new(DocumentUris.Comparer);
    private readonly ILogger<LuaAnalyzerInstances> _logger;
    private readonly Dictionary<string, (string Text, int Version)> _open = new(DocumentUris.Comparer);
    private readonly Dictionary<string, (Instance Instance, ITimer Timer)> _kept = new(DocumentUris.Comparer);
    private readonly Dictionary<string, ITimer> _settling = new(DocumentUris.Comparer);
    private readonly Func<string, ILuaAnalyzerProcess> _processes;
    private readonly Func<int, bool> _processAlive;
    private readonly LuaStubLocation _stubs;
    private readonly LuaGeneratedTexts _texts;
    private readonly TimeProvider _time;
    private readonly LuaAnalyzerInstanceTiming _timing;
    private bool _disposed;
    private int _next;
    private int _refreshQueued;
    private IReadOnlyList<string>? _scriptRoots;

    /// <param name="processes">Creates an analyzer process with its configuration in the given directory.</param>
    /// <param name="index">The index the require trees are resolved over.</param>
    /// <param name="fileHelper">URI normalization, and the disk the unopened files are read from.</param>
    /// <param name="stubs">Where the stubs are, and which stand in for a library.</param>
    /// <param name="texts">The annotations written into a file read from disk.</param>
    /// <param name="directory">The directory every instance's own directory is made in.</param>
    /// <param name="logger">The log.</param>
    /// <param name="processAlive">
    ///     Whether a process of the given id runs: the directory beside <paramref name="directory" /> of
    ///     one that does not is a server's that ended without stopping its analyzers, and is removed.
    /// </param>
    /// <param name="timing">When an analyzer starts and stops around its script; none waits by default.</param>
    /// <param name="time">The clock <paramref name="timing" /> is measured on.</param>
    public LuaAnalyzerInstances(Func<string, ILuaAnalyzerProcess> processes, IGameIndexService index,
        IFileHelper fileHelper, LuaStubLocation stubs, LuaGeneratedTexts texts, string directory,
        ILogger<LuaAnalyzerInstances> logger, Func<int, bool>? processAlive = null,
        LuaAnalyzerInstanceTiming? timing = null, TimeProvider? time = null)
    {
        _timing = timing ?? LuaAnalyzerInstanceTiming.Immediate;
        _time = time ?? TimeProvider.System;
        _processes = processes;
        _index = index;
        _fileHelper = fileHelper;
        _stubs = stubs;
        _texts = texts;
        _directory = directory;
        _logger = logger;
        _processAlive = processAlive ?? IsAlive;
        _index.IndexChanged += _ => QueueRefresh();
    }

    public async ValueTask DisposeAsync()
    {
        List<Instance> running;
        lock (_gate)
        {
            _disposed = true;
            running = TakeAll();
        }

        foreach (var instance in running) await instance.Process.DisposeAsync();
        RemoveDirectory(_directory);
    }

    /// <summary>The container's shutdown path: every process is ended, no handshake.</summary>
    public void Dispose()
    {
        List<Instance> running;
        lock (_gate)
        {
            _disposed = true;
            running = TakeAll();
        }

        foreach (var instance in running) instance.Process.Dispose();
        RemoveDirectory(_directory);
    }

    /// <summary>Whether the project is known and the analyzer is on: every open script then has an instance.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _scriptRoots is not null && !_disposed;
            }
        }
    }

    public event Action<PublishDiagnosticsParams>? DiagnosticsPublished;

    public void DidOpen(string uri, string text, int version)
    {
        var key = _fileHelper.NormalizeUri(uri);
        lock (_gate)
        {
            _open[key] = (text, version);
            if (!Configured) return;
            foreach (var instance in _instances.Values) Resend(instance, key);
            if (_instances.ContainsKey(key) || _settling.ContainsKey(key)) return;
            if (_kept.Remove(key, out var kept))
            {
                kept.Timer.Dispose();
                Reopen(kept.Instance);
            }
            else if (_timing.Settle <= TimeSpan.Zero)
            {
                Start(key);
            }
            else
            {
                _settling[key] = _time.CreateTimer(_ => Settled(key), null, _timing.Settle, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void DidChange(string uri, string text, int version)
    {
        var key = _fileHelper.NormalizeUri(uri);
        lock (_gate)
        {
            _open[key] = (text, version);
            if (!Configured) return;
            foreach (var instance in _instances.Values) Resend(instance, key);
        }
    }

    public void DidClose(string uri)
    {
        var key = _fileHelper.NormalizeUri(uri);
        lock (_gate)
        {
            _open.Remove(key);
            if (_settling.Remove(key, out var settling)) settling.Dispose();
            if (_instances.Remove(key, out var own)) Keep(own);
            foreach (var instance in _instances.Values) Resend(instance, key);
        }
    }

    public async Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class?
    {
        var request = JToken.FromObject(parameters, s_json);
        // A lens names its declaration alone (measured, 0.25.1), so each carries the script whose
        // analyzer made it, and its resolve goes back there with the lens as made.
        var lens = method == LensResolve ? request["data"] as JObject : null;
        if (lens?[LensScript] is not null) request["data"] = lens[LensData];
        var origin = request["textDocument"]?["uri"]?.Value<string>() ?? lens?[LensScript]?.Value<string>();
        Instance? instance;
        lock (_gate)
        {
            if (origin is null) return null;
            var key = _fileHelper.NormalizeUri(origin);
            // The editor asks about a script it shows - its outline the moment it opens it, kept until
            // the script changes - so a request does not wait out the settle.
            if (!_instances.TryGetValue(key, out instance) && _settling.Remove(key, out var settling))
            {
                settling.Dispose();
                Start(key);
                instance = _instances[key];
            }

            if (instance is null) return null;
            instance.Mirror.ToMirror(request);
        }

        try
        {
            await instance.Started.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        var answer = await instance.Process.RequestAsync<JToken>(method, request, ct);
        if (answer is null || answer.Type == JTokenType.Null) return null;
        lock (_gate)
        {
            instance.Mirror.ToReal(answer);
        }

        if (method == LensResolve && answer is JObject resolved) resolved["data"] = lens;
        if (method == Lenses && answer is JArray made)
            foreach (var item in made.OfType<JObject>())
                item["data"] = new JObject { [LensScript] = origin, [LensData] = item["data"] ?? JValue.CreateNull() };

        return answer.ToObject<T>(s_json);
    }

    /// <summary>
    ///     The project's layers, known or reloaded: every open script gets an instance resolved over
    ///     them, and every instance of an earlier load stops.
    /// </summary>
    public void Configure(IReadOnlyList<ProjectLayer> layers)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_scriptRoots is null) RemoveStaleDirectories();
            _scriptRoots = layers.SelectMany(l => l.ScriptRoots).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var instance in TakeAll()) Stop(instance);
            foreach (var uri in _open.Keys.ToList()) Start(uri);
        }
    }

    private bool Configured => _scriptRoots is not null && !_disposed;

    // ── directories ──────────────────────────────────────────────────────────

    private void RemoveStaleDirectories()
    {
        // Housekeeping: it never keeps the analyzer from starting.
        try
        {
            var fs = _fileHelper.FileSystem;
            if (fs.Path.GetDirectoryName(fs.Path.GetFullPath(_directory)) is not { } parent ||
                !fs.Directory.Exists(parent))
                return;
            foreach (var sibling in fs.Directory.EnumerateDirectories(parent))
                if (int.TryParse(fs.Path.GetFileName(sibling), out var pid) && !_processAlive(pid))
                    RemoveDirectory(sibling);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Lua analyzer directories of ended servers not removed");
        }
    }

    private void RemoveDirectory(string directory)
    {
        try
        {
            if (_fileHelper.FileSystem.Directory.Exists(directory))
                _fileHelper.FileSystem.Directory.Delete(directory, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Lua analyzer directory {Directory} not removed", directory);
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A process this user may not open (measured live: access denied) exists all the same.
            return true;
        }
    }

    // ── instances ────────────────────────────────────────────────────────────

    private void Start(string uri)
    {
        var directory = Path.Combine(_directory, (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var root = Path.Combine(directory, RootFolder);
        _fileHelper.FileSystem.Directory.CreateDirectory(root);
        var process = _processes(directory);
        var instance = new Instance(uri, process, root, new LuaInstanceMirror(root, _scriptRoots!, _fileHelper));
        process.DiagnosticsPublished += published => OnPublished(instance, published);
        _instances[uri] = instance;
        Sync(instance, _index.Current);
        Launch(instance);
    }

    private void Stop(Instance instance)
    {
        _ = StopAsync(instance.Process);
    }

    // Every instance, the kept ones too, out of the books; nothing waits to start any more.
    private List<Instance> TakeAll()
    {
        foreach (var timer in _settling.Values) timer.Dispose();
        _settling.Clear();
        var all = _instances.Values.Concat(_kept.Values.Select(k => k.Instance)).ToList();
        foreach (var kept in _kept.Values) kept.Timer.Dispose();
        _kept.Clear();
        _instances.Clear();
        return all;
    }

    private void Settled(string uri)
    {
        lock (_gate)
        {
            if (!_settling.Remove(uri, out var timer)) return;
            timer.Dispose();
            if (Configured && _open.ContainsKey(uri) && !_instances.ContainsKey(uri)) Start(uri);
        }
    }

    // A closed script's instance, kept silent for the grace: out of _instances, nothing it publishes is passed on.
    private void Keep(Instance instance)
    {
        if (_timing.Grace <= TimeSpan.Zero)
        {
            Stop(instance);
            return;
        }

        var timer = _time.CreateTimer(_ => Expired(instance), null, _timing.Grace, Timeout.InfiniteTimeSpan);
        _kept[instance.Uri] = (instance, timer);
    }

    private void Expired(Instance instance)
    {
        lock (_gate)
        {
            if (!_kept.TryGetValue(instance.Uri, out var kept) || !ReferenceEquals(kept.Instance, instance)) return;
            _kept.Remove(instance.Uri);
            kept.Timer.Dispose();
            Stop(instance);
        }
    }

    // The kept instance serves its script again: what changed while it was kept is sent, and the
    // script's new version makes the analyzer infer and report it again.
    private void Reopen(Instance instance)
    {
        _instances[instance.Uri] = instance;
        if (Sync(instance, _index.Current))
        {
            Launch(instance);
            return;
        }

        foreach (var file in instance.Held.Keys.ToList()) Resend(instance, file);
        Touch(instance);
    }

    private async Task StopAsync(ILuaAnalyzerProcess process)
    {
        try
        {
            await process.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Lua analyzer instance did not stop cleanly");
        }
    }

    private void Launch(Instance instance)
    {
        _ = LaunchAsync(instance, new LuaAnalyzerInstancePlan(instance.Root, instance.Library));
    }

    private async Task LaunchAsync(Instance instance, LuaAnalyzerInstancePlan plan)
    {
        try
        {
            await instance.Process.StartAsync(plan);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Lua analyzer instance not started");
            return;
        }
        finally
        {
            instance.Started.TrySetResult();
        }

        lock (_gate)
        {
            Touch(instance);
        }
    }

    // The script again, unchanged but for its version. The analyzer infers a document once, when it
    // arrives, and keeps what it inferred when a file the document requires arrives or changes later
    // (measured, 0.25.1: a local assigned a library function's result stayed unknown after the
    // library came in); a new version of the script makes it infer again, against everything sent.
    private void Touch(Instance instance)
    {
        if (!ReferenceEquals(_instances.GetValueOrDefault(instance.Uri), instance) ||
            !instance.Held.TryGetValue(instance.Uri, out var held))
            return;
        var version = held.Version + 1;
        instance.Process.DidChange(instance.Mirror.PlaceOf(instance.Uri)!, held.Text, version);
        instance.Held[instance.Uri] = held with { Version = version };
    }

    // Brings the instance's files and library in line with the index; true when the library changed.
    private bool Sync(Instance instance, GameIndex index)
    {
        var tree = LuaRequireTree.Of(instance.Uri, index.Documents, _fileHelper);
        var wanted = new HashSet<string>(tree.Files, DocumentUris.Comparer);

        foreach (var file in instance.Held.Keys.Where(f => !wanted.Contains(f)).ToList())
        {
            instance.Process.DidClose(instance.Mirror.PlaceOf(file)!);
            instance.Mirror.Remove(file);
            instance.Held.Remove(file);
        }

        // What a file requires before the file: the analyzer infers a document when it arrives.
        foreach (var file in tree.Files.Reverse())
        {
            var document = index.Documents.GetValueOrDefault(file);
            if (instance.Held.TryGetValue(file, out var held))
            {
                if (!_open.ContainsKey(file) && !ReferenceEquals(held.Seen, document)) Resend(instance, file);
                instance.Held[file] = instance.Held[file] with { Seen = document };
                continue;
            }

            if (TextOf(file) is not { } text) continue;
            var place = instance.Mirror.Add(file);
            Write(place, text);
            instance.Process.DidOpen(place, text, 1);
            instance.Held[file] = new Sent(document, 1, text);
        }

        var library = LibraryOf(tree);
        if (library.SequenceEqual(instance.Library, StringComparer.Ordinal)) return false;
        instance.Library = library;
        return true;
    }

    // The file's current text sent to the instance, when it holds the file and the text is new.
    private void Resend(Instance instance, string file)
    {
        if (!instance.Held.TryGetValue(file, out var held) || TextOf(file) is not { } text ||
            string.Equals(text, held.Text, StringComparison.Ordinal))
            return;
        var version = held.Version + 1;
        var place = instance.Mirror.PlaceOf(file)!;
        // An open file's copy stays as it was: the analyzer reads a write under its folder as a change
        // of its own and, sent the edit as well, reported nothing on 17 of 24 edits (measured, 0.25.1).
        // A restart replays the open documents over the copies.
        if (!_open.ContainsKey(file)) Write(place, text);
        instance.Process.DidChange(place, text, version);
        instance.Held[file] = held with { Version = version, Text = text };
    }

    // The text at its place on disk too, for the analyzer's own scan of the folder when it starts.
    private void Write(string place, string text)
    {
        if (_fileHelper.FileUriToPath(place) is not { } path) return;
        try
        {
            var fs = _fileHelper.FileSystem;
            if (fs.Path.GetDirectoryName(path) is { } directory) fs.Directory.CreateDirectory(directory);
            fs.File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Lua analyzer instance: {Place} not written", place);
        }
    }

    // As the editor has it (generated already), else as on disk with the annotations written in.
    private string? TextOf(string file)
    {
        if (_open.TryGetValue(file, out var open)) return open.Text;
        if (_fileHelper.FileUriToPath(file) is not { } path || !_fileHelper.FileSystem.File.Exists(path)) return null;
        try
        {
            return _texts.Generate(file, _fileHelper.FileSystem.File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Lua analyzer instance: {File} not read", file);
            return null;
        }
    }

    // The engine's stubs for every instance; a library's stub where the tree requires it and no layer ships it.
    private IReadOnlyList<string> LibraryOf(LuaRequireTree tree)
    {
        if (_stubs.Directory is not { } directory) return [];
        var standIns = new HashSet<string>(_stubs.Libraries.Values, StringComparer.OrdinalIgnoreCase);
        return _stubs.Files.Where(f => !standIns.Contains(f))
            .Concat(tree.Unresolved.Select(m => _stubs.Libraries.GetValueOrDefault(m)).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => Path.Combine(directory, f))
            .ToList();
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        _ = Task.Run(Refresh);
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        var index = _index.Current;
        lock (_gate)
        {
            if (!Configured) return;
            foreach (var instance in _instances.Values)
                if (Sync(instance, index))
                    Launch(instance);
        }
    }

    private void OnPublished(Instance instance, PublishDiagnosticsParams published)
    {
        JToken json;
        lock (_gate)
        {
            if (!ReferenceEquals(_instances.GetValueOrDefault(instance.Uri), instance) ||
                instance.Mirror.RealOf(published.Uri.ToString()) is not { } file ||
                !DocumentUris.Same(file, instance.Uri))
                return;
            json = JToken.FromObject(published, s_json);
            instance.Mirror.ToReal(json);
        }

        DiagnosticsPublished?.Invoke(json.ToObject<PublishDiagnosticsParams>(s_json)!);
    }

    private sealed record Sent(DocumentIndex? Seen, int Version, string Text);

    private sealed class Instance(string uri, ILuaAnalyzerProcess process, string root, LuaInstanceMirror mirror)
    {
        public string Uri { get; } = uri;
        public ILuaAnalyzerProcess Process { get; } = process;
        public string Root { get; } = root;
        public LuaInstanceMirror Mirror { get; } = mirror;
        public Dictionary<string, Sent> Held { get; } = new(DocumentUris.Comparer);
        public IReadOnlyList<string> Library { get; set; } = [];

        /// <summary>Done once its analyzer's first start has ended, started or not: a request waits for it.</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}