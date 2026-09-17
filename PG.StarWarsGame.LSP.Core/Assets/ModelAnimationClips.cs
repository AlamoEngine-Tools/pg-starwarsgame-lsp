// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>
///     Which animation files belong to a model, and how many takes of an animation type it has.
/// </summary>
/// <remarks>
///     <para>
///         A clip is <c>&lt;model&gt;_&lt;type&gt;_&lt;nn&gt;.ala</c> beside the model. Built for the preview's
///         animation picker and shared so a diagnostic asks the question the same way - two copies of "which
///         clips are this model's" would disagree exactly where it matters, on a variant model whose name
///         prefixes another's.
///     </para>
///     <para>
///         The LONGEST model name that prefixes a clip owns it: <c>rv_gargantuan_</c> prefixes
///         <c>rv_gargantuan_dc_die_00</c>, and that clip is the death clone's, not the hull's. The separator
///         after the model name is required - <c>Ei_bobafettish_wave</c> is not Boba Fett's.
///     </para>
/// </remarks>
public static partial class ModelAnimationClips
{
    /// <summary>The clip file names that belong to <paramref name="modelReference" />, in name order.</summary>
    /// <param name="index">Supplies the animation files and the known models.</param>
    /// <param name="modelReference">A model name, with or without a path or extension, in any case.</param>
    public static IReadOnlyList<string> For(GameIndex index, string modelReference)
    {
        var stem = Stem(modelReference);
        if (stem.Length == 0) return [];

        // Every OTHER model whose name also starts this one's - the variants that would otherwise have
        // their clips taken.
        var longer = index.ModelBones.Keys
            .Select(Stem)
            .Where(other => other.Length > stem.Length
                            && other.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
            .Select(other => other + "_")
            .ToList();

        return
        [
            .. index.AssetFiles
                .GetByExtension(".ala")
                .Select(Path.GetFileName)
                .Where(name => name is not null
                               && name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
                               && !longer.Any(other => name.StartsWith(other, StringComparison.OrdinalIgnoreCase)))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    ///     How many takes of <paramref name="animationType" /> the clips hold - what
    ///     <c>ModelAnimsListClass::Get_Total_Animations_Of_Type</c> answers for the model.
    /// </summary>
    /// <param name="clipNames">The model's clips, as <see cref="For" /> lists them.</param>
    /// <param name="modelReference">The model they belong to.</param>
    /// <param name="animationType">An animation type name, e.g. <c>DIE</c> or <c>DEPLOYED_DIE</c>, in any case.</param>
    public static int CountOfType(IEnumerable<string> clipNames, string modelReference, string animationType)
    {
        var stem = Stem(modelReference);
        var type = animationType.Trim();
        if (stem.Length == 0 || type.Length == 0) return 0;

        return clipNames.Count(name =>
        {
            if (!name.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)) return false;

            var take = TakeSuffix().Match(name);
            if (!take.Success) return false;

            // Between "<model>_" and "_<nn>.ala", so DIE never counts a DEPLOYED_DIE take.
            var clipType = name[(stem.Length + 1)..take.Index];
            return clipType.Equals(type, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>The model's file name without its extension, from a bare name or a path with either slash.</summary>
    private static string Stem(string reference)
    {
        var fileName = ModelBoneKey.From(reference);
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    [GeneratedRegex(@"_\d+\.ala$", RegexOptions.IgnoreCase)]
    private static partial Regex TakeSuffix();
}