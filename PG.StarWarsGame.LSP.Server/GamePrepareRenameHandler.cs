// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>Prepare-rename routes as <see cref="GameRenameHandler" /> does: to the provider that handles the document.</summary>
public sealed class GamePrepareRenameHandler : PrepareRenameHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly IReadOnlyList<IRenameProvider> _providers;

    public GamePrepareRenameHandler(IEnumerable<IRenameProvider> providers, IFileHelper fileHelper)
    {
        _providers = [.. providers];
        _fileHelper = fileHelper;
    }

    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var provider = _providers.FirstOrDefault(p => p.Handles(uri));
        return provider is null
            ? Task.FromResult<RangeOrPlaceholderRange?>(null)
            : provider.PrepareAsync(request, ct);
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