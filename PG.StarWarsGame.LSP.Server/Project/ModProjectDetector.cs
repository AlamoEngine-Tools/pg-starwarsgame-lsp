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

    public IReadOnlyList<string> FindAll(IEnumerable<string> workspaceRoots)
    {
        var found = new List<string>();

        // Roots overlap routinely: ComputeScanRoots appends the configured game-data directory,
        // which is usually a subdirectory of a workspace folder. Dedupe on the normalised project
        // path so one project discovered through several roots stays one project.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in workspaceRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !_fileHelper.FileSystem.Directory.Exists(root))
                continue;

            var matches = _fileHelper.FileSystem.Directory
                .GetFiles(root, "*.pgproj", SearchOption.AllDirectories);
            if (matches.Length == 0) continue;

            // One folder is one project. Two mods are expressed as two workspace folders, so this
            // stays a hard error rather than silently guessing which project the folder means.
            if (matches.Length > 1)
                throw new ModProjectLoadException(
                    $"Found multiple .pgproj files under '{root}': {string.Join(", ", matches)}. " +
                    "Only one .pgproj is supported per workspace folder - remove or relocate the " +
                    "extras, or add each project as its own folder to a multi-root workspace.");

            var match = matches[0];
            if (!seen.Add(_fileHelper.NormalizeUri(_fileHelper.PathToFileUri(match))))
                continue;

            _logger.LogInformation("Detected mod project file '{Path}'.", match);
            found.Add(match);
        }

        return found;
    }
}
