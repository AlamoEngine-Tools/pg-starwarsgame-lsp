// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Preview.Sections;

/// <summary>
///     The clips a model plays and the model a unit borrows them from: the animation set of a
///     model reference, and the <c>*_Model_Anim_Override_Name</c> that redirects it (#314).
/// </summary>
public sealed class AnimationSectionBuilder(IMessageCatalog messages)
{
    /// <summary>
    ///     The animation-set overrides, land first.
    /// </summary>
    /// <remarks>
    ///     Only the land tag appears in the shipped data - about 47 uses, all infantry and indigenous -
    ///     but the space one is in the schema and in the engine's own parser, so a mod may well use it.
    /// </remarks>
    private static readonly string[] AnimationOverrideTags =
        ["Land_Model_Anim_Override_Name", "Space_Model_Anim_Override_Name"];

    /// <summary>
    ///     The animations that belong to a model, by filename.
    /// </summary>
    /// <remarks>
    ///     An <c>.ala</c> names no model, so the pairing is the filename: the clips sit beside their
    ///     model under a <c>&lt;model&gt;_</c> prefix, which is the same rule
    ///     <see cref="BuildForAnimation" /> uses in reverse. The separator matters -
    ///     <c>Ei_bobafettish_wave.ala</c> is not Boba Fett's.
    ///     Offered, not verified: each is checked against the skeleton when it is actually loaded,
    ///     and 50 of the shipped animations sit beside a model they do not fit. Listing one that
    ///     turns out not to match costs an entry in a picker; listing none at all - which is what
    ///     happened while nothing could enumerate them - reads as a model with no animations.
    ///     <para>
    ///         The matching lives in <see cref="ModelAnimationClips" />, shared with the death animation
    ///         diagnostic. The longest model name prefixing a clip owns it there: without that a hull
    ///         collected its own death clone's clips - <c>rv_gargantuan_</c> prefixes
    ///         <c>rv_gargantuan_dc_die_00</c> - and since a clip binds by INDEX and carries VISIBILITY tracks,
    ///         the clone's clip switched off whichever hull bones sat at the indices its own skeleton hides.
    ///         Two of the Gargantuan's turrets were mounted on those bones and went with them.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<string> AnimationsFor(GameIndex index, string modelReference)
    {
        return ModelAnimationClips.For(index, PreviewModelReference.Normalise(modelReference));
    }

    /// <summary>
    ///     The model whose animations this object actually plays, or null when it uses its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The override borrows another model's animation set. This used to insist the two
    ///         skeletons be IDENTICAL and report a difference as a warning, on the reasoning that the
    ///         engine could not otherwise bind the tracks. The user reported that the message fires
    ///         all over the BASE GAME, on units that animate correctly in the engine - so the rule
    ///         was simply wrong, and the clips are loaded and bound by index either way.
    ///     </para>
    ///     <para>
    ///         It is still reported, as INFORMATION rather than a warning: where the two skeletons
    ///         disagree about what a bone is called, the clip drives whatever sits at that index, and
    ///         a reader watching a model move oddly deserves to know that is possible. What the exact
    ///         engine rule is - the user's guess is that the animated bones need only share a tree -
    ///         is not settled, and nothing here claims it is.
    ///     </para>
    /// </remarks>
    public string? ResolveAnimationOverride(GameIndex index,
        EffectiveObject effective, string? hull, List<PreviewProblem> problems)
    {
        var overrideModel = AnimationOverrideTags
            .Select(tag => effective.ValueOf(tag))
            .FirstOrDefault(value => value is not null);

        if (overrideModel is null)
            return null;

        if (hull is null)
            return overrideModel;

        var hullBones = index.ModelBones.GetValueOrDefault(ModelBoneKey.From(hull));
        var overrideBones = index.ModelBones.GetValueOrDefault(ModelBoneKey.From(overrideModel));

        // Only compare when both are catalogued; an unknown model is already reported elsewhere.
        if (!hullBones.IsDefaultOrEmpty && !overrideBones.IsDefaultOrEmpty
                                        && !hullBones.SequenceEqual(overrideBones, StringComparer.OrdinalIgnoreCase))
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewAnimationSkeletonMismatch, "info",
                messages.Format("PreviewScene_AnimationSkeletonDiffers", effective.ObjectId, overrideModel, hull,
                    overrideBones.Length, hullBones.Length)));

        return overrideModel;
    }
}