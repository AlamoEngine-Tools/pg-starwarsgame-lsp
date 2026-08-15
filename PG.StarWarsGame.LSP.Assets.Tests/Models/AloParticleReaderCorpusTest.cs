// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Parses every shipped particle system.
/// </summary>
/// <remarks>
///     The reader is strict - an unknown property id is a refusal, not a skip - so this sweep is what
///     establishes that the id set is actually closed over the shipped data. It is also what proves
///     the version claim the whole design rests on: that no Star Wars particle file uses format 2.
/// </remarks>
public sealed class AloParticleReaderCorpusTest
{
    private const string OptInVariable = "AET_ALO_CORPUS";

    [Fact]
    public void Read_EveryShippedParticleSystem_Parses()
    {
        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        var failures = new List<string>();
        var parsed = 0;
        var emitters = 0;
        var withSpawnLinks = 0;

        foreach (var file in roots.SelectMany(r => Directory.EnumerateFiles(r, "*.alo")))
        {
            var bytes = File.ReadAllBytes(file);
            if (AloFile.Classify(bytes) != AloFileKind.Particle)
                continue;

            try
            {
                var system = AloParticleReader.Read(bytes);
                parsed++;
                emitters += system.Emitters.Count;

                foreach (var emitter in system.Emitters)
                {
                    // Seven tracks, always: the block's fourteen chunks are a header and a key list
                    // for each, and a curve that lost its endpoints would fade to nothing.
                    Assert.Equal(7, emitter.Tracks.Count);
                    Assert.All(emitter.Tracks, t => Assert.True(t.Keys.Count >= 2));

                    // Keys must run forwards, or sampling walks off the end of the curve.
                    foreach (var track in emitter.Tracks)
                        for (var i = 1; i < track.Keys.Count; i++)
                            Assert.True(track.Keys[i].Time >= track.Keys[i - 1].Time,
                                $"{Path.GetFileName(file)} {track.Channel} keys run backwards");

                    if (emitter.SpawnOnDeath >= 0 || emitter.SpawnDuringLife >= 0)
                        withSpawnLinks++;
                }
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} failed: {string.Join("; ", failures.Take(5))}");

        // 987 systems across the two trees at the time of writing.
        Assert.True(parsed > 900, $"Only {parsed} particle systems were parsed.");
        Assert.True(emitters > parsed, "Every system parsed with no emitters at all.");

        // Chained effects - an explosion starting its own smoke - are the reason the spawn links are
        // read. If none survive, the links are being dropped.
        Assert.True(withSpawnLinks > 0, "No emitter carried a spawn link.");
    }

    [Fact]
    public void Read_NoShippedParticleSystemUsesFormatVersion2()
    {
        var roots = CorpusRoots();
        if (roots.Count == 0)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        // The claim the reader's whole scope rests on. Version 2 would need around sixty plugin
        // types; if one ever appears in the corpus, that decision has to be revisited rather than
        // discovered as a mystery refusal.
        var version2 = roots
            .SelectMany(r => Directory.EnumerateFiles(r, "*.alo"))
            .Where(f =>
            {
                Span<byte> header = stackalloc byte[4];
                using var stream = File.OpenRead(f);
                return stream.ReadAtLeast(header, 4, false) == 4
                       && BitConverter.ToUInt32(header) == 0x1500;
            })
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(version2.Count == 0,
            $"Format 2 particle systems exist after all: {string.Join(", ", version2.Take(5))}");
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
