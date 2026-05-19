// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Parsing;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlHoverHandler : HoverHandlerBase
{
    private readonly ILspConfigurationProvider _config;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly ILogger<XmlHoverHandler> _logger;
    private readonly ISchemaProvider _schema;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlHoverHandler(
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        ILspConfigurationProvider config,
        ILogger<XmlHoverHandler> logger,
        IFileTypeRegistry fileTypeRegistry)
    {
        _workspaceHost = workspaceHost;
        _schema = schema;
        _config = config;
        _logger = logger;
        _fileTypeRegistry = fileTypeRegistry;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        _logger.LogDebug("Hover request at {Line}:{Character}",
            request.Position.Line, request.Position.Character);

        var uri = request.TextDocument.Uri.ToString();
        if (!_workspaceHost.TryGet(uri, out var doc))
            return Task.FromResult<Hover?>(null);
        var text = doc.Text;

        var lines = text.Split('\n');
        var lineIndex = request.Position.Line;
        if (lineIndex >= lines.Length)
            return Task.FromResult<Hover?>(null);

        var line = lines[lineIndex].TrimEnd('\r');
        var (tagName, tagStart) = ExtractTagNameAtPosition(line, request.Position.Character);
        if (tagName is null)
            return Task.FromResult<Hover?>(null);

        var locale = _config.Current.Locale;

        // Try element-name-based type lookup first (works when element name = type name).
        var typeDef = _schema.GetObjectType(tagName);

        // Registry-based fallback: for files with arbitrary element names, look up the type
        // via the registry and confirm the cursor is on a depth-1 type-container element.
        if (typeDef is null)
        {
            var normalized = XmlGameDocumentParser.NormalizeDocumentUri(uri);
            var fileTypes = _fileTypeRegistry.GetTypesForFile(normalized);
            if (!fileTypes.IsEmpty)
            {
                var registeredType = fileTypes
                    .Select(t => _schema.GetObjectType(t))
                    .FirstOrDefault(t => t?.NameTag is not null);
                if (registeredType is not null && IsDepthOneElement(lines, lineIndex, tagStart))
                    typeDef = registeredType;
            }
        }

        if (typeDef is not null)
        {
            _logger.LogDebug("Hover resolved: type {TagName}", tagName);
            return Task.FromResult<Hover?>(BuildTypeHover(typeDef, tagName, lineIndex, tagStart, locale));
        }

        // The document root element is always a file container, never a field tag.
        if (IsDocumentRootTag(lines, lineIndex, tagStart))
            return Task.FromResult<Hover?>(null);

        var tagDef = _schema.GetTag(tagName);
        if (tagDef is not null)
        {
            _logger.LogDebug("Hover resolved: tag {TagName}", tagName);
            return Task.FromResult<Hover?>(BuildTagHover(tagDef, tagName, lineIndex, tagStart, locale));
        }

        _logger.LogDebug("Hover request at {Line}:{Character} produced no result.",
            request.Position.Line, request.Position.Character);
        return Task.FromResult<Hover?>(null);
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions { DocumentSelector = TextDocumentSelector.ForLanguage("xml") };
    }

    private static Hover BuildTagHover(XmlTagDefinition tag, string tagName, int line, int colStart, string locale)
    {
        var sb = new StringBuilder();

        sb.Append($"### `{tagName}` *`{tag.ValueType}");
        if (tag.ReferenceKind != ReferenceKind.None && tag.ReferenceKind != ReferenceKind.Unknown)
        {
            if (tag.ReferenceKind == ReferenceKind.Enum)
                sb.Append($"::{tag.EnumName}");
            else
                sb.Append($"::{tag.ReferenceKind}");
        }

        sb.Append("`*\n");

        if (tag.AvailableSince is not null || tag.Deprecated)
        {
            var parts = new List<string>();
            if (tag.AvailableSince is not null) parts.Add($"Since {tag.AvailableSince}");
            if (tag.Deprecated) parts.Add("Deprecated");
            sb.AppendLine($"**{string.Join(" · ", parts)}**");
        }

        sb.AppendLine();
        sb.Append(DescriptionResolver.Resolve(tag.Description, locale));

        var hint = ValueTypeHint.Build(tag);
        if (hint is not null)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(hint);
        }

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = sb.ToString()
            }),
            Range = MakeRange(line, colStart, tagName.Length)
        };
    }

    private static Hover BuildTypeHover(GameObjectTypeDefinition type, string tagName, int line, int colStart,
        string locale)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### `{tagName}`");

        sb.AppendLine(type.NameTag is not null
            ? $"*name tag: `{type.NameTag}`*"
            : "*singleton type*");
        sb.AppendLine();

        sb.Append(DescriptionResolver.Resolve(type.Description, locale));

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = sb.ToString()
            }),
            Range = MakeRange(line, colStart, tagName.Length)
        };
    }

    private static LspRange MakeRange(int line, int colStart, int length)
    {
        return new LspRange(new Position(line, colStart), new Position(line, colStart + length));
    }

    /// <summary>
    ///     Returns the XML element name under the cursor and its starting column,
    ///     or (null, 0) if the cursor is not on an element name.
    /// </summary>
    private static (string? name, int colStart) ExtractTagNameAtPosition(string line, int character)
    {
        if (character > line.Length)
            character = line.Length;

        // Expand left
        var start = character;
        while (start > 0 && IsTagNameChar(line[start - 1]))
            start--;

        // Expand right
        var end = character;
        while (end < line.Length && IsTagNameChar(line[end]))
            end++;

        if (start == end)
            return (null, 0);

        // Confirm preceded by '<' or '</'
        var prefixPos = start - 1;
        while (prefixPos >= 0 && line[prefixPos] == '/')
            prefixPos--;

        if (prefixPos < 0 || line[prefixPos] != '<')
            return (null, 0);

        return (line[start..end], start);
    }

    private static bool IsTagNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    ///     Returns true when the tag at <paramref name="tagNameStart" /> is the document root element
    ///     (depth 0 in the XML tree). Root elements are file-level containers, not field tags.
    /// </summary>
    private static bool IsDocumentRootTag(string[] lines, int lineIdx, int tagNameStart)
    {
        var line = lineIdx < lines.Length ? lines[lineIdx] : string.Empty;
        var isClosing = tagNameStart >= 1 && line[tagNameStart - 1] == '/';
        var ltCol = isClosing ? tagNameStart - 2 : tagNameStart - 1;

        var depth = 0;
        for (var li = 0; li <= lineIdx; li++)
        {
            var l = (li < lines.Length ? lines[li] : string.Empty).TrimEnd('\r');
            var colLimit = li < lineIdx ? l.Length : ltCol;
            var pos = 0;
            while (pos < colLimit)
            {
                var open = l.IndexOf('<', pos);
                if (open < 0 || open >= colLimit) break;
                var close = l.IndexOf('>', open);
                if (close < 0) break;
                var inner = l[(open + 1)..Math.Min(close, l.Length)].Trim();
                var sc = inner.EndsWith('/');
                if (sc) inner = inner[..^1].Trim();
                if (inner.StartsWith('/'))
                    depth = Math.Max(0, depth - 1);
                else if (!inner.StartsWith('!') && !inner.StartsWith('?') && !sc)
                    depth++;
                pos = close + 1;
            }
        }

        return depth == (isClosing ? 1 : 0);
    }

    /// <summary>
    ///     Returns true when the tag is a depth-1 element (one level below the document root).
    ///     In multi-instance files this identifies type-container elements.
    /// </summary>
    private static bool IsDepthOneElement(string[] lines, int lineIdx, int tagNameStart)
    {
        var line = lineIdx < lines.Length ? lines[lineIdx] : string.Empty;
        var isClosing = tagNameStart >= 1 && line[tagNameStart - 1] == '/';
        var ltCol = isClosing ? tagNameStart - 2 : tagNameStart - 1;

        var depth = 0;
        for (var li = 0; li <= lineIdx; li++)
        {
            var l = (li < lines.Length ? lines[li] : string.Empty).TrimEnd('\r');
            var colLimit = li < lineIdx ? l.Length : ltCol;
            var pos = 0;
            while (pos < colLimit)
            {
                var open = l.IndexOf('<', pos);
                if (open < 0 || open >= colLimit) break;
                var close = l.IndexOf('>', open);
                if (close < 0) break;
                var inner = l[(open + 1)..Math.Min(close, l.Length)].Trim();
                var sc = inner.EndsWith('/');
                if (sc) inner = inner[..^1].Trim();
                if (inner.StartsWith('/'))
                    depth = Math.Max(0, depth - 1);
                else if (!inner.StartsWith('!') && !inner.StartsWith('?') && !sc)
                    depth++;
                pos = close + 1;
            }
        }

        return depth == (isClosing ? 2 : 1);
    }
}