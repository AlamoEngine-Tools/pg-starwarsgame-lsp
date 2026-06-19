// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion;

public sealed class TagNameCompletionContext
{
    public TagNameCompletionContext(
        string documentUri,
        GameIndex index,
        ISchemaProvider schema,
        HtmlNode enclosingNode,
        string enclosingTag,
        int enclosingDepth,
        string prefix,
        string text,
        int lineIndex,
        int character,
        bool isStoryParser)
    {
        DocumentUri = documentUri;
        Index = index;
        Schema = schema;
        EnclosingNode = enclosingNode;
        EnclosingTag = enclosingTag;
        EnclosingDepth = enclosingDepth;
        Prefix = prefix;
        Text = text;
        LineIndex = lineIndex;
        Character = character;
        IsStoryParser = isStoryParser;
    }

    public string DocumentUri { get; }
    public GameIndex Index { get; }
    public ISchemaProvider Schema { get; }
    public HtmlNode EnclosingNode { get; }
    public string EnclosingTag { get; }
    public int EnclosingDepth { get; }
    public string Prefix { get; }
    public string Text { get; }
    public int LineIndex { get; }
    public int Character { get; }
    public bool IsStoryParser { get; }
}