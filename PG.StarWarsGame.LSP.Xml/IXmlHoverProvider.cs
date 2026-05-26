// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml;

public interface IXmlHoverProvider
{
    Task<Hover?> Handle(HoverParams request, CancellationToken ct);
}