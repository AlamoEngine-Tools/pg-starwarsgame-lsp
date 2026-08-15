// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     The cameras a model carries in its own skeleton.
/// </summary>
/// <remarks>
///     447 of the 3340 shipped models carry a camera-ish bone and 437 of those pair properly, so this
///     is a real authored thing rather than export noise - a human aimed each one at its own model,
///     which is exactly the framing an icon wants.
/// </remarks>
public sealed class PreviewCameraTest
{
    private static AlamoModelBone Bone(int index, string name, float x, float y, float z)
    {
        // The absolute transform is what matters: every measured camera is parented to bone 0 and
        // sits outside the model aiming in, so its own world translation is directly usable.
        return new AlamoModelBone(index, name, index == 0 ? -1 : 0, true,
            AlamoBillboardType.Disable, Matrix4x4.Identity,
            Matrix4x4.CreateTranslation(x, y, z));
    }

    private static AlamoModelContent Model(params AlamoModelBone[] bones)
    {
        return new AlamoModelContent(bones, [], [], [], []);
    }

    [Fact]
    public void PairsACameraWithItsTarget()
    {
        var cameras = PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "Camera01", 10, 20, 30),
            Bone(2, "Camera01.Target", 1, 2, 3)));

        var camera = Assert.Single(cameras);

        Assert.Equal("Camera01", camera.Name);
        Assert.Equal([10f, 20f, 30f], camera.Position);
        Assert.Equal([1f, 2f, 3f], camera.Target);
    }

    [Fact]
    public void PairsCaseInsensitively()
    {
        // Six shipped files spell it `.target`, and bone names are case-insensitive everywhere else
        // in this codebase anyway.
        var cameras = PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "CAMERA01", 10, 0, 0),
            Bone(2, "camera01.target", 0, 0, 0)));

        Assert.Single(cameras);
    }

    [Fact]
    public void KeepsTheNameTheFileWrote()
    {
        // It is quotable: `Get_Bone_Position` is a shipped game-object method, so this name can be
        // pasted straight into Lua to address the same point from script.
        var cameras = PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "CAMERA01", 10, 0, 0),
            Bone(2, "CAMERA01.TARGET", 0, 0, 0)));

        Assert.Equal("CAMERA01", Assert.Single(cameras).Name);
    }

    [Fact]
    public void ReadsEveryCameraInFileOrder()
    {
        var cameras = PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "Camera01", 1, 0, 0),
            Bone(2, "Camera01.Target", 0, 0, 0),
            Bone(3, "Camera02", 2, 0, 0),
            Bone(4, "Camera02.Target", 0, 0, 0)));

        Assert.Equal(["Camera01", "Camera02"], cameras.Select(c => c.Name));
    }

    [Fact]
    public void IgnoresACameraWithNoTarget()
    {
        // Without a target there is no aim, and guessing one - the model centre, say - would be our
        // framing wearing the author's name.
        Assert.Empty(PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "Camera01", 10, 0, 0))));
    }

    [Fact]
    public void IgnoresATargetWithNoCamera()
    {
        Assert.Empty(PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "Camera01.Target", 0, 0, 0))));
    }

    [Fact]
    public void IgnoresBonesThatMerelyMentionACamera()
    {
        // `C_camera.alo`, `C_cameratarget.alo` and three interface models carry `b_camera_g` and
        // `b_camera_t`. They are the ten that do not pair, and the pairing rule excludes them by
        // itself - no blocklist needed, which is one less thing to keep in step with the data.
        Assert.Empty(PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "b_camera_g", 10, 0, 0),
            Bone(2, "b_camera_t", 0, 0, 0),
            Bone(3, "CameraShake", 5, 0, 0))));
    }

    [Fact]
    public void IgnoresAMaxSpotlightEvenThoughItHasATarget()
    {
        // Found on the real `Eb_icc.alo`, which returned Spot01 through Spot05 alongside its one
        // genuine camera. A 3ds Max spotlight is a target object too, so "anything with a .Target
        // sibling" is not the rule - these are the same export residue the model LIGHTS were
        // rejected as, and a light is not a shot.
        var cameras = PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "Camera01", 10, 0, 0),
            Bone(2, "Camera01.Target", 0, 0, 0),
            Bone(3, "Spot01", 100, 0, 0),
            Bone(4, "Spot01.Target", 0, 0, 0),
            Bone(5, "Omni01", 50, 0, 0),
            Bone(6, "Omni01.Target", 0, 0, 0)));

        Assert.Equal("Camera01", Assert.Single(cameras).Name);
    }

    [Fact]
    public void TakesTheThreeSpellingsTheFilesActuallyUse()
    {
        // Camera01 (424 files), Camera02 (13) and Camera03 (2).
        var cameras = PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "Camera01", 1, 0, 0), Bone(2, "Camera01.Target", 0, 0, 0),
            Bone(3, "Camera02", 2, 0, 0), Bone(4, "Camera02.Target", 0, 0, 0),
            Bone(5, "Camera03", 3, 0, 0), Bone(6, "Camera03.Target", 0, 0, 0)));

        Assert.Equal(3, cameras.Count);
    }

    [Fact]
    public void IgnoresABoneMerelyStartingWithCamera()
    {
        // `CameraShake` is a bone that moves the shot, not one that defines it.
        Assert.Empty(PreviewCameraReader.From(Model(
            Bone(0, "Root", 0, 0, 0),
            Bone(1, "CameraShake", 10, 0, 0),
            Bone(2, "CameraShake.Target", 0, 0, 0))));
    }

    [Fact]
    public void HasNoCamerasWhenTheModelDeclaresNone()
    {
        Assert.Empty(PreviewCameraReader.From(Model(Bone(0, "Root", 0, 0, 0))));
    }

    [Fact]
    public void ReadsAModelWithNoBonesAtAll()
    {
        Assert.Empty(PreviewCameraReader.From(Model()));
    }
}
