// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Parses every shipped animation, and pairs each one with its model.
/// </summary>
/// <remarks>
///     Opt-in via <c>AET_ALO_CORPUS=1</c>, like the model sweep. The pairing half is the valuable
///     part: it checks the reader's bone indices and names against a real skeleton 3771 times, which
///     no synthetic fixture can do, and it simultaneously proves the longest-prefix rule the
///     <c>.ala</c> editor entry point depends on.
/// </remarks>
public sealed class AlaAnimationReaderCorpusTest
{
    private const string OptInVariable = "AET_ALO_CORPUS";

    [Fact]
    public void Read_EveryShippedAnimation_Parses()
    {
        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        var failures = new List<string>();
        var parsed = 0;
        var version2 = 0;

        foreach (var file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.ala")))
            try
            {
                var animation = AlaAnimationReader.Read(File.ReadAllBytes(file));
                Assert.All(animation.Bones, b => Assert.Equal(animation.FrameCount, b.Frames.Count));
                parsed++;

                // Counted so the sweep cannot silently stop covering one of the two formats.
                if (animation.FormatVersion == 2) version2++;
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.Message}");
            }

        Assert.Empty(failures);

        // 3771 animations across the two trees, 1463 of them version 2, at the time of writing.
        // Lower bounds, so a mod tree does not fail the sweep, but high enough that a half-enumerated
        // root or a format that stopped being exercised cannot pass as success.
        Assert.True(parsed > 3000, $"Only {parsed} animations were parsed; the corpus looks incomplete.");
        Assert.True(version2 > 1000, $"Only {version2} version 2 animations were seen.");
    }

    /// <summary>
    ///     Every animation verifies against at least one candidate model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Candidates come from the filename prefix, longest first, searched across BOTH trees -
    ///         which is how the engine resolves anyway, FoC layering over EaW. Searching one tree in
    ///         isolation is wrong, and the corpus proves it: FoC re-exported
    ///         <c>Eb_commandcenter.alo</c> with 59 bones where EaW's has 56, but ships the EaW-authored
    ///         <c>Eb_commandcenter_idle_00.ala</c> beside it. That animation matches the EaW skeleton
    ///         and genuinely does not match the FoC one - 54 animations are in that position.
    ///     </para>
    ///     <para>
    ///         So the prefix rule alone is not enough to pair an animation with a model, and the
    ///         <c>.ala</c> entry point must verify rather than assume. This is that verification,
    ///         exercised 3771 times.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Read_EveryAnimation_VerifiesAgainstSomeCandidateModel()
    {
        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        var models = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.alo")))
        {
            var key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!models.TryGetValue(key, out var paths)) models[key] = paths = [];
            paths.Add(path);
        }

        var boneNameCache = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        List<string> BoneNames(string path)
        {
            if (boneNameCache.TryGetValue(path, out var cached)) return cached;
            return boneNameCache[path] = AloModelReader
                .Read(File.ReadAllBytes(path), AloReadOptions.SkipGeometry)
                .Bones.Select(b => b.Name).ToList();
        }

        var unverified = new List<string>();
        var verified = 0;
        var noCandidate = 0;

        foreach (var file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.ala")))
        {
            var stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var candidates = models.Keys
                .Where(m => stem.StartsWith(m + "_", StringComparison.Ordinal))
                .OrderByDescending(m => m.Length)
                .SelectMany(m => models[m])
                .ToList();

            if (candidates.Count == 0)
            {
                noCandidate++;
                continue;
            }

            var animation = AlaAnimationReader.Read(File.ReadAllBytes(file));
            if (candidates.Any(c => animation.MatchesModel(BoneNames(c))))
                verified++;
            else
                unverified.Add(Path.GetFileName(file));
        }

        // 3700 of 3771 verify. The rest are orphans in the shipped data, not reader failures, and two
        // were confirmed by an independent parse: Nb_basepad.alo carries three bones - Root, Camera01,
        // Camera01.Target - in BOTH trees, while Nb_basepad_deploy.ala drives p_basepad_spark, gird_00
        // and gird_02 at indices 1 to 3, so it names a bone past the end of the skeleton it is filed
        // next to. AloViewer refuses these pairings too; its loader asserts the same name-and-index
        // agreement. Bounded rather than asserted away, so a real regression still shows up as a jump.
        Assert.True(verified > 3600, $"Only {verified} animations verified; expected around 3700.");
        Assert.True(unverified.Count < 60,
            $"{unverified.Count} verified against no model, up from the 50 shipped orphans: " +
            string.Join(", ", unverified.Take(8)));

        // Only the ri_padowan_* animations, whose model does not ship at all.
        Assert.True(noCandidate < 100,
            $"{noCandidate} animations found no candidate model, which is more than expected.");
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
