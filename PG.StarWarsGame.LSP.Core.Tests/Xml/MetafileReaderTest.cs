// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Core.Tests.Xml;

/// <summary>
///     The one file-list reader behind the indexer, the baseline builder and the story chain scanner
///     (#308): every entry as written, in document order, from the text or the attribute form.
/// </summary>
public sealed class MetafileReaderTest
{
    [Fact]
    public void ReadsEveryEntryInDocumentOrder_AsWritten()
    {
        const string xml = """
                           <Game_Object_Files>
                             <File>Foo.xml</File>
                             <File>Units\Foo.xml</File>
                             <File>Data\XML\Foo.xml</File>
                           </Game_Object_Files>
                           """;

        Assert.Equal(["Foo.xml", @"Units\Foo.xml", @"Data\XML\Foo.xml"], MetafileReader.ReadFileList(xml));
    }

    [Fact]
    public void TrimsTheTextAroundAnEntry()
    {
        Assert.Equal(["Foo.xml"], MetafileReader.ReadFileList("<Files><File>\n  Foo.xml \n</File></Files>"));
    }

    [Fact]
    public void SkipsAnEmptyEntry()
    {
        const string xml = "<Files><File></File><File>  </File><File/><File>Foo.xml</File></Files>";

        Assert.Equal(["Foo.xml"], MetafileReader.ReadFileList(xml));
    }

    [Fact]
    public void TakesTheFilenameAttribute_WhenTheElementHoldsNoText()
    {
        const string xml = """<Files><File filename="Attr.xml"/><File FileName=" Mixed.xml "></File></Files>""";

        Assert.Equal(["Attr.xml", "Mixed.xml"], MetafileReader.ReadFileList(xml));
    }

    [Fact]
    public void TheTextWinsOverTheAttribute()
    {
        Assert.Equal(["Text.xml"],
            MetafileReader.ReadFileList("""<Files><File filename="Attr.xml">Text.xml</File></Files>"""));
    }

    [Fact]
    public void MatchesTheElementNameInAnyCase()
    {
        const string xml = "<Files><file>lower.xml</file><FILE>upper.xml</FILE><File>mixed.xml</File></Files>";

        Assert.Equal(["lower.xml", "upper.xml", "mixed.xml"], MetafileReader.ReadFileList(xml));
    }

    [Fact]
    public void IgnoresElementsOfOtherNames()
    {
        Assert.Empty(MetafileReader.ReadFileList("<Files><Entry>Foo.xml</Entry><Files2>x</Files2></Files>"));
    }

    [Fact]
    public void AnEmptyDocument_HasNoEntries()
    {
        Assert.Empty(MetafileReader.ReadFileList(""));
    }
}