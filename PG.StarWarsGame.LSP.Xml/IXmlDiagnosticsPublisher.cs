using OmniSharp.Extensions.LanguageServer.Protocol;

namespace PG.StarWarsGame.LSP.Xml;

public interface IXmlDiagnosticsPublisher
{
    void Publish(DocumentUri uri, string text);
    void ClearDiagnostics(DocumentUri uri);
}