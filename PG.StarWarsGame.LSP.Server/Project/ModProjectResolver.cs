// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Project;

public sealed class ModProjectResolver
{
    private readonly ProjectDependencyGraph _graph;
    private readonly ModProjectLoader _loader;
    private readonly ILogger<ModProjectResolver> _logger;
    private readonly IMessageCatalog _messages;

    public ModProjectResolver(
        IFileHelper fileHelper,
        ModProjectLoader loader,
        ProjectDependencyGraph graph,
        ILogger<ModProjectResolver> logger,
        IMessageCatalog messages)
    {
        FileHelper = fileHelper;
        _loader = loader;
        _graph = graph;
        _logger = logger;
        _messages = messages;
    }

    /// <summary>The file system this resolver reads project files through; shared with the configuration resolver.</summary>
    public IFileHelper FileHelper { get; }

    public WorkspaceConfiguration Resolve(string rootPath, ModProjectFile root)
    {
        var ordered = _graph.Build(rootPath, root, LoadReference, out var dependencies);
        ThrowIfGamesConflict(rootPath, root, ordered);

        var xml = new List<string>();
        var scripts = new List<string>();
        var text = new List<string>();
        var assets = new List<string>();
        var storyDialog = new List<string>();
        string? textResourceType = null;
        var layers = new List<ProjectLayer>(ordered.Count);

        // ordered runs dependencies-first, root last, so the index is the precedence rank
        // (dependencies low, root project highest).
        for (var rank = 0; rank < ordered.Count; rank++)
        {
            var (written, file) = ordered[rank];
            var path = OnDisk(written);
            var projectDir = GetDirectory(path);

            var layerXml = file.Directories.Xml.Select(d => Combine(projectDir, d)).ToList();
            var layerScripts = file.Directories.Scripts.Select(d => Combine(projectDir, d)).ToList();
            var layerText = file.Localisation is { } loc
                ? new List<string> { Combine(projectDir, loc.Directory) }
                : [];
            var layerAssets = file.Directories.Art.Select(d => Combine(projectDir, d))
                .Concat(file.Directories.Audio.Select(d => Combine(projectDir, d))).ToList();
            var layerStoryDialog = file.Directories.StoryDialog.Select(d => Combine(projectDir, d)).ToList();

            xml.AddRange(layerXml);
            scripts.AddRange(layerScripts);
            text.AddRange(layerText);
            assets.AddRange(layerAssets);
            storyDialog.AddRange(layerStoryDialog);

            // Root project is last in the ordered list so its value overwrites dependencies.
            if (file.Localisation is not null)
                textResourceType = file.Localisation.Type;

            // Per-layer TextResourceType is preserved so each project's localisation loads with its
            // own format (a dependency's .csv must not be skipped because the root uses another type).
            layers.Add(new ProjectLayer(rank, file.Name, layerXml, layerScripts, layerText, layerAssets,
                file.Localisation?.Type, path)
            {
                StoryDialogRoots = layerStoryDialog,
                Credits = file.Localisation?.Credits,
                Icons = file.Icons
            });
        }

        return new WorkspaceConfiguration(xml, scripts, text, assets, textResourceType)
        {
            Layers = layers,
            StoryDialogRoots = storyDialog,
            // Root project only - a mega texture replaces rather than merges, so a dependency's
            // icon configuration has no say in which one the root project ships.
            Icons = root.Icons,
            Dependencies = dependencies,
            Game = root.Game
        };
    }

    /// <summary>
    ///     Refuses an Empire at War project built on a Forces of Corruption one. The expansion reads
    ///     the base game's data beneath its own, so the other direction is an ordinary layering; the
    ///     base game cannot load the expansion's data, so this one describes a game that does not exist.
    /// </summary>
    private void ThrowIfGamesConflict(string rootPath, ModProjectFile root,
        IReadOnlyList<(string Path, ModProjectFile File)> ordered)
    {
        if (root.Game != TargetGame.EAW) return;

        var expansion = ordered.FirstOrDefault(p => p.File.Game == TargetGame.FOC);
        if (expansion.File is null) return;

        throw new ModProjectLoadException(_messages.Format("ModProject_EawProjectReferencesFocProject",
            FileHelper.FileSystem.Path.GetFileName(rootPath), expansion.File.Name));
    }

    private ModProjectFile? LoadReference(string absolutePath)
    {
        try
        {
            return _loader.Load(absolutePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load referenced project '{Path}'.", absolutePath);
            return null;
        }
    }

    private static string GetDirectory(string normalizedPath)
    {
        return ProjectPathResolution.GetDirectory(normalizedPath);
    }

    /// <summary>
    ///     Resolves a project directory, spelled as the disk spells it.
    /// </summary>
    /// <remarks>
    ///     Never folded: every URI under the directory is built from it, and a folded one names a
    ///     file the host does not have. Case folds where paths are compared, in
    ///     <see cref="DocumentUris" />.
    /// </remarks>
    private string Combine(string directory, string relative)
    {
        var candidate = GamePaths.Forward(relative);
        return OnDisk(ProjectPathResolution.Resolve(directory, candidate));
    }

    /// <summary>
    ///     A forward-slashed absolute path with each segment spelled as the disk spells it, up to the
    ///     first segment that does not exist; from there on, as given (#291).
    /// </summary>
    /// <remarks>
    ///     A project file may spell a folder in any case - the vanilla ones say <c>data/xml</c> of a
    ///     folder the game ships as <c>Data/XML</c> - and the game does not mind. The host does: it
    ///     knows a file by the disk's name, so a URI under another spelling is a second document.
    /// </remarks>
    private string OnDisk(string path)
    {
        var fs = FileHelper.FileSystem;
        var parts = path.Split('/');
        var spelled = new List<string>(parts.Length) { parts[0] };
        for (var i = 1; i < parts.Length; i++)
        {
            var parent = spelled.Count == 1 ? spelled[0] + "/" : string.Join('/', spelled);
            var match = parts[i].Length == 0 || !fs.Directory.Exists(parent)
                ? null
                : fs.Directory.EnumerateFileSystemEntries(parent)
                    .Select(fs.Path.GetFileName)
                    .FirstOrDefault(name => string.Equals(name, parts[i], StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                spelled.AddRange(parts[i..]);
                break;
            }

            spelled.Add(match);
        }

        return string.Join('/', spelled);
    }
}