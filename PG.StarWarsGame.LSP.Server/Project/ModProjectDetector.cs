// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Project;

/// <summary>
///     Finds the project file to serve when the client named none. One server runs per open
///     project, so a client that knows its project passes <c>projectPath</c> and never comes
///     here; this is the fallback for a bare root - a JetBrains host sends one folder, the
///     command line sends one directory.
/// </summary>
/// <remarks>
///     Several project files under one root used to be refused as ambiguous. They are an
///     ordinary layout now (a workspace holding a mod and the library it extends), so the
///     shallowest is served and the others are reported, never refused: the shallowest is the one
///     the folder is "about", and a tie is broken by name so two starts agree.
/// </remarks>
public sealed class ModProjectDetector : IModProjectDetector
{
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<ModProjectDetector> _logger;

    public ModProjectDetector(IFileHelper fileHelper, ILogger<ModProjectDetector> logger)
    {
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public bool TryFind(IEnumerable<string> workspaceRoots, out string? projectFilePath)
    {
        return TryFind(workspaceRoots, out projectFilePath, out _);
    }

    public bool TryFind(IEnumerable<string> workspaceRoots, out string? projectFilePath,
        out IReadOnlyList<string> otherProjectFiles)
    {
        foreach (var root in workspaceRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !_fileHelper.FileSystem.Directory.Exists(root))
                continue;

            var matches = _fileHelper.FileSystem.Directory
                .GetFiles(root, "*.pgproj", SearchOption.AllDirectories)
                .OrderBy(Depth)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (matches.Length == 0) continue;

            projectFilePath = matches[0];
            otherProjectFiles = matches.Skip(1).ToArray();
            _logger.LogInformation("Detected mod project file '{Path}'.", projectFilePath);
            if (otherProjectFiles.Count > 0)
                _logger.LogInformation("{Count} other project file(s) under '{Root}': {Others}",
                    otherProjectFiles.Count, root, string.Join(", ", otherProjectFiles));
            return true;
        }

        projectFilePath = null;
        otherProjectFiles = [];
        return false;
    }

    private static int Depth(string path)
    {
        return path.Count(c => c is '/' or '\\');
    }
}