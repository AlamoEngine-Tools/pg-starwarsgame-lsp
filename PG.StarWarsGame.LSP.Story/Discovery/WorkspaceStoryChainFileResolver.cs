// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Story.Discovery;

/// <summary>
///     Resolves story-chain files against the workspace's declared XML directories. Reads follow
///     the layer override rule: <paramref name="xmlRoots" /> arrives dependencies-first /
///     root-project-last (see <c>ModProjectResolver.Resolve</c>), so roots are searched in reverse
///     and the highest-rank copy wins. Files no layer ships fall back to the shipped baseline's
///     file-type map (registration without content).
/// </summary>
public sealed class WorkspaceStoryChainFileResolver(
    IFileHelper fileHelper,
    IReadOnlyList<string> xmlRoots,
    BaselineIndex baseline) : IStoryChainFileResolver
{
    private readonly IReadOnlyList<string> _rootsHighestRankFirst = xmlRoots.Reverse().ToList();

    public StoryChainFile? ReadFile(string xmlRelativePath)
    {
        foreach (var root in _rootsHighestRankFirst)
        {
            // Case-insensitive per-part lookup - the engine ignores casing, the host OS may not.
            var path = fileHelper.FindInWorkspace([root], xmlRelativePath);
            if (path is null) continue;

            try
            {
                var content = fileHelper.FileSystem.File.ReadAllText(path);
                return new StoryChainFile(content, fileHelper.NormalizeUri(path));
            }
            catch (IOException)
            {
                // Unreadable copy: keep searching lower-rank layers instead of failing the chain.
            }
        }

        return null;
    }

    public bool IsKnownToBaseline(string xmlRelativePath)
    {
        // Baseline keys are normalized game paths as listed in the shipped registry files —
        // either xml-dir-relative or with the full data/xml prefix.
        var normalized = xmlRelativePath.Replace('\\', '/').ToLowerInvariant();
        return baseline.FileTypeMap.ContainsKey(normalized)
               || baseline.FileTypeMap.ContainsKey("data/xml/" + normalized);
    }
}