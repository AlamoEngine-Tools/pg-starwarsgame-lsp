// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>One model to convert, and the clips to convert it with - what a request for it will name.</summary>
/// <param name="ModelReference">The model, as a request names it: a file name.</param>
/// <param name="Animations">The clips offered with it, by file name, in the order a request offers them.</param>
public sealed record PreviewBuildItem(string ModelReference, IReadOnlyList<string> Animations)
{
    /// <summary>What makes two items the same conversion: the model and the clips, whatever their case.</summary>
    public string Key => (PreviewModelReference.Normalise(ModelReference) + "|" + string.Join(",", Animations))
        .ToLowerInvariant();
}

/// <summary>Shows the background build to the reader, who may cancel it.</summary>
public interface IPreviewBuildProgress
{
    /// <summary>
    ///     Starts showing a build of <paramref name="total" /> models.
    ///     <paramref name="cancel" /> is called when the reader cancels it.
    /// </summary>
    IPreviewBuildProgressRun Begin(int total, Action cancel);
}

/// <summary>One build being shown. Disposed when it ends, however it ends.</summary>
public interface IPreviewBuildProgressRun : IDisposable
{
    /// <summary>Says how far the build is: <paramref name="done" /> of <paramref name="total" />.</summary>
    void Report(int done, int total);
}

/// <summary>Fills the preview cache ahead of the requests that will read it.</summary>
public interface IPreviewCacheBuilder
{
    /// <summary>
    ///     Converts every model of the project in the background, unless a build of this project
    ///     already ran to its end. Returns at once; the work is on a thread of its own.
    /// </summary>
    void StartUnlessBuilt();

    /// <summary>
    ///     Puts these models at the front of the queue, in this order, ahead of everything waiting.
    ///     Called for the models of a subject the reader has just opened; ones the queue did not
    ///     hold - a dependency's, a death clone's - are added.
    /// </summary>
    void Prioritise(IReadOnlyList<PreviewBuildItem> items);

    /// <summary>
    ///     Converts again, in the background, the models these files are or belong to: a model
    ///     itself, the models a clip is offered with, and the models and clips an archive holds.
    ///     Called with every model, clip, texture and archive the file watcher reports. Starts the
    ///     worker if it is not running; this work is not shown.
    /// </summary>
    void FilesChanged(IReadOnlyCollection<string> paths);

    /// <summary>
    ///     Stops the build after the model in hand. What it had not reached is converted when
    ///     something asks for it, or by the next start.
    /// </summary>
    void Cancel();
}

