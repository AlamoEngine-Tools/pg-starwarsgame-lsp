// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

public sealed class XmlCodeActionContext
{
    public XmlCodeActionContext(DocumentUri documentUri, Diagnostic diagnostic)
    {
        DocumentUri = documentUri;
        Diagnostic = diagnostic;
    }

    public DocumentUri DocumentUri { get; }
    public Diagnostic Diagnostic { get; }

    /// <summary>The server's texts in the configured locale; English when nothing set it.</summary>
    public IMessageCatalog Messages { get; init; } = ResxMessageCatalog.Server("en");
}