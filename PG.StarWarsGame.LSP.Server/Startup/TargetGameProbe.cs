// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The game the workspace's project file names, read before the project loads.
/// </summary>
public interface ITargetGameProbe
{
    /// <summary>
    ///     The game the project under <paramref name="roots" /> names; FoC when there is no project or
    ///     it names none that can be read.
    /// </summary>
    TargetGame Probe(IReadOnlyList<string> roots);
}

/// <summary>
///     Reads the <c>game</c> field and nothing else, and never reports a problem: the baseline download
///     starts beside the schema's on this answer, and the project load that follows reports every
///     problem with the file and asks for the baseline of the game it actually resolved.
/// </summary>
public sealed class TargetGameProbe(
    IModProjectDetector detector,
    ILspConfigurationProvider config,
    IFileHelper fileHelper,
    ILogger<TargetGameProbe> logger) : ITargetGameProbe
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public TargetGame Probe(IReadOnlyList<string> roots)
    {
        try
        {
            var path = config.Current.ProjectPath;
            if (string.IsNullOrWhiteSpace(path) || !fileHelper.FileSystem.File.Exists(path))
                if (!detector.TryFind(roots, out path) || path is null)
                    return TargetGame.FOC;

            var root = JsonNode.Parse(fileHelper.FileSystem.File.ReadAllText(path), documentOptions: DocumentOptions);
            var value = root is JsonObject project && project["game"] is JsonValue game
                                                   && game.TryGetValue<string>(out var text)
                ? text
                : null;
            return TargetGameNames.TryParse(value, out var named) ? named : TargetGame.FOC;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(ex, "Could not read the project's game ahead of loading it; assuming FoC");
            return TargetGame.FOC;
        }
    }
}