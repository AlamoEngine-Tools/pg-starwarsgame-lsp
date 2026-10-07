// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Xml.Formatting;
using PG.StarWarsGame.LSP.Xml.Util;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     Formats a game XML document, or the part of one a range covers, through
///     <see cref="XmlFormatter" />. A refused document gets no edits and a warning saying why.
/// </summary>
internal sealed class XmlFormattingRequest(
    IXmlParseCache parseCache,
    IEaWXmlContext context,
    IFileHelper fileHelper,
    IUserNotifier notifier,
    ILspConfigurationProvider config)
{
    public bool Enabled => config.Current.Features.Xml.Formatting;

    public TextEditContainer? Format(TextDocumentIdentifier document, FormattingOptions options, LspRange? range)
    {
        if (!Enabled) return null;
        var uri = fileHelper.NormalizeUri(document.Uri.ToString());
        if (!context.IsEaWXmlFile(uri) || parseCache.GetOrParse(uri) is not { } doc) return null;

        var unit = options.InsertSpaces ? new string(' ', Math.Max(1, options.TabSize)) : "\t";
        (int, int)? offsets = range is null
            ? null
            : (XmlUtility.PositionToOffset(doc.Text, range.Start.Line, range.Start.Character),
                XmlUtility.PositionToOffset(doc.Text, range.End.Line, range.End.Character));

        var result = XmlFormatter.Format(doc, unit, offsets);
        if (result.Refusal is { } why)
        {
            notifier.ShowWarning(why);
            return null;
        }

        return new TextEditContainer(result.Edits.Select(e => new TextEdit
        {
            Range = new LspRange(Position(doc, e.Start), Position(doc, e.Start + e.Length)), NewText = e.NewText
        }));
    }

    private static Position Position(ParsedXmlDocument doc, int offset)
    {
        var (line, col) = doc.LineIndex.GetPosition(offset);
        return new Position(line, col);
    }
}

public sealed class XmlDocumentFormattingHandler : DocumentFormattingHandlerBase
{
    private readonly XmlFormattingRequest _request;

    public XmlDocumentFormattingHandler(IXmlParseCache parseCache, IEaWXmlContext context, IFileHelper fileHelper,
        IUserNotifier notifier, ILspConfigurationProvider config)
    {
        _request = new XmlFormattingRequest(parseCache, context, fileHelper, notifier, config);
    }

    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken ct)
    {
        return Task.FromResult(_request.Format(request.TextDocument, request.Options, null));
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentFormattingRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }
}

public sealed class XmlDocumentRangeFormattingHandler : DocumentRangeFormattingHandlerBase
{
    private readonly XmlFormattingRequest _request;

    public XmlDocumentRangeFormattingHandler(IXmlParseCache parseCache, IEaWXmlContext context,
        IFileHelper fileHelper, IUserNotifier notifier, ILspConfigurationProvider config)
    {
        _request = new XmlFormattingRequest(parseCache, context, fileHelper, notifier, config);
    }

    public override Task<TextEditContainer> Handle(DocumentRangeFormattingParams request, CancellationToken ct)
    {
        return Task.FromResult(_request.Format(request.TextDocument, request.Options, request.Range) ??
                               new TextEditContainer());
    }

    protected override DocumentRangeFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentRangeFormattingCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DocumentRangeFormattingRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml")
        };
    }
}