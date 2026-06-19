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
        int storyParamPosition)
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
}