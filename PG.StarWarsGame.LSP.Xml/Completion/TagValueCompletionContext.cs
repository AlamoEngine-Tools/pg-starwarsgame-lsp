// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion;

public sealed class TagValueCompletionContext
{
    public TagValueCompletionContext(
        string documentUri,
        GameIndex index,
        ISchemaProvider schema,
        HtmlDocument doc,
        HtmlNode enclosingNode,
        string enclosingTag,
        int enclosingDepth,
        XmlTagDefinition? tagDef,
        string partialValue,
        int lineIndex,
        int character,
        bool isStoryParser,
        string? storyParamSide,
        int storyParamPosition,
        int tupleSlotIndex = 0)
    {
        DocumentUri = documentUri;
        Index = index;
        Schema = schema;
        Doc = doc;
        EnclosingNode = enclosingNode;
        EnclosingTag = enclosingTag;
        EnclosingDepth = enclosingDepth;
        TagDef = tagDef;
        PartialValue = partialValue;
        LineIndex = lineIndex;
        Character = character;
        IsStoryParser = isStoryParser;
        StoryParamSide = storyParamSide;
        StoryParamPosition = storyParamPosition;
        TupleSlotIndex = tupleSlotIndex;
    }

    public string DocumentUri { get; }
    public GameIndex Index { get; }
    public ISchemaProvider Schema { get; }
    public HtmlDocument Doc { get; }
    public HtmlNode EnclosingNode { get; }
    public string EnclosingTag { get; }
    public int EnclosingDepth { get; }
    public XmlTagDefinition? TagDef { get; }
    public string PartialValue { get; }
    public int LineIndex { get; }
    public int Character { get; }
    public bool IsStoryParser { get; }
    public string? StoryParamSide { get; }
    public int StoryParamPosition { get; }

    /// <summary>
    ///     0-based index of the comma-separated item the cursor sits in, for tuple-shaped
    ///     <see cref="XmlTagDefinition.ValueType" />s (e.g. <c>HardPointSfxMap</c>). Not clamped: a
    ///     fixed-shape tuple splits on the FIRST comma, so its consumer reads any index past 0 as slot
    ///     1, while a TupleList repeats its slots and takes the index modulo the slot count.
    ///     Meaningless (always 0) for non-tuple types.
    /// </summary>
    public int TupleSlotIndex { get; }
}