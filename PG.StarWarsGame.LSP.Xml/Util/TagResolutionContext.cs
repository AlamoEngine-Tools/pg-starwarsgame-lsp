// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;

namespace PG.StarWarsGame.LSP.Xml.Util;

/// <summary>
/// One node in the ancestor-type chain from the enclosing object definition to the current element.
/// Built once per document traversal; all handlers consume it without re-parsing.
/// <c>Node</c> is a pointer into the already-parsed HAP tree, enabling lazy cross-reference
/// navigation without re-parsing. <c>Depth</c> is precomputed for future handler dispatch.
/// </summary>
internal sealed class TagResolutionContext
{
    public string ObjectTypeName { get; }
    public int Depth { get; }
    public HtmlNode Node { get; }
    public TagResolutionContext? Parent { get; }

    public TagResolutionContext(
        string objectTypeName, int depth, HtmlNode node, TagResolutionContext? parent = null)
    {
        ObjectTypeName = objectTypeName;
        Depth = depth;
        Node = node;
        Parent = parent;
    }
}
