// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>Per-workspace editor preferences (not game data). Extend with new fields as needed.</summary>
public sealed record WorkspaceSettings
{
    /// <summary>Skip the "delete story event?" confirmation modal (user ticked "don't ask again").</summary>
    public bool SkipStoryDeleteConfirmation { get; init; }

    /// <summary>Show the per-thread swimlane overlay in the story graph.</summary>
    public bool ShowThreadLanes { get; init; }

    /// <summary>Show the per-chapter swimlane overlay in the story graph.</summary>
    public bool ShowChapterLanes { get; init; }
}

/// <summary>Reads/writes the workspace's editor preferences.</summary>
public interface IWorkspaceSettingsStore
{
    WorkspaceSettings Get();
    void Set(WorkspaceSettings settings);
}

/// <summary>
///     JSON sidecar under the project's <c>.aetswg/settings/workspace.settings.json</c>
///     (<see cref="ProjectIndexLocator" /> conventions) - editor preferences that belong with the
///     workspace, not the mod's xml tree. Without a .pgproj the store degrades to in-memory (the
///     preference survives the session only). A corrupt file is treated as defaults, never fatal.
/// </summary>
public sealed class WorkspaceSettingsStore(
    IModProjectReloadService reloadService,
    IFileHelper fileHelper,
    ILogger<WorkspaceSettingsStore> logger) : IWorkspaceSettingsStore
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private WorkspaceSettings? _cache;

    public WorkspaceSettings Get()
    {
        lock (_gate)
        {
            return LoadLocked();
        }
    }

    public void Set(WorkspaceSettings settings)
    {
        lock (_gate)
        {
            _cache = settings;
            SaveLocked(settings);
        }
    }

    private WorkspaceSettings LoadLocked()
    {
        if (_cache is not null) return _cache;

        _cache = new WorkspaceSettings();
        var path = SidecarPath();
        if (path is null) return _cache;

        try
        {
            var fs = fileHelper.FileSystem;
            if (fs.File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<WorkspaceSettings>(fs.File.ReadAllText(path), s_json);
                if (loaded is not null) _cache = loaded;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "workspace.settings.json unreadable - using defaults");
        }

        return _cache;
    }

    private void SaveLocked(WorkspaceSettings settings)
    {
        var path = SidecarPath();
        if (path is null) return;

        try
        {
            var fs = fileHelper.FileSystem;
            var dir = path[..path.LastIndexOf('/')];
            fs.Directory.CreateDirectory(dir);
            fs.File.WriteAllText(path, JsonSerializer.Serialize(settings, s_json));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist workspace.settings.json - the preference stays in-memory");
        }
    }

    private string? SidecarPath()
    {
        var rootLayer = reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank)
            .FirstOrDefault();
        if (rootLayer?.ProjectPath is not { } pgprojPath) return null;
        return ProjectIndexLocator.GetAetswgDirectory(pgprojPath) + "/settings/workspace.settings.json";
    }
}