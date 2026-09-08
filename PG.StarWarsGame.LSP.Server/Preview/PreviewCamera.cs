// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     A camera a model carries in its own skeleton, in MODEL space.
/// </summary>
/// <param name="Name">
///     The bone's name as the file spells it, casing included.
///     <para>
///         Kept verbatim because it is quotable: <c>Get_Bone_Position</c> is a shipped game-object
///         method - the campaign scripts use it to anchor an effect between two bones - so this name
///         can be pasted into Lua to address the same point from script.
///     </para>
/// </param>
/// <param name="Position">Where the camera stands, as three floats.</param>
/// <param name="Target">What it looks at, as three floats.</param>
/// <remarks>
///     <para>
///         MODEL space, not the viewport's. The exporter turns Alamo's Z-up into glTF's Y-up with a
///         rotation on the root node, so a camera placed from these numbers without the same
///         rotation lands a quarter turn out. Leaving the conversion to the client lets it reuse the
///         one transform the geometry already went through, which cannot drift out of step with it.
///     </para>
/// </remarks>
public sealed record PreviewCamera(
    string Name, IReadOnlyList<float> Position, IReadOnlyList<float> Target);

/// <summary>
///     Reads the cameras out of a model's skeleton.
/// </summary>
/// <remarks>
///     <para>
///         Measured across all 3340 shipped models: 447 carry a camera-ish bone and 437 of those
///         pair properly, spelled <c>Camera01</c> (424), <c>Camera02</c> (13) and <c>Camera03</c>
///         (2). Structures carry one 48.3% of the time against about 12% for everything else, which
///         is not how uniform export junk distributes - a human aimed these.
///     </para>
///     <para>
///         Mechanically they are 3ds Max target cameras, out of the same export path that produced
///         the <c>Spot01</c> / <c>Omni01</c> lights this preview rejects. The difference is that a
///         camera is useful whether or not the engine reads it: someone deliberately framed the
///         model, and that framing is exactly what an icon wants.
///     </para>
/// </remarks>
public static class PreviewCameraReader
{
    /// <summary>What marks the aiming half of a pair.</summary>
    private const string TargetSuffix = ".Target";

    /// <summary>
    ///     What a camera bone is called.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The NAME has to carry the rule, because "has a matching <c>.Target</c>" does not:
    ///         3ds Max spotlights are target objects too. Measured on the real <c>Eb_icc.alo</c>,
    ///         which pairs <c>Spot01</c> through <c>Spot05</c> alongside its one genuine camera -
    ///         the same export residue the model LIGHTS were rejected as, and a light is not a shot.
    ///     </para>
    ///     <para>
    ///         Anchored at both ends so <c>CameraShake</c> - a bone that MOVES the shot rather than
    ///         defining one - does not qualify. The digits are optional for a mod that ships a lone
    ///         <c>Camera</c>; the shipped files spell it <c>Camera01</c> (424), <c>Camera02</c> (13)
    ///         and <c>Camera03</c> (2).
    ///     </para>
    /// </remarks>
    private static readonly Regex CameraName =
        new(@"^camera\d*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Every properly paired camera the model declares, in file order.
    /// </summary>
    /// <remarks>
    ///     A camera with no target is dropped rather than aimed at a guess. Pointing it at the model
    ///     centre would look like the author's framing while being ours, and the whole value of this
    ///     is that a person chose it.
    /// </remarks>
    public static IReadOnlyList<PreviewCamera> From(AlamoModelContent model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var targets = new Dictionary<string, AlamoModelBone>(StringComparer.OrdinalIgnoreCase);

        foreach (var bone in model.Bones)
            if (bone.Name.EndsWith(TargetSuffix, StringComparison.OrdinalIgnoreCase))
                targets[bone.Name[..^TargetSuffix.Length]] = bone;

        var cameras = new List<PreviewCamera>();

        foreach (var bone in model.Bones)
        {
            // Named like a camera AND carrying a target. Both halves are needed: the name alone
            // would take `CameraShake`, and the target alone would take every Max spotlight in the
            // file. It is also what keeps the ten unpaired oddities out without a blocklist -
            // `C_camera.alo`, `C_cameratarget.alo` and three interface models carry `b_camera_g` /
            // `b_camera_t`, and a camera model has nothing to frame anyway.
            if (!CameraName.IsMatch(bone.Name)
                || !targets.TryGetValue(bone.Name, out var target))
                continue;

            cameras.Add(new PreviewCamera(bone.Name, Translation(bone), Translation(target)));
        }

        return cameras;
    }

    /// <summary>
    ///     Where a bone ends up, taken from its ABSOLUTE transform.
    /// </summary>
    /// <remarks>
    ///     Every camera measured is parented to bone 0 and sits outside the model bounds aiming
    ///     inward - <c>Ev_lambdashuttle</c> above and behind at 232 units, <c>Eb_icc</c> looking
    ///     down from 653 - so the absolute translation is usable as it stands.
    /// </remarks>
    private static float[] Translation(AlamoModelBone bone)
    {
        var t = bone.AbsoluteTransform.Translation;
        return [t.X, t.Y, t.Z];
    }
}
