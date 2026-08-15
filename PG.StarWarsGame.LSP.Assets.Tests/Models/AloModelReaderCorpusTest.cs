// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Parses every shipped model in the repository's extracted game trees.
/// </summary>
/// <remarks>
///     <para>
///         This is the test that matters most for a binary format reader. Hand-built fixtures only
///         ever prove the reader handles the shapes we already thought of; the 2353 shipped models are
///         where the variants we did not think of live - the older bone chunk, meshes with no
///         sub-meshes, the collision-tree block, models with no connections at all.
///     </para>
///     <para>
///         Opt-in, because reading ~1.5 GB takes far longer than the rest of the suite combined and
///         the trees are not present in every checkout. Set <c>AET_ALO_CORPUS=1</c> to run it, and run
///         it after every change to the reader.
///     </para>
/// </remarks>
public sealed class AloModelReaderCorpusTest
{
    private const string OptInVariable = "AET_ALO_CORPUS";

    [Fact]
    public void Read_EveryShippedModel_Parses()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) is not "1")
            Assert.Skip($"Set {OptInVariable}=1 to sweep the extracted game trees.");

        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip("No extracted game tree found next to the repository root.");

        var failures = new List<string>();
        var parsed = 0;

        foreach (var file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.alo")))
        {
            var bytes = File.ReadAllBytes(file);

            // Particle systems share the extension but not the format; they are chunk 10's job.
            if (AloFile.Classify(bytes) != AloFileKind.Model)
                continue;

            try
            {
                var model = AloModelReader.Read(bytes);
                Assert.NotEmpty(model.Bones);
                parsed++;
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.Message}");
            }
        }

        // 2353 models across the two shipped trees at the time of writing. A lower bound rather than
        // an exact count, so adding a mod tree does not fail the sweep - but high enough that an
        // empty or half-enumerated root cannot pass as success.
        Assert.True(parsed > 2000, $"Only {parsed} models were parsed; the corpus looks incomplete.");
        Assert.Empty(failures);
    }

    /// <summary>
    ///     The <c>eaw/</c> and <c>foc/</c> trees at the repository root, when present. Located by
    ///     walking up from the test binary rather than by a relative path, which breaks the moment the
    ///     output layout changes.
    /// </summary>
    private static List<string> CorpusRoots()
    {
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
