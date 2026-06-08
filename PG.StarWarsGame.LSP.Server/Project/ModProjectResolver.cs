// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Project;

public sealed class ModProjectResolver
{
    private readonly IFileHelper _fileHelper;
    private readonly ProjectDependencyGraph _graph;
    private readonly ModProjectLoader _loader;
    private readonly ILogger<ModProjectResolver> _logger;

    public ModProjectResolver(
        IFileHelper fileHelper,
        ModProjectLoader loader,
        ProjectDependencyGraph graph,
        ILogger<ModProjectResolver> logger)
    {
        _fileHelper = fileHelper;
        _loader = loader;
        _graph = graph;
        _logger = logger;
    }

    public WorkspaceConfiguration Resolve(string rootPath, ModProjectFile root)
    {
        var ordered = _graph.Build(rootPath, root, LoadReference);

        var xml = new List<string>();
        var scripts = new List<string>();
        var text = new List<string>();
        var assets = new List<string>();
        string? textResourceType = null;

        foreach (var (path, file) in ordered)
        {
            var projectDir = GetDirectory(path);

            xml.AddRange(file.Directories.Xml.Select(d => Combine(projectDir, d)));
            scripts.AddRange(file.Directories.Scripts.Select(d => Combine(projectDir, d)));
            text.AddRange(file.Directories.Text.Select(d => Combine(projectDir, d)));
            assets.AddRange(file.Directories.Art.Select(d => Combine(projectDir, d)));
            assets.AddRange(file.Directories.Audio.Select(d => Combine(projectDir, d)));

            // Root project is last in the ordered list so its value overwrites dependencies.
            if (file.Directories.TextResourceType is not null)
                textResourceType = file.Directories.TextResourceType;
        }

        return new WorkspaceConfiguration(xml, scripts, text, assets, textResourceType);
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
        var idx = normalizedPath.LastIndexOf('/');
        return idx < 0 ? string.Empty : normalizedPath[..idx];
    }

    private static string Combine(string directory, string relative)
    {
        var candidate = relative.Replace('\\', '/');
        var basePath = string.IsNullOrEmpty(directory) ? "." : directory;
        var segments = new List<string>(basePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var basePrefix = basePath.StartsWith('/') ? "/" : string.Empty;

        foreach (var segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    if (segments.Count > 0)
                        segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    segments.Add(segment);
                    break;
            }

        return (basePrefix + string.Join('/', segments)).ToLowerInvariant();
    }
}