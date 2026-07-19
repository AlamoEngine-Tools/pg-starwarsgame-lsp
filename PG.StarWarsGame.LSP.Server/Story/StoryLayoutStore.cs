// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>One saved node position, keyed by thread file name + event name.</summary>
public sealed record StoryLayoutEntry(string File, string EventName, double X, double Y);

/// <summary>Per-campaign story graph layout persistence.</summary>
public interface IStoryLayoutStore
{
    IReadOnlyList<StoryLayoutEntry> Get(string campaign);

    /// <summary>Upserts by (file, eventName); entries not mentioned keep their stored position.</summary>
    void Set(string campaign, IReadOnlyList<StoryLayoutEntry> entries);
}

/// <summary>
///     JSON sidecar under the project's <c>.aetswg/</c> directory
///     (<c>story-layout.json</c>, <see cref="ProjectIndexLocator" /> conventions). Layout is
///     editor state, not game data - it deliberately lives next to the index caches, not in the
///     mod's xml tree. Without a .pgproj the store degrades to in-memory (positions survive the
///     session only). Orphaned entries (deleted events) are harmless and left in place.
/// </summary>
public sealed class StoryLayoutStore(
    IModProjectReloadService reloadService,
    IFileHelper fileHelper,
    ILogger<StoryLayoutStore> logger) : IStoryLayoutStore
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private Dictionary<string, List<StoryLayoutEntry>>? _cache;

    public IReadOnlyList<StoryLayoutEntry> Get(string campaign)
    {
        lock (_gate)
        {
            var data = LoadLocked();
            return data.TryGetValue(campaign, out var entries) ? entries.ToList() : [];
        }
    }

    public void Set(string campaign, IReadOnlyList<StoryLayoutEntry> entries)
    {
        lock (_gate)
        {
            var data = LoadLocked();
            if (!data.TryGetValue(campaign, out var existing))
                data[campaign] = existing = [];

            foreach (var entry in entries)
            {
                var index = existing.FindIndex(e =>
                    string.Equals(e.File, entry.File, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.EventName, entry.EventName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) existing[index] = entry;
                else existing.Add(entry);
            }

            SaveLocked(data);
        }
    }

    private Dictionary<string, List<StoryLayoutEntry>> LoadLocked()
    {
        if (_cache is not null) return _cache;

        _cache = new Dictionary<string, List<StoryLayoutEntry>>(StringComparer.OrdinalIgnoreCase);
        var path = SidecarPath();
        if (path is null) return _cache;

        try
        {
            var fs = fileHelper.FileSystem;
            if (fs.File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<StoryLayoutEntry>>>(
                    fs.File.ReadAllText(path), s_json);
                if (loaded is not null)
                    _cache = new Dictionary<string, List<StoryLayoutEntry>>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            // A corrupt sidecar must never break the editor - start over.
            logger.LogWarning(ex, "story-layout.json unreadable - starting with an empty layout");
        }

        return _cache;
    }

    private void SaveLocked(Dictionary<string, List<StoryLayoutEntry>> data)
    {
        var path = SidecarPath();
        if (path is null) return;

        try
        {
            var fs = fileHelper.FileSystem;
            var dir = path[..path.LastIndexOf('/')];
            fs.Directory.CreateDirectory(dir);
            fs.File.WriteAllText(path, JsonSerializer.Serialize(data, s_json));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist story-layout.json - positions stay in-memory");
        }
    }

    private string? SidecarPath()
    {
        var rootLayer = reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank)
            .FirstOrDefault();
        if (rootLayer?.ProjectPath is not { } pgprojPath) return null;
        return ProjectIndexLocator.GetAetswgDirectory(pgprojPath) + "/story-layout.json";
    }
}