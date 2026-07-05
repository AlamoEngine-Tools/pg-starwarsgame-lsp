// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCompletionHandler : CompletionHandlerBase
{
    private static readonly Regex ParamTagPattern =
        new(@"^(Event|Reward)_Param(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Every validator for these types splits on the FIRST comma only — completions therefore only
    // ever need to distinguish "before the first comma" (slot 0) from "after it" (slot 1), regardless
    // of how many further commas appear within slot 1's own value.
    private static readonly HashSet<XmlValueType> TupleValueTypes =
    [
        XmlValueType.HardPointSfxMap, XmlValueType.AbilitySfxMap, XmlValueType.ConditionalSfxEvent,
        XmlValueType.UnitSpawnTable, XmlValueType.AbilityModMultiplier, XmlValueType.TupleList,
        XmlValueType.InaccuracyMap
    ];

    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly IGameIndexService _indexService;
    private readonly ISchemaProvider _schema;
    private readonly IXmlTagNameCompletionStrategyRegistry _tagNameRegistry;
    private readonly IXmlTagValueCompletionStrategyRegistry _tagValueRegistry;
    private readonly IXmlParseCache _parseCache;
    private readonly ILspConfigurationProvider _config;

    public XmlCompletionHandler(
        IXmlParseCache parseCache,
        ISchemaProvider schema,
        IGameIndexService indexService,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper,
        IEaWXmlContext eaWXmlContext,
        IXmlTagNameCompletionStrategyRegistry tagNameRegistry,
        IXmlTagValueCompletionStrategyRegistry tagValueRegistry,
        ILspConfigurationProvider config)
    {
        _parseCache = parseCache;
        _schema = schema;
        _indexService = indexService;
        _fileTypeRegistry = fileTypeRegistry;
        _fileHelper = fileHelper;
        _eaWXmlContext = eaWXmlContext;
        _tagNameRegistry = tagNameRegistry;
        _tagValueRegistry = tagValueRegistry;
        _config = config;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Xml.Completion)
            return Task.FromResult(new CompletionList());

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult(new CompletionList());
        var parsed = _parseCache.GetOrParse(uri);
        if (parsed is null)
            return Task.FromResult(new CompletionList());
        var text = parsed.Text;

        var lines = parsed.Lines;
        var lineIndex = request.Position.Line;
        if (lineIndex >= lines.Length)
            return Task.FromResult(new CompletionList());

        var line = lines[lineIndex].TrimEnd('\r');
        var character = request.Position.Character;

        // Tag-name completion: cursor is right after '<', possibly with a partial name typed.
        // This check must come before IsCursorInsideTagBracket because the tag-name position
        // is technically inside a bracket (the opening '<' has no paired '>').
        if (IsTagNameContext(line, character))
        {
            var prefix = ExtractPartialTagName(line, character);
            var anglePos = character - prefix.Length - 1;
            var truncatedDoc = XmlUtility.CreateHtmlDocument(BuildTruncatedText(lines, lineIndex, anglePos));
            var enclosingNode = XmlUtility.FindEnclosingElement(truncatedDoc, lineIndex);
            if (enclosingNode is null)
                return Task.FromResult(new CompletionList());

            var isStoryParser = IsStoryParserDocument(uri);
            var index = _indexService.Current;
            var tagNameCtx = new TagNameCompletionContext(
                uri, index, _schema, enclosingNode, enclosingNode.Name,
                XmlUtility.GetDepth(enclosingNode), prefix, text, lineIndex, character, isStoryParser);
            var items = _tagNameRegistry.GetCompletions(tagNameCtx);
            return Task.FromResult(new CompletionList(items));
        }

        // Bail out for other cursor-inside-tag positions (attributes, etc.)
        if (IsCursorInsideTagBracket(line, character))
            return Task.FromResult(new CompletionList());

        // Value completion: cursor is inside an element body
        var valueDoc = parsed.Html;
        var enclosingValueNode = XmlUtility.FindEnclosingElement(valueDoc, lineIndex);
        var enclosingTag = enclosingValueNode?.Name;
        var enclosingDepth = enclosingValueNode is null ? 0 : XmlUtility.GetDepth(enclosingValueNode);
        if (enclosingTag is null)
            return Task.FromResult(new CompletionList());

        // Depth 1 = cursor directly inside the file-level container — never a field tag.
        if (enclosingDepth == 1)
            return Task.FromResult(new CompletionList());

        // Depth 2 in a multi-instance file = cursor inside a type container, not a field tag.
        if (enclosingDepth == 2 && IsMultiInstanceFile(uri))
            return Task.FromResult(new CompletionList());

        // Fallback for unregistered files: element-name-based type detection.
        if (_schema.GetObjectType(enclosingTag) is not null)
            return Task.FromResult(new CompletionList());

        // Pre-compute story param context for StoryParamValueCompletionStrategy
        var paramMatch = ParamTagPattern.Match(enclosingTag);
        var isStoryParserForValue = IsStoryParserDocument(uri);
        string? storyParamSide = null;
        var storyParamPosition = 0;
        if (paramMatch.Success && isStoryParserForValue)
        {
            storyParamSide = paramMatch.Groups[1].Value;
            storyParamPosition = int.Parse(paramMatch.Groups[2].Value) - 1;
        }

        // Three-tier type-aware tag lookup (mirrors XmlDocumentFactProducer.ResolveTag):
        //   Tier 1 — ability sub-object context (Type56/57): use the ability schema type
        //   Tier 2 — registered file types: use GetTagsForType for each registered type
        //   Tier 3 — flat fallback
        // Only resolved when not in story-param context (which ignores tagDef entirely).
        XmlTagDefinition? tagDef = null;
        if (storyParamSide is null)
        {
            var fileTypes = _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(uri));
            if (TryResolveContainingAbilityType(enclosingValueNode!, out var containingAbilityType))
            {
                var resCtx = new TagResolutionContext(
                    containingAbilityType!, XmlUtility.GetDepth(enclosingValueNode!), enclosingValueNode!);
                tagDef = XmlTagResolver.Resolve(_schema, enclosingTag, resCtx);
            }
            else if (!fileTypes.IsEmpty)
            {
                XmlTagDefinition? typeSpecificDef = null;
                foreach (var typeName in fileTypes)
                {
                    typeSpecificDef = _schema.GetTagsForType(typeName)
                        .FirstOrDefault(t => t.Tag.Equals(enclosingTag, StringComparison.OrdinalIgnoreCase));
                    if (typeSpecificDef is not null) break;
                }

                // Prefer the registered-type def when it has completions; otherwise fall back to the
                // flat def which may carry reference info from a different type's YAML.
                tagDef = typeSpecificDef is not null && HasCompletions(typeSpecificDef)
                    ? typeSpecificDef
                    : _schema.GetTag(enclosingTag);
            }
            else
            {
                tagDef = _schema.GetTag(enclosingTag);
            }

            if (tagDef is null || !HasCompletions(tagDef))
                return Task.FromResult(new CompletionList());
        }

        var partialValue = ExtractPartialValue(line, character);
        var valueIndex = _indexService.Current;
        var tupleSlotIndex = tagDef is not null && TupleValueTypes.Contains(tagDef.ValueType)
            ? ComputeTupleSlotIndex(text, enclosingValueNode!, lineIndex, character)
            : 0;
        var valueCtx = new TagValueCompletionContext(
            uri, valueIndex, _schema, valueDoc, enclosingValueNode!, enclosingTag, enclosingDepth,
            tagDef, partialValue, lineIndex, character, isStoryParserForValue, storyParamSide,
            storyParamPosition, tupleSlotIndex);
        var valueItems = _tagValueRegistry.GetCompletions(valueCtx);
        return Task.FromResult(new CompletionList(valueItems));
    }

    private bool IsStoryParserDocument(string documentUri)
    {
        return _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(documentUri)).Contains("StoryParser");
    }

    private bool IsMultiInstanceFile(string documentUri)
    {
        var fileTypes = _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(documentUri));
        return fileTypes.Any(t => _schema.GetObjectType(t)?.NameTag is not null);
    }

    private bool TryResolveContainingAbilityType(HtmlNode node, out string? abilityTypeName)
    {
        var child = node;
        var parent = child.ParentNode;
        while (parent is { NodeType: HtmlNodeType.Element })
        {
            var tagDef = _schema.GetTag(parent.Name);
            if (tagDef?.ValueType == XmlValueType.GuiActivatedAbilityDefinitionSubObjectList)
            {
                abilityTypeName = "UnitAbility";
                return true;
            }

            if (tagDef?.ValueType == XmlValueType.AbilityDefinitionSubObjectList)
            {
                abilityTypeName = XmlUtility.ToPascalCase(child.Name);
                return true;
            }

            child = parent;
            parent = parent.ParentNode;
        }

        abilityTypeName = null;
        return false;
    }

    private static bool HasCompletions(XmlTagDefinition tagDef)
    {
        return tagDef.ReferenceKind is ReferenceKind.XmlObject or ReferenceKind.HardcodedSet
                   or ReferenceKind.LocalisationKey or ReferenceKind.TextureFile or ReferenceKind.ModelFile
                   or ReferenceKind.AudioFile or ReferenceKind.MapFile or ReferenceKind.BoneName ||
               tagDef.ValueType is XmlValueType.Boolean or XmlValueType.DynamicEnumValue ||
               TupleValueTypes.Contains(tagDef.ValueType);
    }

    /// <summary>
    ///     Counts commas between the enclosing element's content start and the cursor, clamped to 1 —
    ///     see <see cref="TupleValueTypes" /> for why only "before/after the first comma" matters.
    /// </summary>
    private static int ComputeTupleSlotIndex(string text, HtmlNode enclosingNode, int lineIndex, int character)
    {
        var innerStart = enclosingNode.InnerStartIndex;
        var cursorOffset = XmlUtility.PositionToOffset(text, lineIndex, character);
        if (innerStart < 0 || cursorOffset <= innerStart) return 0;

        var span = text[innerStart..Math.Min(cursorOffset, text.Length)];
        var commaCount = 0;
        foreach (var c in span)
            if (c == ',')
                commaCount++;
        return Math.Min(1, commaCount);
    }

    /// <summary>
    ///     Returns true when the cursor sits inside a tag bracket (e.g. on an attribute or tag name).
    ///     Scans leftward: if we hit '&lt;' before '&gt;' the cursor is inside a tag.
    /// </summary>
    private static bool IsCursorInsideTagBracket(string line, int character)
    {
        var bound = Math.Min(character, line.Length);
        for (var i = bound - 1; i >= 0; i--)
        {
            if (line[i] == '>') return false;
            if (line[i] == '<') return true;
        }

        return false;
    }

    /// <summary>
    ///     Returns true when the cursor is in a tag-name context: the character immediately before
    ///     the cursor (ignoring the cursor position itself) is '&lt;', meaning the user just opened
    ///     a new element and wants tag-name suggestions.
    /// </summary>
    private static bool IsTagNameContext(string line, int character)
    {
        var bound = Math.Min(character, line.Length);
        var i = bound - 1;
        while (i >= 0 && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '-'))
            i--;
        return i >= 0 && line[i] == '<';
    }

    /// <summary>Extracts the partial tag name typed after '&lt;' (for prefix filtering).</summary>
    private static string ExtractPartialTagName(string line, int character)
    {
        var bound = Math.Min(character, line.Length);
        var i = bound - 1;
        while (i >= 0 && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '-'))
            i--;
        return line[(i + 1)..bound];
    }

    /// <summary>Extracts the token being typed at the cursor (for prefix filtering).</summary>
    private static string ExtractPartialValue(string line, int character)
    {
        var bound = Math.Min(character, line.Length);
        var i = bound - 1;
        while (i >= 0 && line[i] != '>' && line[i] != ',' && line[i] != ';'
               && line[i] != '|' && line[i] != '/' && line[i] != '\\'
               && !char.IsWhiteSpace(line[i]))
            i--;
        return line[(i + 1)..bound];
    }

    /// <summary>
    ///     Builds the document text truncated at <paramref name="anglePos" /> on the current line,
    ///     so HAP sees only the XML that came BEFORE the partial &lt; the user is typing.
    /// </summary>
    private static string BuildTruncatedText(string[] lines, int lineIndex, int anglePos)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < lineIndex; i++)
            sb.Append(lines[i]).Append('\n');
        if (lineIndex < lines.Length)
        {
            var currentLine = lines[lineIndex].TrimEnd('\r');
            var truncateAt = Math.Max(0, Math.Min(anglePos, currentLine.Length));
            sb.Append(currentLine[..truncateAt]);
        }

        return sb.ToString();
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("xml"),
            TriggerCharacters = new Container<string>(">", "<"),
            ResolveProvider = false
        };
    }
}