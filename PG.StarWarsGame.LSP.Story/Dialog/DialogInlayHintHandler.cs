// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     End-of-line translated text for localisation-key arguments (TEXT/TITLE) in story-dialog
///     scripts - the same <c>"…"</c> / <c>KEY: MISSING</c> format the XML side shows for
///     localisation references. Scope-gated like the dialog diagnostics: .txt files outside the
///     pgproj storyDialog directories get nothing.
/// </summary>
public sealed class DialogInlayHintHandler : InlayHintsHandlerBase
{
    private readonly ILspConfigurationProvider _config;
    private readonly DialogFactProducer _factProducer;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly IStoryDialogScope _scope;
    private readonly IDocumentTextSource _textSource;

    public DialogInlayHintHandler(
        IGameIndexService indexService,
        IStoryDialogScope scope,
        DialogFactProducer factProducer,
        IFileHelper fileHelper,
        IDocumentTextSource textSource,
        ILspConfigurationProvider config)
    {
        _indexService = indexService;
        _scope = scope;
        _factProducer = factProducer;
        _fileHelper = fileHelper;
        _textSource = textSource;
        _config = config;
    }

    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Dialog.InlayHints)
            return Task.FromResult<InlayHintContainer?>(null);

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_scope.IsInScope(uri))
            return Task.FromResult<InlayHintContainer?>(null);

        var text = _textSource.GetText(uri)?.Text;
        if (text is null)
            return Task.FromResult<InlayHintContainer?>(null);

        var document = StoryDialogParser.Parse(text);
        var index = _indexService.Current;
        var range = request.Range;
        var hints = new List<InlayHint>();

        foreach (var fact in _factProducer.Produce(document, uri))
        {
            ct.ThrowIfCancellationRequested();
            if (fact.Def?.Params is null) continue;

            foreach (var param in fact.Def.Params)
            {
                if (param.ReferenceKind != ReferenceKind.LocalisationKey) continue;
                if (param.Position >= fact.Command.Args.Count) continue;

                var arg = fact.Command.Args[param.Position];
                if (arg.Line < range.Start.Line || arg.Line > range.End.Line) continue;

                var translated = index.Localisation.GetValue(arg.Text) ?? arg.Text + ": MISSING";
                hints.Add(new InlayHint
                {
                    Position = new Position(arg.Line, int.MaxValue),
                    Label = ((StringOrInlayHintLabelParts?)$"\"{translated}\"")!,
                    Kind = InlayHintKind.Type,
                    PaddingLeft = true
                });
            }
        }

        return Task.FromResult<InlayHintContainer?>(new InlayHintContainer(hints));
    }

    public override Task<InlayHint> Handle(InlayHint request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(
        InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities)
    {
        return new InlayHintRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.txt"),
            ResolveProvider = false
        };
    }
}