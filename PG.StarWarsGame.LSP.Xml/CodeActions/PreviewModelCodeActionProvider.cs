// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

/// <summary>
///     Offers "Preview model X.ALO" on a value that names a model: a whole model tag from anywhere on
///     its line, and the model item of a slotted tuple under the cursor. Gated on
///     <c>features.tools.modelPreview</c>.
/// </summary>
/// <remarks>
///     A model that is not found is offered DISABLED with the reason rather than left out, so the
///     reader sees that the action exists and why it cannot run. A value that is not an
///     <c>.alo</c> gets nothing: there is no file to open, and the format diagnostic already says
///     what is wrong.
/// </remarks>
internal sealed class PreviewModelCodeActionProvider : IXmlCursorCodeActionProvider
{
    public const string PreviewModelCommand = "aet-eaw-edit.lsp.previewModel";

    private static readonly string[] ModelExtensions = [".alo"];

    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly IGameIndexService _indexService;
    private readonly IXmlParseCache _parseCache;
    private readonly ISchemaProvider _schema;

    public PreviewModelCodeActionProvider(IXmlParseCache parseCache, ISchemaProvider schema,
        IGameIndexService indexService, IFileHelper fileHelper, ILspConfigurationProvider config)
    {
        _parseCache = parseCache;
        _schema = schema;
        _indexService = indexService;
        _fileHelper = fileHelper;
        _config = config;
    }

    public IEnumerable<CommandOrCodeAction> Handle(DocumentUri documentUri, Position position)
    {
        if (!_config.Current.Features.Tools.ModelPreview)
            return [];

        var parsed = _parseCache.GetOrParse(_fileHelper.NormalizeUri(documentUri.ToString()));
        if (parsed is null)
            return [];

        // The element that ENCLOSES the cursor line, not the one that starts on it: a multi-line
        // value puts most of its items on lines where no element starts.
        var node = XmlUtility.FindEnclosingElement(parsed.Html, position.Line);
        if (node is null)
            return [];

        // A container element has children, not a value.
        if (node.ChildNodes.Any(c => c.NodeType == HtmlAgilityPack.HtmlNodeType.Element))
            return [];

        var tag = _schema.GetTag(node.Name);
        if (tag is null)
            return [];

        var index = _indexService.Current;
        var model = tag.Slots.Count > 0
            ? ModelItemUnderCursor(tag, node, parsed.Text, position, index)
            : tag.ReferenceKind == ReferenceKind.ModelFile
                ? node.InnerText.Trim()
                : null;

        if (string.IsNullOrEmpty(model) || !model.EndsWith(".alo", StringComparison.OrdinalIgnoreCase))
            return [];

        var found = AssetFileLookup.Resolves(index.AssetFiles, model, ModelExtensions, []);
        return
        [
            new CommandOrCodeAction(new CodeAction
            {
                Title = $"Preview model {model}",
                Command = new Command
                {
                    Title = $"Preview model {model}",
                    Name = PreviewModelCommand,
                    Arguments = new JArray(model)
                },
                Disabled = found ? null : new CodeActionDisabled { Reason = "Model file not found" }
            })
        ];
    }

    private static string? ModelItemUnderCursor(XmlTagDefinition tag, HtmlAgilityPack.HtmlNode node, string text,
        Position position, GameIndex index)
    {
        var lineIndex = new LineOffsetIndex(text);
        var isKey = tag.ValueType == XmlValueType.ListMap ? ListMapKeys.FromIndex(tag, index) : null;
        foreach (var item in TupleItems.Read(tag, node.InnerText, isKey))
        {
            var (line, column) = lineIndex.GetPosition(node.InnerStartIndex + item.Offset);
            if (line != position.Line || position.Character < column || position.Character >= column + item.Text.Length)
                continue;

            return item.Slot?.ReferenceKind == ReferenceKind.ModelFile ? item.Text : null;
        }

        return null;
    }
}