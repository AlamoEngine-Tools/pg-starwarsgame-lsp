// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Story.Dialog;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     <see cref="IStoryDialogScope" /> over the resolved workspace configuration: the pgproj
///     <c>directories.storyDialog</c> roots define which .txt files are story-dialog scripts,
///     name resolution follows layer precedence (root project beats dependencies), and chapter
///     sets are parsed on demand with a last-write-time cache.
/// </summary>
public sealed class StoryDialogScopeService : IStoryDialogScope
{
    private readonly Dictionary<string, (DateTime WriteTimeUtc, IReadOnlyCollection<int> Chapters)> _chapterCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILspConfigurationProvider _configProvider;
    private readonly IFileHelper _fileHelper;

    private readonly object _gate = new();
    private readonly IModProjectReloadService _reloadService;

    public StoryDialogScopeService(
        IModProjectReloadService reloadService,
        ILspConfigurationProvider configProvider,
        IFileHelper fileHelper)
    {
        _reloadService = reloadService;
        _configProvider = configProvider;
        _fileHelper = fileHelper;
    }

    private IReadOnlyList<string> Roots => _reloadService.LastWorkspaceConfig?.StoryDialogRoots ?? [];

    public bool Enabled =>
        _configProvider.Current.Features.Dialog.Diagnostics && Roots.Count > 0;

    public bool IsInScope(string canonicalUri)
    {
        foreach (var root in Roots)
            if (canonicalUri.StartsWith(_fileHelper.NormalizeUri(root) + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public string? ResolveDialogFile(string dialogName)
    {
        var fileName = dialogName + ".txt";
        // Roots are dependencies-first / root-project-last; the highest layer's copy wins.
        for (var i = Roots.Count - 1; i >= 0; i--)
        {
            var path = _fileHelper.FindInWorkspace([Roots[i]], fileName);
            if (path is not null)
                return _fileHelper.NormalizeUri(path);
        }

        return null;
    }

    public IReadOnlyCollection<int> GetChapters(string canonicalUri)
    {
        var path = _fileHelper.FileUriToPath(canonicalUri);
        if (path is null || !_fileHelper.FileSystem.File.Exists(path)) return [];

        var writeTime = _fileHelper.FileSystem.File.GetLastWriteTimeUtc(path);
        lock (_gate)
        {
            if (_chapterCache.TryGetValue(canonicalUri, out var cached) && cached.WriteTimeUtc == writeTime)
                return cached.Chapters;
        }

        var document = StoryDialogParser.Parse(_fileHelper.FileSystem.File.ReadAllText(path));
        var chapters = document.Chapters
            .Where(c => c.Index != StoryDialogChapter.AnonymousIndex)
            .Select(c => c.Index)
            .ToHashSet();

        lock (_gate)
        {
            _chapterCache[canonicalUri] = (writeTime, chapters);
        }

        return chapters;
    }
}