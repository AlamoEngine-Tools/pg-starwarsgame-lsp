// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml;

public interface IXmlDiagnosticsRevalidator
{
    Task RevalidateWorkspaceAsync(CancellationToken ct);
    Task RevalidateDocumentAsync(string uri, CancellationToken ct);
}
