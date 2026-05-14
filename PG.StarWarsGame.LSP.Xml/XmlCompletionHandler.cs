using System.Text;
using HtmlAgilityPack;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml;

public sealed class XmlCompletionHandler : CompletionHandlerBase
{
    private readonly XmlDocumentBuffer _buffer;
    private readonly IXmlValueProposalRegistry _proposals;
    private readonly ISchemaProvider _schema;

    public XmlCompletionHandler(
        XmlDocumentBuffer buffer,
        ISchemaProvider schema,
        IXmlValueProposalRegistry proposals)
    {
        _buffer = buffer;
        _schema = schema;
        _proposals = proposals;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var text = _buffer.Get(request.TextDocument.Uri);
        if (text is null)
            return Task.FromResult(new CompletionList());

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
            var (enclosingType, _) = FindEnclosingTagName(lines, lineIndex, character);
            if (enclosingType is null)
                return Task.FromResult(new CompletionList());
            var tagItems = BuildTagNameCompletions(text, enclosingType, ExtractPartialTagName(line, character));
            return Task.FromResult(new CompletionList(tagItems));
        }

        // Bail out for other cursor-inside-tag positions (attributes, etc.)
        if (IsCursorInsideTagBracket(line, character))
            return Task.FromResult(new CompletionList());

        // Value completion: cursor is inside an element body
        var (enclosingTag, enclosingDepth) = FindEnclosingTagName(lines, lineIndex, character);
        if (enclosingTag is null)
            return Task.FromResult(new CompletionList());

        // Depth 1 = cursor directly inside the root element (a file container, never a field tag).
        // Type containers at any depth are also not field tags.
        if (enclosingDepth == 1 || _schema.GetObjectType(enclosingTag) is not null)
            return Task.FromResult(new CompletionList());

        var tagDef = _schema.GetTag(enclosingTag);
        if (tagDef is null)
            return Task.FromResult(new CompletionList());

        var partialValue = ExtractPartialValue(line, character);
        var valueProposals = _proposals.GetProposals(tagDef.ValueType, tagDef, partialValue);

        var valueItems = valueProposals.Select(p => new CompletionItem
        {
            Label = p.Label,
            Detail = p.Detail,
            InsertText = p.InsertText ?? p.Label,
            Kind = CompletionItemKind.Value
        });

        return Task.FromResult(new CompletionList(valueItems));
    }

    private IEnumerable<CompletionItem> BuildTagNameCompletions(string text, string parentName, string prefix)
    {
        var typeDef = _schema.GetObjectType(parentName);
        if (typeDef is null)
            return [];

        var candidates = _schema.GetTagsForType(parentName);

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