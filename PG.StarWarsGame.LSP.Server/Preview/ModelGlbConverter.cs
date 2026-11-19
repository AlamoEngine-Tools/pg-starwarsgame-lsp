// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Assets.Projection;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Turns a model's bytes and the clips offered with it into a GLB, and says beforehand which of
///     those clips it would bake. What the preview cache converts with, whoever asked: a request,
///     or the background build that fills the cache ahead of one.
/// </summary>
public interface IModelGlbConverter
{
    /// <summary>
    ///     One model and the clips offered with it, as a GLB. Runs on a cache miss only; any change
    ///     to what it produces from the same bytes raises
    ///     <see cref="PreviewModelCache.ConverterVersion" />.
    /// </summary>
    PreviewModelConversion Convert(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered);

    /// <summary>
    ///     Which of the offered clips <see cref="Convert" /> would bake into this model, and what it
    ///     would say of the rest - the same reading of each clip, without the export. The cache
    ///     asks before it looks an entry up: only a baked clip is part of what the GLB is.
    /// </summary>
    IReadOnlyList<PreviewClipVerdict> Judge(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered);
}

/// <summary>
///     The conversion from the Alamo model and animation formats. It reads nothing but its
///     arguments - not even the model's name - because its result answers for every model of these
///     bytes.
/// </summary>
public sealed class ModelGlbConverter : IModelGlbConverter
{
    public PreviewModelConversion Convert(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered)
    {
        var model = AloModelReader.Read(aloBytes);
        var notes = new List<PreviewClipNote>();
        var clips = LoadAnimations(model, aloBytes, offered, notes);

        return new PreviewModelConversion(ModelGlbExporter.Export(model, clips), [.. clips.Select(c => c.Name)],
            notes);
    }

    public IReadOnlyList<PreviewClipVerdict> Judge(byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered)
    {
        if (offered.Count == 0)
            return [];

        var boneNames = BoneTargets(AloModelReader.Read(aloBytes), aloBytes);
        return
        [
            .. offered.Select(clip =>
            {
                var (animation, note) = Read(clip, boneNames);
                return new PreviewClipVerdict(animation is not null, animation is null ? note : null);
            })
        ];
    }

    /// <summary>
    ///     Parses the offered clips, dropping any that do not belong to this skeleton.
    /// </summary>
    /// <remarks>
    ///     Dropping rather than failing: 50 shipped animations sit beside a model they were not
    ///     authored against, so a mismatch is ordinary data, not an error. The result reports which
    ///     clips actually made it.
    /// </remarks>
    private static List<(string Name, AlamoAnimationContent Animation)> LoadAnimations(
        AlamoModelContent model, byte[] aloBytes, IReadOnlyList<PreviewClipSource> offered,
        List<PreviewClipNote> notes)
    {
        var clips = new List<(string, AlamoAnimationContent)>();
        if (offered.Count == 0)
            return clips;

        var boneNames = BoneTargets(model, aloBytes);

        foreach (var clip in offered)
        {
            var (animation, note) = Read(clip, boneNames);
            if (animation is not null)
                clips.Add((Path.GetFileNameWithoutExtension(clip.Name), animation));
            if (note is not null)
                notes.Add(note);
        }

        return clips;
    }

    /// <summary>The names a clip's tracks are matched against: the model's bone space.</summary>
    private static IReadOnlyList<string> BoneTargets(AlamoModelContent model, byte[] aloBytes)
    {
        // Bones UNIONED WITH MESH NAMES, which is the engine's own bone space: it assumes a bone at
        // the origin of every mesh, so a clip may legitimately drive one of those at an index past
        // the end of the skeleton, and matching against the skeleton alone rejects it.
        //
        // Measured over the whole FOC tree: of 2826 model/clip pairs whose names pair them, two are
        // recovered by the union and none are lost. Both are Underworld turrets -
        // `Ub_turretmst50_idle00` and `Ub_turretptl05_idle00` - and both drive a last bone called
        // `collision` that the model carries only as a MESH. Two is a small number and the rule is
        // still right: the alternative is a clip silently dropped for being what the engine calls
        // valid.
        //
        // Through `ModelNameCatalog` rather than rebuilt here, so the union stays defined once -
        // it is the same call `BoneNameExtractor` makes to populate `index.ModelBones`.
        return ModelNameCatalog.ReadBoneReferenceTargets(
            aloBytes, _ => model.Bones.Select(b => b.Name).ToList());
    }

    /// <summary>
    ///     One offered clip against the model's bone space: the animation when it plays, and what
    ///     there is to say about it either way. The one place that decides, for the conversion and
    ///     for the cache's key alike.
    /// </summary>
    private static (AlamoAnimationContent? Animation, PreviewClipNote? Note) Read(
        PreviewClipSource clip, IReadOnlyList<string> boneNames)
    {
        var name = clip.Name;
        try
        {
            var animation = AlaAnimationReader.Read(clip.Bytes);

            // FITS, not MATCHES. Requiring an identical skeleton dropped clips the base game
            // plays: a unit that borrows another model's animation set does so across skeletons
            // that differ, and the game binds tracks by position regardless. What cannot be
            // recovered is a track reaching past the end of the bone list - there is no node.
            if (!animation.FitsSkeleton(boneNames))
                return (null, new PreviewClipNote(name, PreviewClipNoteKind.CannotPlay,
                    animation.WhyNotModel(boneNames) ?? string.Empty));

            return (animation, animation.WhyNotModel(boneNames) is { } disagreement
                ? new PreviewClipNote(name, PreviewClipNoteKind.DiffersButPlays, disagreement)
                : null);
        }
        catch (AloFormatException e)
        {
            return (null, new PreviewClipNote(name, PreviewClipNoteKind.Unreadable, e.Message));
        }
    }
}
