// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.Localisation.Services;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class GetLanguagesHandler
    : IJsonRpcRequestHandler<GetLanguagesParams, GetLanguagesResult>
{
    private readonly ILanguageService _langService;

    public GetLanguagesHandler(ILanguageService langService) => _langService = langService;

    public Task<GetLanguagesResult> Handle(GetLanguagesParams request, CancellationToken ct)
    {
        var languages = _langService.OfficiallySupported
            .Select(l => l.LanguageIdentifier)
            .ToList();
        return Task.FromResult(new GetLanguagesResult(languages));
    }
}
