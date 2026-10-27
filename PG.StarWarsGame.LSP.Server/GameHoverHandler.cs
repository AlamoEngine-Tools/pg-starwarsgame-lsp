// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Features;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     The one hover handler the client sees. It hands the request to the first registered
///     <see cref="IHoverProvider" /> that handles the document; it knows no language itself.
/// </summary>
public sealed class GameHoverHandler : HoverHandlerBase
{
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<GameHoverHandler> _logger;
    private readonly IReadOnlyList<IHoverProvider> _providers;

    public GameHoverHandler(IEnumerable<IHoverProvider> providers, IFileHelper fileHelper,
        ILogger<GameHoverHandler> logger)
    {
        _providers = [.. providers];
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        var provider = _providers.FirstOrDefault(p => p.Handles(uri));
        if (provider is null)
        {
            _logger.LogWarning("No hover provider handles {Uri}", uri);
            return Task.FromResult<Hover?>(null);
        }

        _logger.LogDebug("Hover request for {Uri} goes to {Provider}", uri, provider.GetType().Name);
        return provider.HoverAsync(request, ct);
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = new TextDocumentSelector(
                TextDocumentFilter.ForLanguage("xml"),
                TextDocumentFilter.ForLanguage("lua"))
        };
    }
}