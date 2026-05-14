using System.Collections.Concurrent;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlDocumentBuffer
{
    private readonly ConcurrentDictionary<string, string> _docs = new(StringComparer.Ordinal);

    public void Set(DocumentUri uri, string text)
    {
        _docs[uri.ToString()] = text;
    }

    public string? Get(DocumentUri uri)
    {
        return _docs.GetValueOrDefault(uri.ToString());
    }

    public void Remove(DocumentUri uri)
    {
        _docs.TryRemove(uri.ToString(), out _);
    }
}