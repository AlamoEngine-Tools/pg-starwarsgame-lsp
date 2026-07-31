// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Suppression;

/// <summary>One persisted project-wide suppression.</summary>
public sealed class SuppressionEntry
{
    /// <summary>Wire form of a <see cref="SuppressionMatcher" />: an id or <c>aetswg-000-*</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Free text explaining the decision. Never read back - it is there for the next reader.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

/// <summary>
///     JSON sidecar at <c>.aetswg/suppressions.json</c>, following the same conventions as
///     <see cref="Story.StoryLayoutStore" />: cached in memory, written through on every change, and
///     degrading to in-memory when there is no <c>.pgproj</c> to anchor it.
///     <para>
///         Unlike the layout sidecar this one is intended for version control - suppressing a
///         diagnostic is a decision about the mod, not editor state - which is why
///         <c>.aetswg/.gitignore</c> excludes only <c>indices/</c>.
///     </para>
/// </summary>
public sealed class GlobalSuppressionStore : IGlobalSuppressionStore
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IFileHelper _fileHelper;
    private readonly object _gate = new();
    private readonly ILogger<GlobalSuppressionStore> _logger;
    private readonly IUserNotifier? _notifier;
    private readonly IModProjectReloadService _reloadService;
    private List<SuppressionEntry>? _cache;

    public GlobalSuppressionStore(
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<GlobalSuppressionStore> logger,
        IUserNotifier? notifier = null)
    {
        _reloadService = reloadService;
        _fileHelper = fileHelper;
        _logger = logger;
        _notifier = notifier;
    }

    public IReadOnlyList<SuppressionMatcher> GetAll()
    {
        lock (_gate)
        {
            var result = new List<SuppressionMatcher>();
            foreach (var entry in LoadLocked())
                // A malformed entry is skipped, not fatal: it must leave the diagnostic visible
                // rather than take the whole suppression file down with it. It is reported when the
                // file is read, not here - this runs on every publish.
                if (SuppressionMatcher.TryParse(entry.Id, out var matcher))
                    result.Add(matcher);

            return result;
        }
    }

    public void Add(SuppressionMatcher matcher, string? reason = null)
    {
        lock (_gate)
        {
            var entries = LoadLocked();
            var wire = matcher.ToString();
            if (entries.Any(e => string.Equals(e.Id, wire, StringComparison.OrdinalIgnoreCase))) return;

            entries.Add(new SuppressionEntry { Id = wire, Reason = reason });
            SaveLocked(entries);
        }
    }

    public void Remove(SuppressionMatcher matcher)
    {
        lock (_gate)
        {
            var entries = LoadLocked();
            var wire = matcher.ToString();
            if (entries.RemoveAll(e => string.Equals(e.Id, wire, StringComparison.OrdinalIgnoreCase)) == 0)
                return;

            SaveLocked(entries);
        }
    }

    private List<SuppressionEntry> LoadLocked()
    {
        if (_cache is not null) return _cache;

        _cache = [];
        var path = SidecarPath();
        if (path is null) return _cache;

        try
        {
            var fs = _fileHelper.FileSystem;
            if (fs.File.Exists(path))
                _cache = JsonSerializer.Deserialize<List<SuppressionEntry>>(
                    fs.File.ReadAllText(path), s_json) ?? [];
        }
        catch (Exception ex)
        {
            // A corrupt file must not disable diagnostics wholesale; starting empty means
            // everything is reported, which is the safe direction to fail in.
            _logger.LogWarning(ex, "suppressions.json unreadable - no global suppressions applied");
            Report("suppressions.json could not be read, so no project-wide suppressions are "
                   + $"applied. Fix or delete '{path}'. ({ex.Message})");
            return _cache;
        }

        ReportUnreadableEntries(path);
        return _cache;
    }

    /// <summary>
    ///     Tells the user about entries that will silence nothing. Done once, when the file is
    ///     read, because the alternative is a balloon on every publish - and left to the log alone,
    ///     a typo here is invisible: the diagnostic simply stays on screen with no explanation.
    /// </summary>
    private void ReportUnreadableEntries(string path)
    {
        var bad = _cache!
            .Where(e => !SuppressionMatcher.TryParse(e.Id, out _))
            .Select(e => $"'{e.Id}'")
            .ToList();

        if (bad.Count == 0) return;

        foreach (var id in bad)
            _logger.LogWarning("Ignoring unreadable suppression id {Id}", id);

        Report($"suppressions.json has {(bad.Count == 1 ? "an entry that is not" : "entries that are not")} "
               + $"a diagnostic id: {string.Join(", ", bad)}. "
               + $"{(bad.Count == 1 ? "It suppresses" : "They suppress")} nothing. "
               + $"Expected aetswg-<group>-<number> or aetswg-<group>-*, in '{path}'.");
    }

    private void Report(string message)
    {
        _notifier?.ShowError(message);
    }

    private void SaveLocked(List<SuppressionEntry> entries)
    {
        var path = SidecarPath();
        if (path is null) return;

        try
        {
            var fs = _fileHelper.FileSystem;
            fs.Directory.CreateDirectory(path[..path.LastIndexOf('/')]);
            fs.File.WriteAllText(path, JsonSerializer.Serialize(entries, s_json));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist suppressions.json - change stays in-memory");
        }
    }

    private string? SidecarPath()
    {
        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank)
            .FirstOrDefault();
        if (rootLayer?.ProjectPath is not { } pgprojPath) return null;
        return ProjectIndexLocator.GetAetswgDirectory(pgprojPath) + "/suppressions.json";
    }
}
