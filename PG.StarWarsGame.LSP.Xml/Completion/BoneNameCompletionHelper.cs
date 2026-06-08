// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion;

/// <summary>
///     Produces bone-name completion proposals for <c>boneName</c> reference tags. Bones are
///     model-specific: the candidate set is the union of the bones exposed by the <c>.alo</c> model(s)
///     referenced by sibling model tags (e.g. <c>Space_Model_Name</c>) on the XML object that owns the
///     bone tag, or any ancestor object up the tree.
/// </summary>
public sealed class BoneNameCompletionHelper
{
    // Climbing past this depth is pointless: model tags always live on the object or one of its
    // immediate containers, never near the document root.
    private const int MaxAncestorDepth = 6;

    private readonly ISchemaProvider _schema;

    public BoneNameCompletionHelper(ISchemaProvider schema)
    {
        _schema = schema;
    }

    public IReadOnlyList<ValueProposal> GetProposals(HtmlNode enclosingNode, string partial, GameIndex index)
    {
        var modelPaths = FindModelPaths(enclosingNode);
        if (modelPaths.Count == 0)
            return [];

        var bones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in modelPaths)
            if (index.ModelBones.TryGetValue(path, out var modelBones))
                foreach (var bone in modelBones)
                    bones.Add(bone);

        return bones
            .Where(b => partial.Length == 0 || b.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(b => new ValueProposal { Label = b })
            .ToList();
    }

    // Walks up the ancestor chain from the bone tag's owning object, collecting normalised model
    // paths from sibling model tags. Stops at the first level that yields any model reference.
    private List<string> FindModelPaths(HtmlNode enclosingNode)
    {
        var result = new List<string>();
        var current = enclosingNode.ParentNode;

        for (var depth = 0; current is { NodeType: HtmlNodeType.Element } && depth < MaxAncestorDepth; depth++)
        {
            foreach (var child in current.ChildNodes)
            {
                if (child.NodeType != HtmlNodeType.Element) continue;
                var tagDef = _schema.GetTag(child.Name);
                if (tagDef?.ReferenceKind != ReferenceKind.ModelFile) continue;

                var value = child.InnerText.Trim();
                if (value.Length > 0)
                    result.Add(Normalize(value));
            }

            if (result.Count > 0)
                break;

            current = current.ParentNode;
        }

        return result;
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }
}