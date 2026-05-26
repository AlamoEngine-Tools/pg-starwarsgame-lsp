// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCodeLensHandler : CodeLensHandlerBase
{
    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly ILogger<XmlCodeLensHandler> _logger;

    public XmlCodeLensHandler(IGameIndexService indexService, ILogger<XmlCodeLensHandler> logger,
        IEaWXmlContext eaWXmlContext, IFileHelper fileHelper)
    {
        _indexService = indexService;
        _logger = logger;
        _eaWXmlContext = eaWXmlContext;
        _fileHelper = fileHelper;
    }

    public override Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken ct)
    {
        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult<CodeLensContainer?>(null);

        var index = _indexService.Current;
        if (!index.Documents.TryGetValue(uri, out var docIndex))
            return Task.FromResult<CodeLensContainer?>(new CodeLensContainer());

        var lenses = new List<CodeLens>();
        foreach (var symbol in docIndex.Symbols)
        {
            if (symbol.Origin is not FileOrigin fo)
                continue;

            var count = index.WorkspaceReferences.TryGetValue(symbol.Id, out var refs) ? refs.Length : 0;
            var title = count == 1 ? "1 reference" : $"{count} references";
            var range = new LspRange(new Position(fo.Line, 0), new Position(fo.Line, 0));

            Command? command = null;
            if (count > 0)
            {
                var locations = refs!.Select(r => new
                {
                    uri = r.DocumentUri,
                    range = new
                    {
                        start = new { line = r.Line, character = r.Column },
                        end = new { line = r.Line, character = r.Column + r.Length }
                    }
                });

                command = new Command
                {
                    Title = title,
                    Name = "aet.showReferences",
                    Arguments = JArray.FromObject(new object[]
                    {
                        fo.Uri,
                        new { line = fo.Line, character = fo.Column ?? 0 },
                        locations.ToArray()
                    })
                };
            }
            else
            {
                command = new Command { Title = title };
            }

            lenses.Add(new CodeLens { Range = range, Command = command });
            _logger.LogDebug("CodeLens: {Id} → {Count} reference(s) at line {Line}", symbol.Id, count, fo.Line);
        }

        return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(lenses));
    }

    public override Task<CodeLens> Handle(CodeLens request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(
        CodeLensCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CodeLensRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            ResolveProvider = false
        };
    }
}