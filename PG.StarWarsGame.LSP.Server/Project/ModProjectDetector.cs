// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Project;

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
        foreach (var root in workspaceRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !_fileHelper.FileSystem.Directory.Exists(root))
                continue;

            var matches = _fileHelper.FileSystem.Directory
                .GetFiles(root, "*.pgproj", SearchOption.AllDirectories);
            if (matches.Length == 0) continue;

            if (matches.Length > 1)
                throw new ModProjectLoadException(
                    $"Found multiple .pgproj files under '{root}': {string.Join(", ", matches)}. " +
                    "Only one .pgproj is supported per workspace - remove or relocate the extras, " +
                    "or open the specific directory that contains the one you want to use.");

            projectFilePath = matches[0];
            _logger.LogInformation("Detected mod project file '{Path}'.", projectFilePath);
            return true;
        }

        projectFilePath = null;
        return false;
    }
}