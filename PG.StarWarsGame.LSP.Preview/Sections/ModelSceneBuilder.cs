// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Preview;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Preview.Sections;

/// <summary>
///     The scene of a bare file: a model or particle system opened directly, or an animation whose
///     model has to be recovered from the skeletons it fits - and the cameras a model declares, which
///     the object scene reads for its hull too (#314).
/// </summary>
public sealed class ModelSceneBuilder(
    IGameAssetResolver assets,
    ISchemaProvider schema,
    IVariantTagSource tagSource,
    IMessageCatalog messages)
{
    private readonly FactionSectionBuilder _factions = new(schema, tagSource);
    private readonly PartsSectionBuilder _parts = new(assets, tagSource, messages);

    public PreviewScene BuildForModel(GameIndex index, string modelReference)
    {
        var path = PreviewModelReference.ModelPath(modelReference);
        var location = assets.Locate(path);
        var resolved = location is not null;

        var problems = new List<PreviewProblem>();
        if (!resolved)
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelNotFound, "error",
                messages.Format("PreviewScene_ModelWasNotFound", modelReference, assets.Tiers.Explain(messages))));

        var kind = PreviewSceneKind.Model;
        if (resolved)
            switch (AloFile.Classify(assets.Read(path) ?? []))
            {
                case AloFileKind.Particle:
                    kind = PreviewSceneKind.Particle;
                    break;

                case AloFileKind.Unknown:
                    problems.Add(new PreviewProblem(DiagnosticIds.PreviewNotAModel, "error",
                        messages.Format("PreviewScene_NotAModel", modelReference)));
                    break;

                case AloFileKind.Model:
                default:
                    // The kind it started as.
                    break;
            }

        var part = new PreviewPart("hull", modelReference, null, null, PreviewPartOrigin.Hull, null,
            resolved);

        var particles = kind == PreviewSceneKind.Model && resolved
            ? _parts.ParticlesOf(part, [], problems)
            : [];

        return new PreviewScene(
            kind,
            modelReference,
            [part],
            [],
            _factions.Factions(index),
            problems,
            assets.Tiers,
            null,
            particles,
            AnimationSectionBuilder.AnimationsFor(index, modelReference),
            resolved ? CamerasOf(modelReference, problems) : [])
        {
            SourceFile = SourceFileOf(location)
        };
    }

    /// <summary>
    ///     A scene for an animation file: the model it drives, with the clip already selected.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An <c>.ala</c> names no model, so the pairing is recovered from the filename - the
    ///         longest model name that prefixes it - and then VERIFIED, never assumed. Verification is
    ///         not optional: FoC re-exported some models without re-exporting their animations, so a
    ///         file sitting right next to its apparent model can belong to a different skeleton, and
    ///         50 shipped animations match no model at all.
    ///     </para>
    ///     <para>
    ///         Candidates come from the indexed bone catalog rather than a directory scan, which means
    ///         no filesystem walk and the same layered view of the game the rest of the server has.
    ///     </para>
    /// </remarks>
    public PreviewScene BuildForAnimation(GameIndex index, string animationReference)
    {
        var animation = PreviewModelReference.Normalise(animationReference);
        var stem = Path.GetFileNameWithoutExtension(animation).ToLowerInvariant();

        var bytes = assets.Read(PreviewModelReference.ModelPath(animation));
        AlamoAnimationContent? clip = null;

        if (bytes is not null)
            try
            {
                clip = AlaAnimationReader.Read(bytes);
            }
            catch (AloFormatException)
            {
                // Reported below as "does not match any model" - a file that will not parse cannot be
                // paired either, and one message is clearer than two.
            }

        var candidates = index.ModelBones.Keys
            .Where(key => stem.StartsWith(
                Path.GetFileNameWithoutExtension(key) + "_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(key => key.Length)
            .ToList();

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator - the
        // first fitting model ends the search with a built scene; a query would bury that exit.
        foreach (var key in candidates)
        {
            if (clip is null || !clip.MatchesModel(index.ModelBones[key]))
                continue;

            var scene = BuildForModel(index, key);
            return scene with
            {
                Subject = animation,
                Problems = [.. scene.Problems]
            };
        }

        var reason = candidates.Count == 0
            ? messages.Format("PreviewScene_NoModelForAnimation", animation)
            : messages.Format("PreviewScene_SkeletonMismatch", animation, string.Join(", ", candidates.Take(3)));

        return PreviewScene.NotFound(animation, reason, assets.Tiers);
    }

    /// <summary>
    ///     The cameras a model carries in its own skeleton.
    /// </summary>
    /// <remarks>
    ///     Its own read, and a cheap one: <c>SkipGeometry</c> wants the skeleton and nothing else,
    ///     which is the same reason the particle resolver reads it that way. A camera that cannot be
    ///     read costs the shot and nothing else, so a malformed file is reported at warning and the
    ///     rest of the scene stands.
    /// </remarks>
    public IReadOnlyList<PreviewCamera> CamerasOf(
        string modelReference, List<PreviewProblem> problems)
    {
        var bytes = assets.Read(PreviewModelReference.ModelPath(modelReference));
        if (bytes is null || AloFile.Classify(bytes) != AloFileKind.Model)
            return [];

        try
        {
            return PreviewCameraReader.From(AloModelReader.Read(bytes, AloReadOptions.SkipGeometry));
        }
        catch (AloFormatException e)
        {
            problems.Add(new PreviewProblem(DiagnosticIds.PreviewModelUnreadable, "warning",
                messages.Format("PreviewScene_CouldNotBeReadIts", modelReference, e.Message)));
            return [];
        }
    }

    /// <summary>
    ///     The file an external tool would be handed, or null when the model resolved nowhere.
    /// </summary>
    /// <remarks>
    ///     An archive hit's resolved path is the path INSIDE the MEG, which no tool can open - so it
    ///     travels as "packed" with no path rather than as something that looks openable.
    /// </remarks>
    private static PreviewSourceFile? SourceFileOf(GameAssetLocation? location)
    {
        if (location is null) return null;

        return location.Tier == GameAssetTier.Archive
            ? new PreviewSourceFile(null, true)
            : new PreviewSourceFile(location.ResolvedPath, false);
    }
}