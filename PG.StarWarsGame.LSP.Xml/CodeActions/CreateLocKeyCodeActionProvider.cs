// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

internal sealed class CreateLocKeyCodeActionProvider : IXmlCodeActionProvider
{
    private readonly ILspConfigurationProvider _config;

    public CreateLocKeyCodeActionProvider(ILspConfigurationProvider config)
    {
        _config = config;
    }

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        // Gated on features.tools.localisation (not xml.codeActions): the quick-fix drives the
        // createLocalisationKey command, which is localisation tooling.
        if (!_config.Current.Features.Tools.Localisation)
            return [];

        var locKey = ctx.Diagnostic.Data?["createLocKey"]?.Value<string>();
        if (locKey is null)
            return [];

        return
        [
            new CommandOrCodeAction(new CodeAction
            {
                Title = $"Create localisation key '{locKey}'",
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(ctx.Diagnostic),
                Command = new Command
                {
                    Name = "aet-eaw-edit.lsp.createLocalisationKey",
                    Title = "Create localisation key",
                    Arguments = new JArray(locKey)
                }
            })
        ];
    }
}