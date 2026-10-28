// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Where a publisher's diagnostics go.
/// </summary>
/// <remarks>
///     The one seam between a publisher and the editor. Every publisher used to carry two
///     constructors for it - one taking the server facade, one taking a delegate for the tests - and
///     the second was the constructor the container fell back to whenever the first had a parameter
///     it could not fill (#305). One sink, one constructor.
/// </remarks>
public interface IDiagnosticsSink
{
    void Publish(PublishDiagnosticsParams diagnostics);
}

/// <summary>The editor, over <c>textDocument/publishDiagnostics</c>.</summary>
public sealed class LanguageServerDiagnosticsSink(ILanguageServerFacade server) : IDiagnosticsSink
{
    public void Publish(PublishDiagnosticsParams diagnostics)
    {
        server.TextDocument.PublishDiagnostics(diagnostics);
    }
}