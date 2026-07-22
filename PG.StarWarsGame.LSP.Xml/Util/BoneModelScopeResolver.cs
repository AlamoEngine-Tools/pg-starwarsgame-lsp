// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
///     Resolves the set of <c>.alo</c> models in scope for a bone-name reference tag: the models named
///     by sibling <see cref="ReferenceKind.ModelFile" /> tags (e.g. <c>Space_Model_Name</c>) on the tag's
///     owning object, or any ancestor object up the tree. Bones are model-specific, so this is the
///     "which model(s) does this bone belong to" step shared by bone-name completion and the bone-model
///     inlay hint - keeping the walk in one place so the two can never diverge on what counts as a model
///     in scope. Keys are reduced through <see cref="ModelBoneKey.From" />, matching how the bone catalog
///     (<see cref="GameIndex.ModelBones" />) is keyed.
/// </summary>
public static class BoneModelScopeResolver
{
    // Climbing past this depth is pointless: model tags always live on the object or one of its
    // immediate containers, never near the document root.
    private const int MaxAncestorDepth = 6;

    /// <summary>
    ///     The <see cref="ModelBoneKey" />-normalised model keys in scope for <paramref name="boneTag" />,
    ///     from the nearest ancestor level that names any model. Empty when none is reachable.
    /// </summary>
    public static IReadOnlyList<string> FindModelKeys(HtmlNode boneTag, ISchemaProvider schema)
    {
        var result = new List<string>();
        var current = boneTag.ParentNode;

        for (var depth = 0; current is { NodeType: HtmlNodeType.Element } && depth < MaxAncestorDepth; depth++)
        {
            foreach (var child in current.ChildNodes)
            {
                if (child.NodeType != HtmlNodeType.Element) continue;
                if (schema.GetTag(child.Name)?.ReferenceKind != ReferenceKind.ModelFile) continue;

                var value = child.InnerText.Trim();
                if (value.Length > 0)
                    result.Add(ModelBoneKey.From(value));
            }

            // Stop at the first level that yields any model reference - a nearer model shadows a farther one.
            if (result.Count > 0)
                break;

            current = current.ParentNode;
        }

        return result;
    }
}
