using OmniSharp.Extensions.LanguageServer.Protocol;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlDocumentBufferTests
{
    private static DocumentUri Uri(string path = "file:///test.xml")
    {
        return DocumentUri.From(path);
    }

    [Fact]
    public void Set_ThenGet_ReturnsSameText()
    {
        var buf = new XmlDocumentBuffer();
        buf.Set(Uri(), "hello");
        Assert.Equal("hello", buf.Get(Uri()));
    }

    [Fact]
    public void Get_NeverSet_ReturnsNull()
    {
        var buf = new XmlDocumentBuffer();
        Assert.Null(buf.Get(Uri()));
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        var buf = new XmlDocumentBuffer();
        buf.Set(Uri(), "content");
        buf.Remove(Uri());
        Assert.Null(buf.Get(Uri()));
    }

    [Fact]
    public void Set_Overwrites_PreviousText()
    {
        var buf = new XmlDocumentBuffer();
        buf.Set(Uri(), "first");
        buf.Set(Uri(), "second");
        Assert.Equal("second", buf.Get(Uri()));
    }

    [Fact]
    public void MultipleUris_AreIndependent()
    {
        var buf = new XmlDocumentBuffer();
        buf.Set(Uri("file:///a.xml"), "alpha");
        buf.Set(Uri("file:///b.xml"), "beta");

        Assert.Equal("alpha", buf.Get(Uri("file:///a.xml")));
        Assert.Equal("beta", buf.Get(Uri("file:///b.xml")));
    }

    [Fact]
    public void Remove_NonExistent_DoesNotThrow()
    {
        var buf = new XmlDocumentBuffer();
        var ex = Record.Exception(() => buf.Remove(Uri("file:///ghost.xml")));
        Assert.Null(ex);
    }
}