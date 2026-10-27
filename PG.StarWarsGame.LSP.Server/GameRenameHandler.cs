// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     The one rename handler the client sees. It hands the request to the first registered
///     <see cref="IRenameProvider" /> that handles the document; it knows no language itself.
/// </summary>
public sealed class GameRenameHandler : RenameHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IReadOnlyList<IRenameProvider> _providers;

    public GameRenameHandler(IEnumerable<IRenameProvider> providers, IFileHelper fileHelper)
    {
        _providers = [.. providers];
        _fileHelper = fileHelper;
    }

    public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var provider = _providers.FirstOrDefault(p => p.Handles(uri));
        return provider is null ? Task.FromResult<WorkspaceEdit?>(null) : provider.RenameAsync(request, ct);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability, ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                new TextDocumentFilter { Language = "xml" },
                new TextDocumentFilter { Language = "lua" }),
            PrepareProvider = true
        };
    }
}