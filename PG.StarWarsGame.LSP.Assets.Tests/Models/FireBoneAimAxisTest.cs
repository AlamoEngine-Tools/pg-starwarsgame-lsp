// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Assets.Tests.Models;

/// <summary>
///     Pins the fire-bone aim convention against the shipped Star Destroyer.
/// </summary>
/// <remarks>
///     The whole point of this test is that the naive reading is wrong in a way that LOOKS right on
///     one side of the ship, so a spot check passes and the finished feature is still broken. Both
///     sides are asserted together for exactly that reason.
/// </remarks>
public sealed class FireBoneAimAxisTest
{
    /// <summary>
    ///     Measured off <c>Ev_stardestroyer.alo</c>: engines sit at +Y and the front mounts at -Y, so
    ///     the ship flies toward -Y; port mounts are at +X; up is +Z.
    /// </summary>
    private static readonly Vector3 Forward = -Vector3.UnitY;

    [Fact]
    public void AimDirection_PointsForwardAndOutboard_OnBothSides()
    {
        if (Environment.GetEnvironmentVariable("AET_ALO_CORPUS") is not "1")
            Assert.Skip("Set AET_ALO_CORPUS=1 to read the extracted game trees.");

        var path = CorpusFile("Ev_stardestroyer.alo");
        if (path is null)
            Assert.Skip("Ev_stardestroyer.alo not found.");

        var model = AloModelReader.Read(File.ReadAllBytes(path));
        var fireBones = model.Bones
            .Where(b => b.Name.StartsWith("FP_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(12, fireBones.Count);

        foreach (var bone in fireBones)
        {
            var aim = AlamoFireBone.AimDirection(bone);
            var side = bone.AbsoluteTransform.Translation.X >= 0 ? Vector3.UnitX : -Vector3.UnitX;

            // Every mount on this ship bears forward of the beam and away from the centreline.
            Assert.True(Vector3.Dot(aim, Forward) > 0.4f,
                $"{bone.Name} aims {Vector3.Dot(aim, Forward):F2} forward; expected to bear forward.");
            Assert.True(Vector3.Dot(aim, side) > 0.4f,
                $"{bone.Name} aims {Vector3.Dot(aim, side):F2} outboard; expected to bear away from the hull.");
        }
    }

    [Fact]
    public void TheNaiveLocalYReading_AimsPortAstern_WhichIsTheBugThisPrevents()
    {
        if (Environment.GetEnvironmentVariable("AET_ALO_CORPUS") is not "1")
            Assert.Skip("Set AET_ALO_CORPUS=1 to read the extracted game trees.");

        var path = CorpusFile("Ev_stardestroyer.alo");
        if (path is null)
            Assert.Skip("Ev_stardestroyer.alo not found.");

        var model = AloModelReader.Read(File.ReadAllBytes(path));

        var port = model.Bones.Single(b => b.Name.Equals("FP_F-L_00", StringComparison.OrdinalIgnoreCase));
        var starboard = model.Bones.Single(b => b.Name.Equals("FP_F-R_00", StringComparison.OrdinalIgnoreCase));

        static Vector3 LocalY(AlamoModelBone b)
        {
            var m = b.AbsoluteTransform;
            return Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        }

        // Taking the bone's own +Y as the aim - the obvious thing to do - puts the port guns astern
        // and the starboard guns forward. Mirrored hulls make the mistake self-concealing.
        Assert.True(Vector3.Dot(LocalY(port), Forward) < -0.4f);
        Assert.True(Vector3.Dot(LocalY(starboard), Forward) > 0.4f);

        // And the true aim is that reading turned back by a quarter turn about the vertical.
        var undone = Vector3.Transform(LocalY(port), Matrix4x4.CreateRotationZ(-MathF.PI / 2f));
        Assert.True(Vector3.Dot(Vector3.Normalize(undone), AlamoFireBone.AimDirection(port)) > 0.99f);
    }

    private static string? CorpusFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eaw", "Data")))
            dir = dir.Parent;

        if (dir is null)
            return null;

        return new[] { "foc", "eaw" }
            .Select(g => Path.Combine(dir.FullName, g, "Data", "Art", "Models", name))
            .FirstOrDefault(File.Exists);
    }
}
