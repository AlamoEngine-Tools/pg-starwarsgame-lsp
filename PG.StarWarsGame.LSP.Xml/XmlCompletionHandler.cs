// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCompletionHandler : CompletionHandlerBase
{
    private const int MaxEventParamSlots = 7;
    private const int MaxRewardParamSlots = 14;

    private static readonly string[] StoryEventStructuralTags =
    [
        "Event_Type", "Event_Filter", "Reward_Type", "Reward_Position",
        "Prereq", "Branch", "Perpetual", "Multiplayer",
        "Story_Dialog", "Story_Chapter", "Story_Tag", "Story_Var",
        "Story_Dialog_Popup", "Story_Dialog_SFX",
        "Inactive_Delay", "Timeout"
    ];

    private static readonly Regex ParamTagPattern =
        new(@"^(Event|Reward)_Param(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IXmlCompletionRegistry _completionRegistry;

    private readonly IEaWXmlContext _eaWXmlContext;
    private readonly IFileHelper _fileHelper;
    private readonly IFileTypeRegistry _fileTypeRegistry;
    private readonly IGameIndexService _indexService;

    private readonly IXmlValueProposalRegistry _proposals;
    private readonly ISchemaProvider _schema;
    private readonly StoryParamValueProposalProvider _storyProposals;
    private readonly IGameWorkspaceHost _workspaceHost;

    public XmlCompletionHandler(
        IGameWorkspaceHost workspaceHost,
        ISchemaProvider schema,
        IXmlValueProposalRegistry proposals,
        IGameIndexService indexService,
        StoryParamValueProposalProvider storyProposals,
        IXmlCompletionRegistry completionRegistry,
        IFileTypeRegistry fileTypeRegistry,
        IFileHelper fileHelper,
        IEaWXmlContext eaWXmlContext)
    {
        _workspaceHost = workspaceHost;
        _schema = schema;
        _proposals = proposals;
        _indexService = indexService;
        _storyProposals = storyProposals;
        _completionRegistry = completionRegistry;
        _fileTypeRegistry = fileTypeRegistry;
        _fileHelper = fileHelper;
        _eaWXmlContext = eaWXmlContext;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        if (!_eaWXmlContext.IsEaWXmlFile(uri))
            return Task.FromResult(new CompletionList());
        if (!_workspaceHost.TryGet(uri, out var doc))
            return Task.FromResult(new CompletionList());
        var text = doc.Text;

        var lines = text.Split('\n');
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
            // Truncate text just before the partial '<' so HAP sees a well-formed document.
            var anglePos = character - prefix.Length - 1;
            var truncatedDoc = XmlUtility.CreateHtmlDocument(BuildTruncatedText(lines, lineIndex, anglePos));
            var enclosingNode = XmlUtility.FindEnclosingElement(truncatedDoc, lineIndex);
            if (enclosingNode is null)
                return Task.FromResult(new CompletionList());

            var enclosingType = enclosingNode.Name;
            var enclosingTagDepth = XmlUtility.GetDepth(enclosingNode);

            // StoryParser context: cursor inside <Event> — use context-aware tag list
            if (string.Equals(enclosingType, "Event", StringComparison.OrdinalIgnoreCase) &&
                IsStoryParserDocument(uri))
            {
                var storyItems = BuildStoryEventTagCompletions(text, lineIndex, character, prefix);
                return Task.FromResult(new CompletionList(storyItems));
            }

            var tagItems = BuildTagNameCompletions(uri, text, enclosingType, enclosingTagDepth, prefix);
            return Task.FromResult(new CompletionList(tagItems));
        }

        // Bail out for other cursor-inside-tag positions (attributes, etc.)
        if (IsCursorInsideTagBracket(line, character))
            return Task.FromResult(new CompletionList());

        // Value completion: cursor is inside an element body
        var valueDoc = XmlUtility.CreateHtmlDocument(text);
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

        // StoryParser context: value completion for Event_Param* / Reward_Param* tags
        var paramMatch = ParamTagPattern.Match(enclosingTag);
        if (paramMatch.Success && IsStoryParserDocument(uri))
        {
            var storyValueItems = BuildStoryParamValueCompletions(text, enclosingTag, paramMatch, lineIndex, character);
            return Task.FromResult(new CompletionList(storyValueItems));
        }

        // Three-tier type-aware tag lookup (mirrors XmlDocumentFactProducer.ResolveTag):
        //   Tier 1 — ability sub-object context (Type56/57): use the ability schema type
        //   Tier 2 — registered file types: use GetTagsForType for each registered type
        //   Tier 3 — flat fallback
        var containingAbilityType = TryResolveContainingAbilityType(enclosingValueNode!);
        var fileTypes = _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(uri));
        XmlTagDefinition? tagDef;
        if (containingAbilityType is not null)
        {
            tagDef = _schema.GetTagsForType(containingAbilityType)
                         .FirstOrDefault(t => t.Tag.Equals(enclosingTag, StringComparison.OrdinalIgnoreCase))
                     ?? _schema.GetTag(enclosingTag);
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

        var partialValue = ExtractPartialValue(line, character);
        var valueProposals = _proposals.GetProposals(tagDef.ValueType, tagDef, partialValue)
            .Concat(_completionRegistry.GetProposals(tagDef, partialValue, _indexService.Current));

        var valueItems = valueProposals.Select(p => new CompletionItem
        {
            Label = p.Label,
            Detail = p.Detail,
            InsertText = p.InsertText ?? p.Label,
            Kind = CompletionItemKind.Value
        });

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

    private IEnumerable<CompletionItem> BuildStoryEventTagCompletions(
        string text, int lineIndex, int character, string prefix)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var ctx = StoryEventCompletionContextReader.Read(doc, lineIndex, character);
        var eventDef = ctx.EventType is not null
            ? _schema.GetEnum("StoryEventType")?.Values
                .FirstOrDefault(v => string.Equals(v.Name, ctx.EventType, StringComparison.OrdinalIgnoreCase))
            : null;
        var rewardDef = ctx.RewardType is not null
            ? _schema.GetEnum("StoryRewardType")?.Values
                .FirstOrDefault(v => string.Equals(v.Name, ctx.RewardType, StringComparison.OrdinalIgnoreCase))
            : null;

        var candidates = new List<string>(StoryEventStructuralTags);

        if (eventDef is not null)
        {
            var paramCount = eventDef.Params is null
                ? MaxEventParamSlots
                : eventDef.Params.Count > 0
                    ? eventDef.Params.Max(p => p.Position) + 1
                    : 0;
            for (var i = 1; i <= paramCount; i++)
                candidates.Add($"Event_Param{i}");
        }

        if (rewardDef is not null)
        {
            var paramCount = rewardDef.Params is null
                ? MaxRewardParamSlots
                : rewardDef.Params.Count > 0
                    ? rewardDef.Params.Max(p => p.Position) + 1
                    : 0;
            for (var i = 1; i <= paramCount; i++)
                candidates.Add($"Reward_Param{i}");
        }

        var existing = CollectExistingEventChildTagNames(doc, lineIndex, character);

        return candidates
            .Where(t => !existing.Contains(t))
            .Where(t => prefix.Length == 0 || t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => new CompletionItem
            {
                Label = t,
                Kind = CompletionItemKind.Property,
                InsertText = $"{t}>$0</{t}>",
                InsertTextFormat = InsertTextFormat.Snippet
            });
    }

    private IEnumerable<CompletionItem> BuildStoryParamValueCompletions(
        string text, string enclosingTag, Match paramMatch, int lineIndex, int character)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var ctx = StoryEventCompletionContextReader.Read(doc, lineIndex, character);
        var side = paramMatch.Groups[1].Value;
        var position = int.Parse(paramMatch.Groups[2].Value);
        var schemaPos = position - 1; // Event_Param1 → position 0

        ParamDefinition? paramDef;
        if (string.Equals(side, "Event", StringComparison.OrdinalIgnoreCase))
        {
            var typeDef = ctx.EventType is not null
                ? _schema.GetEnum("StoryEventType")?.Values
                    .FirstOrDefault(v => string.Equals(v.Name, ctx.EventType, StringComparison.OrdinalIgnoreCase))
                : null;
            paramDef = typeDef?.Params?.FirstOrDefault(p => p.Position == schemaPos);
        }
        else
        {
            var typeDef = ctx.RewardType is not null
                ? _schema.GetEnum("StoryRewardType")?.Values
                    .FirstOrDefault(v => string.Equals(v.Name, ctx.RewardType, StringComparison.OrdinalIgnoreCase))
                : null;
            paramDef = typeDef?.Params?.FirstOrDefault(p => p.Position == schemaPos);
        }

        if (paramDef is null)
            return [];

        var partialValue = ExtractPartialValue(text.Split('\n')[lineIndex].TrimEnd('\r'), character);
        var proposals = _storyProposals.GetProposals(paramDef, partialValue, _indexService.Current);

        return proposals.Select(p => new CompletionItem
        {
            Label = p.Label,
            Detail = p.Detail,
            InsertText = p.InsertText ?? p.Label,
            Kind = CompletionItemKind.Value
        });
    }

    private static HashSet<string> CollectExistingEventChildTagNames(HtmlDocument doc, int lineIndex, int character)
    {
        var cursorLine = lineIndex + 1; // 1-based
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        HtmlNode? enclosingEvent = null;
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 string.Equals(n.Name, "event", StringComparison.OrdinalIgnoreCase)))
            if (node.Line <= cursorLine)
                enclosingEvent = node;
            else
                break;

        if (enclosingEvent is null) return result;
        foreach (var child in enclosingEvent.ChildNodes)
            if (child.NodeType == HtmlNodeType.Element)
                result.Add(child.Name);
        return result;
    }

    private IEnumerable<CompletionItem> BuildTagNameCompletions(
        string uri, string text, string parentName, int depth, string prefix)
    {
        var fileTypes = _fileTypeRegistry.GetTypesForFile(_fileHelper.NormalizeUri(uri));

        IReadOnlyList<XmlTagDefinition> candidates;
        if (!fileTypes.IsEmpty)
        {
            // Only offer field-tag completions at the correct depth for the file's type structure.
            var isMultiInstance = fileTypes.Any(t => _schema.GetObjectType(t)?.NameTag is not null);
            var expectedDepth = isMultiInstance ? 2 : 1;
            if (depth != expectedDepth)
            {
                var subItems = TryBuildSubObjectListCompletions(parentName, prefix);
                if (subItems is not null)
                    return subItems;

                // Allow completions at deeper levels when parent is an ability sub-object type.
                var abilityType = _schema.GetObjectType(XmlUtility.ToPascalCase(parentName));
                if (abilityType is null)
                    return [];
                candidates = _schema.GetTagsForType(abilityType.TypeName);
            }
            else
            {
                var tagsList = new List<XmlTagDefinition>();
                foreach (var typeName in fileTypes)
                    tagsList.AddRange(_schema.GetTagsForType(typeName));
                candidates = tagsList;
            }
        }
        else
        {
            var subItems = TryBuildSubObjectListCompletions(parentName, prefix);
            if (subItems is not null)
                return subItems;

            // Fallback for unregistered files: use element name as type name, with PascalCase conversion
            // for ability class elements (e.g., Lucky_Shot_Attack_Ability → LuckyShotAttackAbility).
            var typeDef = _schema.GetObjectType(parentName)
                       ?? _schema.GetObjectType(XmlUtility.ToPascalCase(parentName));
            if (typeDef is null)
                return [];
            candidates = _schema.GetTagsForType(typeDef.TypeName);
        }

        // Find already-present direct children of the parent element in the current document
        var existingTags = CollectExistingChildTagNames(text, parentName);

        return candidates
            .Where(t => t.MultipleAllowed || !existingTags.Contains(t.Tag))
            .Where(t => prefix.Length == 0 || t.Tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => new CompletionItem
            {
                Label = t.Tag,
                Kind = CompletionItemKind.Property,
                InsertText = $"{t.Tag}>$0</{t.Tag}>",
                InsertTextFormat = InsertTextFormat.Snippet
            });
    }

    private IEnumerable<CompletionItem>? TryBuildSubObjectListCompletions(string parentName, string prefix)
    {
        var parentTagDef = _schema.GetTag(parentName);

        if (parentTagDef?.ValueType == XmlValueType.GuiActivatedAbilityDefinitionSubObjectList)
        {
            const string label = "Unit_Ability";
            if (prefix.Length > 0 && !label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return [];
            return
            [
                new CompletionItem
                {
                    Label = label,
                    Kind = CompletionItemKind.Property,
                    InsertText = "Unit_Ability>\n    <Type>$0</Type>\n</Unit_Ability>",
                    InsertTextFormat = InsertTextFormat.Snippet
                }
            ];
        }

        if (parentTagDef?.ValueType == XmlValueType.AbilityDefinitionSubObjectList)
        {
            return _schema.AllObjectTypes
                .Where(t => t.TypeName.EndsWith("Ability", StringComparison.OrdinalIgnoreCase))
                .Select(t => XmlUtility.ToSnakeCase(t.TypeName))
                .Where(name => prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Property,
                    InsertText = $"{name} Name=\"$1\">\n    $0\n</{name}>",
                    InsertTextFormat = InsertTextFormat.Snippet
                });
        }

        return null;
    }

    private static HashSet<string> CollectExistingChildTagNames(string text, string parentName)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // HtmlAgilityPack lowercases element names; XPath is case-sensitive.
        var parent = doc.DocumentNode.SelectSingleNode($"//{parentName.ToLowerInvariant()}");
        if (parent is null) return result;

        foreach (var child in parent.ChildNodes)
            if (child.NodeType == HtmlNodeType.Element)
                result.Add(child.Name);
        return result;
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

    // ── position helpers ─────────────────────────────────────────────────────

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

    private string? TryResolveContainingAbilityType(HtmlNode node)
    {
        var child = node;
        var parent = child.ParentNode;
        while (parent is { NodeType: HtmlNodeType.Element })
        {
            var tagDef = _schema.GetTag(parent.Name);
            if (tagDef?.ValueType == XmlValueType.GuiActivatedAbilityDefinitionSubObjectList)
                return "UnitAbility";
            if (tagDef?.ValueType == XmlValueType.AbilityDefinitionSubObjectList)
                return XmlUtility.ToPascalCase(child.Name);
            child = parent;
            parent = parent.ParentNode;
        }

        return null;
    }

    private static bool HasCompletions(XmlTagDefinition tagDef) =>
        tagDef.ReferenceKind is ReferenceKind.XmlObject or ReferenceKind.HardcodedSet ||
        tagDef.ValueType is XmlValueType.Boolean or XmlValueType.DynamicEnumValue;

    /// <summary>Extracts the token being typed at the cursor (for prefix filtering).</summary>
    private static string ExtractPartialValue(string line, int character)
    {
        var bound = Math.Min(character, line.Length);
        var i = bound - 1;
        while (i >= 0 && line[i] != '>' && !char.IsWhiteSpace(line[i]))
            i--;
        return line[(i + 1)..bound];
    }

    /// <summary>
    ///     Returns true when the cursor is in a tag-name context: the character immediately before
    ///     the cursor (ignoring the cursor position itself) is '&lt;', meaning the user just opened
    ///     a new element and wants tag-name suggestions.
    /// </summary>
    private static bool IsTagNameContext(string line, int character)
    {
        var bound = Math.Min(character, line.Length);
        // Walk left past any partial identifier characters already typed
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
        // i now points at '<' or whitespace; partial starts at i+1
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
}