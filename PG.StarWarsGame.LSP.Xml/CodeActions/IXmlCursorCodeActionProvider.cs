// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

/// <summary>
///     A code action offered for what is under the cursor rather than for a diagnostic - the
///     <see cref="IXmlCodeActionProvider" />s only ever see diagnostics, so an action on a line with
///     nothing wrong on it needs this seam.
/// </summary>
public interface IXmlCursorCodeActionProvider
{
    IEnumerable<CommandOrCodeAction> Handle(DocumentUri documentUri, Position position);
}