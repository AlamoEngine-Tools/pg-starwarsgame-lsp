// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Exports real shipped models and reads them back under strict glTF validation.
/// </summary>
/// <remarks>
///     <para>
///         The value here is that the validator is not ours. Strict mode enforces the spec's own
///         rules - unit-length normals, unit quaternions, accessor bounds, joint indices inside the
///         skin - so this catches whole classes of malformed output that a hand-written assertion
///         would have to anticipate one at a time.
///     </para>
///     <para>
///         Opt-in via <c>AET_ALO_CORPUS=1</c>, like the other sweeps. Exporting the whole corpus is
///         far slower than parsing it, so this takes a bounded sample that deliberately includes the
///         awkward shapes: the largest models, and a skinned character.
///     </para>
/// </remarks>
public sealed class ModelGlbExporterCorpusTest
{
    private const string OptInVariable = "AET_ALO_CORPUS";

    /// <summary>Models chosen for their shape rather than at random.</summary>
    private static readonly string[] Interesting =
    [
        "Ev_stardestroyer.alo", // the reference hull: 71 bones, 15 meshes, 22 proxies
        "Ai_rancor.alo",        // RSkin + bump + colorize, a real skinned creature
        "Alttest.alo",          // ALT-tagged meshes and proxies
        "Rb_commandcenter.alo"  // the largest shipped model at 7.3 MB
    ];

    [Fact]
    public void Export_NamedModels_PassStrictGltfValidation()
    {
        var root = ModelsRoot();
        if (root is null)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        var checkedAny = false;

        foreach (var name in Interesting)
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
                continue;

            checkedAny = true;
            ExportAndValidate(path);
        }

        Assert.True(checkedAny, "None of the named models were present.");
    }

    [Fact]
    public void Export_ASampleOfEveryShippedModel_PassesStrictGltfValidation()
    {
        var root = ModelsRoot();
        if (root is null)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        // Every 10th file, particle systems dropped - they share the extension but have no skeleton.
        // Broad enough to sweep the variety of vertex formats and skinning modes without exporting
        // 1.5 GB of geometry on every run.
        var sample = Directory.EnumerateFiles(root, "*.alo")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Where((_, i) => i % 10 == 0)
            .Where(IsModel)
            .ToList();

        var failures = new List<string>();
        var exported = 0;

        foreach (var path in sample)
            try
            {
                ExportAndValidate(path);
                exported++;
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(path)}: {e.Message}");
            }

        // Exact rather than a guessed floor: every model in the sample must export. A separate
        // sanity floor catches an empty or half-enumerated root, which an "all of nothing succeeded"
        // assertion would otherwise pass.
        Assert.True(sample.Count > 50, $"The sample held only {sample.Count} models.");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {sample.Count} failed: {string.Join("; ", failures.Take(5))}");
        Assert.Equal(sample.Count, exported);
    }

    /// <summary>Particle systems share the extension but start with their own chunk, not a skeleton.</summary>
    private static bool IsModel(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(header, 4, false) == 4 &&
               BitConverter.ToUInt32(header) == 0x200;
    }

    private static void ExportAndValidate(string path)
    {
        var model = AloModelReader.Read(File.ReadAllBytes(path));
        var glb = ModelGlbExporter.Export(model);

        // Strict mode is the point: it runs the spec's rules over what we wrote.
        var read = ModelRoot.ReadGLB(
            new MemoryStream(glb), new ReadSettings { Validation = ValidationMode.Strict });

        Assert.NotNull(read.DefaultScene);
    }

    [Fact]
    public void Export_CarriesAltAndLodLevelsThroughToTheGlb()
    {
        // Alttest.alo is the engine's own worked example - it ships p_fire_small01_ALT0. The levels
        // are encoded in mesh and proxy NAMES, and the client can only gate on them if the exporter
        // puts them in extras: without this the damage-state control has nothing to switch.
        var models = ModelsRoot();
        if (models is null)
            Assert.Skip($"Set {OptInVariable}=1 with an extracted game tree present to run this.");

        var path = Path.Combine(models, "Alttest.alo");
        if (!File.Exists(path))
            Assert.Skip("Alttest.alo is not in this tree.");

        var model = AloModelReader.Read(File.ReadAllBytes(path));
        Assert.Contains(model.Meshes, m => m.Alt is not null);

        var read = ModelRoot.ReadGLB(
            new MemoryStream(ModelGlbExporter.Export(model)), new ReadSettings());

        var levels = read.LogicalMaterials
            .Select(m => m.Extras?["alamoAlt"]?.GetValue<int>())
            .Where(alt => alt is not null)
            .ToList();

        Assert.NotEmpty(levels);
    }

    private static string? ModelsRoot()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) is not "1")
            return null;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "eaw", "Data", "Art", "Models");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
