// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.Commons;
using PG.StarWarsGame.Files.ALO;
using PG.StarWarsGame.Files.ALO.Services;

namespace PG.StarWarsGame.LSP.Assets.Projection;

/// <summary>
///     Extracts the animation-bone names exposed by each loose <c>.alo</c> model file under a game
///     repository root. The result is keyed by the model's path normalised relative to that root
///     (lowercase, forward-slash). Used by the BaselineBuilder to populate
///     <see cref="Core.Symbols.BaselineIndex.ModelBones" /> and by the workspace scanner for mod models.
/// </summary>
/// <remarks>
///     Bone names are read via the engine's <see cref="IAloFileService" /> ALO model loader
///     (<c>AlamoModel.Bones</c>). Any model that fails to load (corrupt, unsupported version, or not a
///     model ALO) is skipped silently - extraction never throws for a single bad file.
/// </remarks>
public static class BoneNameExtractor
{
    /// <summary>
    ///     Enumerates loose <c>.alo</c> files under <paramref name="rootPath" /> and reads their bones via
    ///     the engine's ALO model loader. Returns an empty map when the root is missing or no model loads.
    /// </summary>
    public static Dictionary<string, string[]> Extract(IFileSystem fileSystem, string rootPath)
    {
        return Extract(fileSystem, rootPath, CreateDefaultBoneLoader(fileSystem));
    }

    /// <summary>
    ///     Testable core: <paramref name="boneLoader" /> receives the absolute path of each <c>.alo</c>
    ///     file and returns its bone names (or <c>null</c>/empty to omit it). Exceptions thrown by the
    ///     loader for an individual file are swallowed so one bad model never aborts the whole scan.
    /// </summary>
    public static Dictionary<string, string[]> Extract(
        IFileSystem fileSystem, string rootPath, Func<string, IList<string>?> boneLoader)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(rootPath) || !fileSystem.Directory.Exists(rootPath))
            return result;

        var root = fileSystem.Path.GetFullPath(rootPath);

        foreach (var file in fileSystem.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!fileSystem.Path.GetExtension(file).Equals(".alo", StringComparison.OrdinalIgnoreCase))
                continue;

            IList<string>? bones;
            try
            {
                bones = boneLoader(file);
            }
            catch
            {
                // Corrupt, unsupported, or non-model ALO - skip without failing the whole scan.
                continue;
            }

            if (bones is null || bones.Count == 0)
                continue;

            var relative = fileSystem.Path.GetRelativePath(root, file);
            result[Normalize(relative)] = bones.ToArray();
        }

        return result;
    }

    private static Func<string, IList<string>?> CreateDefaultBoneLoader(IFileSystem fileSystem)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fileSystem);
        PetroglyphCommons.ContributeServices(services);
        services.SupportALO();
        var provider = services.BuildServiceProvider();
        var aloService = provider.GetRequiredService<IAloFileService>();

        return file =>
        {
            using var stream = fileSystem.File.OpenRead(file);
            using var model = aloService.LoadModel(stream);
            return model.Content.Bones;
        };
    }

    private static string Normalize(string relativePath)
    {
        return relativePath.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
    }
}