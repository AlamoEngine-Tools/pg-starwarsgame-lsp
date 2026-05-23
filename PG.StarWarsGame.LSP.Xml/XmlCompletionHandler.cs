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
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Completion;
using PG.StarWarsGame.LSP.Xml.Parsing;

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
        IFileTypeRegistry fileTypeRegistry)
    {
        _workspaceHost = workspaceHost;
        _schema = schema;
        _proposals = proposals;
        _indexService = indexService;
        _storyProposals = storyProposals;
        _completionRegistry = completionRegistry;
        _fileTypeRegistry = fileTypeRegistry;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
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
            var (enclosingType, enclosingTagDepth) = FindEnclosingTagName(lines, lineIndex, character);
            if (enclosingType is null)
                return Task.FromResult(new CompletionList());

            // StoryParser context: cursor inside <Event> — use context-aware tag list
            if (string.Equals(enclosingType, "Event", StringComparison.OrdinalIgnoreCase) &&
                IsStoryParserDocument(uri))
            {
                var prefix = ExtractPartialTagName(line, character);
                var storyItems = BuildStoryEventTagCompletions(text, lineIndex, character, prefix);
                return Task.FromResult(new CompletionList(storyItems));
            }

            var tagItems = BuildTagNameCompletions(uri, text, enclosingType, enclosingTagDepth,
                ExtractPartialTagName(line, character));
            return Task.FromResult(new CompletionList(tagItems));
        }

        // Bail out for other cursor-inside-tag positions (attributes, etc.)
        if (IsCursorInsideTagBracket(line, character))
            return Task.FromResult(new CompletionList());

        // Value completion: cursor is inside an element body
        var (enclosingTag, enclosingDepth) = FindEnclosingTagName(lines, lineIndex, character);
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

        var tagDef = _schema.GetTag(enclosingTag);
        if (tagDef is null)
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
        var normalized = XmlGameDocumentParser.NormalizeDocumentUri(documentUri);
        return _fileTypeRegistry.GetTypesForFile(normalized).Contains("StoryParser");
    }

    private bool IsMultiInstanceFile(string documentUri)
    {
        var normalized = XmlGameDocumentParser.NormalizeDocumentUri(documentUri);
        var fileTypes = _fileTypeRegistry.GetTypesForFile(normalized);
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
                InsertText = $"{t}></{t}>"
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
        var normalized = XmlGameDocumentParser.NormalizeDocumentUri(uri);
        var fileTypes = _fileTypeRegistry.GetTypesForFile(normalized);

        IReadOnlyList<XmlTagDefinition> candidates;
        if (!fileTypes.IsEmpty)
        {
            // Only offer field-tag completions at the correct depth for the file's type structure.
            var isMultiInstance = fileTypes.Any(t => _schema.GetObjectType(t)?.NameTag is not null);
            var expectedDepth = isMultiInstance ? 2 : 1;
            if (depth != expectedDepth)
                return [];

            var tagsList = new List<XmlTagDefinition>();
            foreach (var typeName in fileTypes)
                tagsList.AddRange(_schema.GetTagsForType(typeName));
            candidates = tagsList;
        }
        else
        {
            // Fallback for unregistered files: use element name as type name.
            var typeDef = _schema.GetObjectType(parentName);
            if (typeDef is null)
                return [];
            candidates = _schema.GetTagsForType(parentName);
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
                InsertText = $"{t.Tag}></{t.Tag}>"
            });
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

    /// <summary>
    ///     Scans backwards from the cursor to find the innermost open element name and its stack depth.
    ///     Opening tags push, closing tags pop, self-closing tags do not push.
    ///     Returns (name, depth) where depth = 1 means cursor is directly inside the document root element.
    /// </summary>
    private static (string? name, int depth) FindEnclosingTagName(string[] lines, int lineIndex, int character)
    {
        // Collect all text up to the cursor
        var sb = new StringBuilder();
        for (var i = 0; i < lineIndex; i++)
            sb.Append(lines[i]).Append('\n');
        var currentLine = lines[lineIndex].TrimEnd('\r');
        sb.Append(currentLine[..Math.Min(character, currentLine.Length)]);

        var text = sb.ToString();

        // Walk forward through all tags; maintain a stack
        var stack = new Stack<string>();
        var pos = 0;
        while (pos < text.Length)
        {
            var openBracket = text.IndexOf('<', pos);
            if (openBracket < 0) break;

            var closeBracket = text.IndexOf('>', openBracket);
            if (closeBracket < 0) break;

            var inner = text[(openBracket + 1)..closeBracket].Trim();
            var selfClose = inner.EndsWith('/');
            if (selfClose)
                inner = inner[..^1].Trim();

            if (inner.StartsWith('/'))
            {
                // Closing tag
                var name = ExtractFirstWord(inner[1..]);
                if (stack.Count > 0 && string.Equals(stack.Peek(), name, StringComparison.OrdinalIgnoreCase))
                    stack.Pop();
            }
            else if (!inner.StartsWith('!') && !inner.StartsWith('?') && !selfClose)
            {
                // Opening tag (not comment, not PI, not self-closing)
                var name = ExtractFirstWord(inner);
                if (!string.IsNullOrEmpty(name))
                    stack.Push(name);
            }

            pos = closeBracket + 1;
        }

        return (stack.Count > 0 ? stack.Peek() : null, stack.Count);
    }

    private static string ExtractFirstWord(string s)
    {
        var i = 0;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '-'))
            i++;
        return s[..i];
    }

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
}