// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Completion;

/// <summary>
///     Produces bone-name completion proposals for <c>boneName</c> reference tags. Bones are
///     model-specific, and which model applies is context-dependent: inside a <c>HardPoint</c> a bone
///     resolves by role (mounting hull vs the hardpoint's own <c>Model_To_Attach</c>, cross-file), so an
///     <c>Attachment_Bone</c> is completed from the mounting hull and a <c>Turret_Bone_Name</c> from the
///     attached model; elsewhere it is the union of the models named by sibling model tags on the owning
///     object or an ancestor. Resolution runs through <see cref="HardpointBoneModelResolver" />, the same
///     entry point as the bone-model inlay hint, so completion and validation cannot disagree.
/// </summary>
public sealed class BoneNameCompletionHelper
{
    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _tagSource;

    public BoneNameCompletionHelper(ISchemaProvider schema, IVariantTagSource tagSource)
    {
        _schema = schema;
        _tagSource = tagSource;
    }

    public IReadOnlyList<ValueProposal> GetProposals(HtmlNode enclosingNode, string partial, GameIndex index)
    {
        var modelKeys = new HardpointBoneModelResolver(index, _schema, _tagSource)
            .ResolveModelKeysForBoneTag(enclosingNode);
        if (modelKeys.Count == 0)
            return [];

        var bones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in modelKeys)
            if (index.ModelBones.TryGetValue(key, out var modelBones))
                foreach (var bone in modelBones)
                    bones.Add(bone);

        return bones
            .Where(b => partial.Length == 0 || b.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(b => new ValueProposal { Label = b })
            .ToList();
    }
}
