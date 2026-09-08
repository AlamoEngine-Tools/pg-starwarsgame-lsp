// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.Commons;
using PG.StarWarsGame.Files.ALO;
using PG.StarWarsGame.Files.ALO.Services;
using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Checks this reader's bone list against the vendored <c>IAloFileService</c> loader's, model by
///     model.
/// </summary>
/// <remarks>
///     <para>
///         These two parsers now sit side by side and their results get UNIONED:
///         <c>BoneNameExtractor</c> takes skeleton bones from the vendored loader and mesh names from
///         <see cref="AloModelReader" />, because the engine resolves a bone reference against either.
///         If the two disagree about the skeleton itself, that union is incoherent - some bone
///         references would validate and others would not, for no reason an author could see.
///     </para>
///     <para>
///         So this is not a duplicate of the other sweeps. They ask "does it parse"; this asks "do the
///         two independent implementations agree", which is the only check that would catch a
///         systematic offset or an off-by-one in either.
///     </para>
/// </remarks>
public sealed class AloModelReaderVendoredLoaderAgreementTest
{
    private const string OptInVariable = "AET_ALO_CORPUS";

    [Fact]
    public void Read_AgreesWithTheVendoredLoaderOnEveryShippedSkeleton()
    {
        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        var fileSystem = new FileSystem();
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fileSystem);
        PetroglyphCommons.ContributeServices(services);
        services.SupportALO();
        var aloService = services.BuildServiceProvider().GetRequiredService<IAloFileService>();

        var disagreements = new List<string>();
        var compared = 0;

        foreach (var file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.alo")))
        {
            var bytes = File.ReadAllBytes(file);

            // Particle systems share the extension but have no skeleton.
            if (bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) != 0x200)
                continue;

            IList<string> vendored;
            try
            {
                // The vendored loader derives the model's directory from the stream's path and throws
                // on a pathless one, so it gets a file-backed stream rather than a MemoryStream.
                using var stream = fileSystem.File.OpenRead(file);
                using var model = aloService.LoadModel(stream);
                vendored = model.Content.Bones;
            }
            catch
            {
                // A model the vendored loader cannot read says nothing about this one.
                continue;
            }

            var ours = AloModelReader.Read(bytes, AloReadOptions.SkipGeometry)
                .Bones.Select(b => b.Name).ToList();
            compared++;

            if (ours.Count != vendored.Count)
            {
                disagreements.Add(
                    $"{Path.GetFileName(file)}: {ours.Count} bones vs vendored {vendored.Count}");
                continue;
            }

            for (var i = 0; i < ours.Count; i++)
                if (!string.Equals(ours[i], vendored[i], StringComparison.OrdinalIgnoreCase))
                {
                    disagreements.Add(
                        $"{Path.GetFileName(file)} [{i}]: '{ours[i]}' vs vendored '{vendored[i]}'");
                    break;
                }
        }

        Assert.True(compared > 2000, $"Only {compared} models were compared; the corpus looks incomplete.");
        Assert.True(disagreements.Count == 0,
            $"{disagreements.Count} skeletons disagree: {string.Join("; ", disagreements.Take(8))}");
    }

    private static List<string> CorpusRoots()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) is not "1")
            return [];

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eaw", "Data")))
            dir = dir.Parent;

        if (dir is null)
            return [];

        return new[] { "eaw", "foc" }
            .Select(g => Path.Combine(dir.FullName, g, "Data", "Art", "Models"))
            .Where(Directory.Exists)
            .ToList();
    }
}