/// <summary>
///     The background build of the preview cache (#436): once a project's first index is live it
///     converts every model of the project, so that opening one reads a converted model instead of
///     converting it.
/// </summary>
/// <remarks>
///     <para>
///         <strong>The project's own files.</strong> The models under the root project's asset
///         roots; a dependency's and the game's are converted when something asks for them. Each is
///         converted with the clips the scene would offer for it
///         (<see cref="ModelAnimationClips.For" />), so the entry a build leaves is the entry a plain
///         open asks for.
///     </para>
///     <para>
///         <strong>Never in the way.</strong> One thread below normal priority, one model at a time,
///         started only after the index is live. A request does not wait for the queue: it converts
///         what it needs itself, and the cache makes the two agree on one conversion - where the
///         model a request needs is the one this thread has in hand, the request waits for it and
///         the thread runs at the request's priority until it is done. What a request does do is
///         <see cref="Prioritise" /> - the subject just opened will ask for its wrecks and death
///         clones next, so those go to the front.
///     </para>
///     <para>
///         <strong>Once.</strong> A build that ran to its end leaves a marker in the cache folder
///         naming the converter version. While it stands the build does not start by itself again,
///         and the cache fills on a miss. A cancelled build leaves none and resumes at the next
///         start, skipping what is already there; a model saved after the cancel is converted like
///         any other save and ends no build. Clearing the cache removes the marker with it.
///     </para>
/// </remarks>
public sealed class PreviewCacheBuilder(
    IGameIndexService index,
    IModProjectReloadService projects,
    IPreviewModelCache cache,
    IModelGlbConverter converter,
    IPreviewCacheLocation cacheLocation,
    IFileHelper fileHelper,
    IMegEntryReader archives,
    IPreviewBuildProgress progress,
    ILogger<PreviewCacheBuilder> logger) : IPreviewCacheBuilder, IDisposable
{
    /// <summary>The marker a finished build leaves in the cache folder.</summary>
    public const string MarkerFileName = ".complete";

    private readonly Lock _gate = new();
    private readonly LinkedList<PreviewBuildItem> _queue = [];
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private int _done;
    private bool _fullBuild;
    private CancellationTokenSource? _run;
    private int _total;
    private Thread? _worker;

    /// <summary>Completes when the worker has emptied its queue or was cancelled. For tests.</summary>
    public Task Idle
    {
        get
        {
            lock (_gate)
            {
                return _idle.Task;
            }
        }
    }

    private TaskCompletionSource _idle = Completed();

    public void StartUnlessBuilt()
    {
        var directory = cacheLocation.Directory;
        if (directory is null || MarkerStands(directory))
            return;

        var items = OwnModels();
        if (items.Count == 0)
        {
            // Nothing to convert is a build that ran to its end.
            WriteMarker();
            return;
        }

        lock (_gate)
        {
            _fullBuild = true;
            foreach (var item in items)
                if (_queued.Add(item.Key))
                    _queue.AddLast(item);
            _total += items.Count;
            EnsureWorker();
        }
    }

    public void Prioritise(IReadOnlyList<PreviewBuildItem> items)
    {
        lock (_gate)
        {
            // Only while a build is under way: with none, a request converts what it needs and the
            // cache fills on the miss, which is all that was asked of it.
            if (_worker is null)
                return;

            // Back to front, so the first named ends up first.
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (_queued.Add(item.Key))
                {
                    _total++;
                }
                else
                {
                    var waiting = Find(item.Key);
                    if (waiting is null)
                        continue; // Converted already, or being converted now.
                    _queue.Remove(waiting);
                }

                _queue.AddFirst(item);
            }
        }
    }

    public void FilesChanged(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0 || cacheLocation.Directory is null)
            return;

        var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            if (path.EndsWith(".meg", StringComparison.OrdinalIgnoreCase))
                foreach (var entry in archives.EnumerateEntryPaths(path))
                    Sort(entry);
            else
                Sort(path);

        var current = index.Current;

        // A clip is converted INTO its models, so a changed clip is a change to each model a plain
        // open offers it with. The name narrows the candidates; the scene's own rule decides.
        if (clips.Count > 0)
            foreach (var model in current.AssetFiles.GetByExtension(".alo").Select(Path.GetFileName))
            {
                if (model is null || models.Contains(model))
                    continue;

                var stem = Path.GetFileNameWithoutExtension(model) + "_";
                if (clips.Any(clip => clip.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                    && ModelAnimationClips.For(current, model).Any(clips.Contains))
                    models.Add(model);
            }

        Requeue(
        [
            .. models
                // A particle system shares the extension and is no model; the index knows which.
                .Where(model => !current.ParticleSystems.Contains(model))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(model => new PreviewBuildItem(model, ModelAnimationClips.For(current, model)))
        ]);
        return;

        void Sort(string file)
        {
            var name = Path.GetFileName(file.Replace('\\', '/'));
            if (name.EndsWith(".alo", StringComparison.OrdinalIgnoreCase))
                models.Add(name);
            else if (name.EndsWith(".ala", StringComparison.OrdinalIgnoreCase))
                clips.Add(name);
        }
    }

    /// <summary>Converts these models in the background, behind whatever is waiting.</summary>
    public void Requeue(IReadOnlyList<PreviewBuildItem> items)
    {
        if (items.Count == 0 || cacheLocation.Directory is null)
            return;

        lock (_gate)
        {
            foreach (var item in items)
            {
                if (Find(item.Key) is not null)
                    continue;

                _queued.Add(item.Key);
                _queue.AddLast(item);
                _total++;
            }

            EnsureWorker();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _run?.Cancel();
        }
    }

    /// <summary>Every model under the root project's own asset roots, with the clips a plain open offers.</summary>
    private List<PreviewBuildItem> OwnModels()
    {
        var root = projects.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank)
            .FirstOrDefault();
        if (root is null)
            return [];

        var current = index.Current;
        var fs = fileHelper.FileSystem;
        var items = new List<PreviewBuildItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assetRoot in root.AssetRoots)
        {
            if (!fs.Directory.Exists(assetRoot))
                continue;

            foreach (var file in fs.Directory.EnumerateFiles(assetRoot, "*.alo", SearchOption.AllDirectories)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                var name = fs.Path.GetFileName(file);

                // A particle system shares the extension and is no model; the index knows which.
                if (current.ParticleSystems.Contains(name) || !seen.Add(name))
                    continue;

                var clips = ModelAnimationClips.For(current, name);
                items.Add(new PreviewBuildItem(name, clips));

                // And without them. A unit that attaches this model as a part offers it the
                // UNIT's clips, and where none of those plays on it - a hull's clip reaches past
                // a turret's bones - what it asks for is the model with no clip baked. For a model
                // with no clips of its own that is the entry above.
                if (clips.Count > 0)
                    items.Add(new PreviewBuildItem(name, []));
            }
        }

        return items;
    }

    private LinkedListNode<PreviewBuildItem>? Find(string key)
    {
        for (var node = _queue.First; node is not null; node = node.Next)
            if (node.Value.Key == key)
                return node;

        return null;
    }

    /// <summary>Starts the worker if none is running. Called under the gate.</summary>
    private void EnsureWorker()
    {
        if (_worker is not null || _queue.Count == 0)
            return;

        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StartWorker();
    }

    /// <summary>Starts a worker on the queue as it stands. Called under the gate, with none running.</summary>
    private void StartWorker()
    {
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "Preview cache build",
            // Below the request threads and the indexer: this is work nobody is waiting for.
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    private void Work()
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        IPreviewBuildProgressRun? shown = null;
        var finished = false;
        var fullBuild = false;

        try
        {
            int total;
            lock (_gate)
            {
                total = _total;
                fullBuild = _fullBuild;
                _run = cancelled;
            }

            // The reader's Cancel empties nothing: the worker stops, and what is left is converted
            // on demand or by the next start. Only the build of every model is shown: a file saved
            // to disk is converted again without a notification of its own.
            // ReSharper disable once AccessToDisposedClosure - the run is disposed before the source.
            if (fullBuild)
                shown = progress.Begin(total, () => cancelled.Cancel());

            while (!cancelled.IsCancellationRequested)
            {
                PreviewBuildItem? item;
                lock (_gate)
                {
                    item = _queue.First?.Value;
                    if (item is null)
                    {
                        finished = true;
                        break;
                    }

                    _queue.RemoveFirst();
                }

                Convert(item);

                lock (_gate)
                {
                    _done++;
                    shown?.Report(_done, _total);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The preview cache build stopped on an unexpected failure");
        }
        finally
        {
            shown?.Dispose();

            bool builtInFull;
            bool again;
            TaskCompletionSource? idle = null;
            lock (_gate)
            {
                _run = null;

                again = finished && _queue.Count > 0 && !_stop.IsCancellationRequested;
                if (again)
                {
                    // Queued in the moment between this worker finding the queue empty and its
                    // leaving: whoever queued it saw a worker and started none. Another takes the
                    // queue up, and the session is idle when THAT one is done. A build of every
                    // model this run showed is over all the same; one asked for in that moment is
                    // the next worker's to show and to mark. This worker stays the one on record
                    // until the next is started below, so nobody starts a second in between.
                    builtInFull = fullBuild;
                    if (fullBuild)
                        _fullBuild = false;

                    _queued.Clear();
                    foreach (var waiting in _queue)
                        _queued.Add(waiting.Key);
                    _done = 0;
                    _total = _queue.Count;
                }
                else
                {
                    // Whichever way this run ended, the build of every model is no longer under
                    // way: only one that reached its end leaves the marker, and a cancelled one
                    // must not pass for a build to the worker a later save starts.
                    builtInFull = _fullBuild && finished;
                    _fullBuild = false;

                    // What a cancelled build left behind is not this session's any more.
                    _queue.Clear();
                    _queued.Clear();
                    _done = 0;
                    _total = 0;
                    _worker = null;
                    idle = _idle;
                }
            }

            try
            {
                if (builtInFull)
                    WriteMarker();
            }
            finally
            {
                if (again)
                    lock (_gate)
                    {
                        StartWorker();
                    }
                else
                    idle?.TrySetResult();
            }
        }
    }

    private void Convert(PreviewBuildItem item)
    {
        var reference = PreviewModelReference.Normalise(item.ModelReference);
        try
        {
            cache.GetOrConvert(
                PreviewModelReference.ModelPath(reference),
                [.. item.Animations.Select(name => (name, PreviewModelReference.ModelPath(name)))],
                converter.Convert, converter.Judge);
        }
        catch (AloFormatException e)
        {
            // Not a model the reader can open either; the request for it says why.
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Preview cache build skipped {Model}: {Reason}", reference, e.Message);
        }
        catch (Exception e)
        {
            // One model that will not convert must not cost the rest their place in the cache.
            logger.LogWarning(e, "Preview cache build could not convert {Model}", reference);
        }
    }

    private bool MarkerStands(string directory)
    {
        var fs = fileHelper.FileSystem;
        var marker = directory + "/" + MarkerFileName;
        try
        {
            return fs.File.Exists(marker)
                   && fs.File.ReadAllText(marker).Trim() == MarkerContent();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteMarker()
    {
        var directory = cacheLocation.Directory;
        if (directory is null)
            return;

        try
        {
            AtomicFile.WriteAllText(fileHelper.FileSystem, directory + "/" + MarkerFileName, MarkerContent() + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Without it the next start walks the models again and finds them all converted.
            logger.LogWarning(e, "Could not mark the preview cache at '{Path}' as built", directory);
        }
    }

    /// <summary>A build under another converter version is not this build.</summary>
    private static string MarkerContent()
    {
        return "converter " + PreviewModelCache.ConverterVersion;
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}
