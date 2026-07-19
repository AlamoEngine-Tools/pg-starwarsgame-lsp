// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>Production applier: <c>workspace/applyEdit</c> through the live server facade.</summary>
public sealed class FacadeWorkspaceEditApplier(Func<ILanguageServerFacade> facade) : IWorkspaceEditApplier
{
    public async Task<bool> ApplyAsync(WorkspaceEdit edit, string label, CancellationToken ct)
    {
        var response = await facade().SendRequest(
            new ApplyWorkspaceEditParams { Edit = edit, Label = label }, ct);
        return response.Applied;
    }
}