// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Xml.Linq;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Localisation.Rows;

/// <summary>
///     Writes a batch into a translation XML file by changing the text where the batch asks for a
///     change and nowhere else (#339).
///     <para>
///         Serialising the document anew rewrites what the grid does not model: the declaration and
///         every attribute's quotes, the spacing inside a tag, and it drops the comments outside the
///         root. Here each command becomes a replacement at the exact span it is about - a value's
///         text, a key's attribute value, a row's lines - and everything else is the original text.
///     </para>
///     <para>
///         The commands are taken as valid: the caller has already applied them to the parsed
///         document, which is where an index out of range or a key that moved is refused. A file this
///         cannot place a command in is answered with null, and the caller writes it the old way.
///     </para>
/// </summary>
internal static class XmlTranslationSplicer
{
    private const string Localisation = "Localisation";
    private const string TranslationData = "TranslationData";
    private const string Translation = "Translation";
    private const string Key = "Key";
    private const string Language = "Language";

    /// <summary>
    ///     <paramref name="text" /> with <paramref name="commands" /> written into it, or null when
    ///     a command cannot be placed in the text as it stands.
    /// </summary>
    /// <param name="text">The file, in the text editor's format.</param>
    /// <param name="commands">The batch, already known to apply.</param>
    /// <param name="lineEnding">The file's line ending, for a line the batch adds.</param>
    public static string? Splice(string text, IReadOnlyList<LocEditCommandDto> commands, string lineEnding)
    {
        var spans = ParsedXmlDocument.Parse(text).Spans.Elements;
        var root = spans.FirstOrDefault(s => s.Parent is null);
        if (root is null) return null;

        var children = spans.Where(s => s.Parent is not null).ToLookup(s => s.Parent!);
        var rows = children[root]
            .Where(s => s.Name == Localisation)
            .Select(s => new Row(s))
            .ToList();
        var live = new List<Row>(rows);

        foreach (var command in commands)
        {
            // A language exists in this format only where a value does; nothing to write.
            if (command.Kind == "addLanguage") continue;
            if (command.Index is not { } index || index < 0 || index >= live.Count) return null;

            var row = live[index];
            switch (command.Kind)
            {
                case "setCell":
                    if (string.IsNullOrWhiteSpace(command.Language)) return null;
                    row.Set(command.Language, command.Value ?? string.Empty);
                    break;
                case "setKey":
                    row.NewKey = command.Key ?? string.Empty;
                    break;
                case "deleteRow":
                    row.Deleted = true;
                    live.RemoveAt(index);
                    break;
                default:
                    return null;
            }
        }

        var edits = new List<Edit>();
        foreach (var row in rows)
            if (!Place(row, text, children, lineEnding, edits))
                return null;

        return Apply(text, edits);
    }

    /// <summary>Turns one row's pending changes into replacements; false when one has no place in the text.</summary>
    private static bool Place(
        Row row, string text, ILookup<XmlElementSpan, XmlElementSpan> children, string lineEnding, List<Edit> edits)
    {
        if (row.Deleted)
        {
            var (start, end) = WholeLines(text, row.Span.Start, row.Span.End);
            edits.Add(new Edit(start, end - start, string.Empty));
            return true;
        }

        if (row.NewKey is { } key)
        {
            var attribute = row.Span.Node.Attributes.FirstOrDefault(a => a.OriginalName == Key);
            if (attribute is null || attribute.ValueStartIndex <= 0) return false;
            edits.Add(new Edit(attribute.ValueStartIndex, (attribute.Value ?? string.Empty).Length,
                EscapeAttribute(key)));
        }

        if (row.Values.Count == 0) return true;

        var data = children[row.Span].FirstOrDefault(s => s.Name == TranslationData);
        if (data is null) return false;
        var translations = children[data].Where(s => s.Name == Translation).ToList();

        var added = new StringBuilder();
        foreach (var (language, value) in row.Values)
        {
            var existing = translations.FirstOrDefault(t => string.Equals(
                LanguageOf(t), language, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                // A self-closing element has no content span to write a value into.
                if (existing.EndTag is not { } endTag) return false;
                var inner = existing.StartTag.Start + existing.StartTag.Length;
                edits.Add(new Edit(inner, endTag.Start - inner, EscapeText(value)));
                continue;
            }

            // A language the row does not carry yet comes into being as a new element after the
            // ones there, on a line of its own with their indentation.
            if (translations.Count == 0) return false;
            var last = translations[^1];
            added.Append(IndentBefore(text, last.Start, lineEnding))
                .Append('<').Append(Translation).Append(' ').Append(Language).Append("=\"")
                .Append(EscapeAttribute(language)).Append("\">")
                .Append(EscapeText(value))
                .Append("</").Append(Translation).Append('>');
        }

        if (added.Length > 0) edits.Add(new Edit(translations[^1].End, 0, added.ToString()));
        return true;
    }

    private static string? LanguageOf(XmlElementSpan translation)
    {
        var attribute = translation.Node.Attributes.FirstOrDefault(a => a.OriginalName == Language);
        return attribute?.Value is { } value ? HtmlEntity.DeEntitize(value) : null;
    }

    /// <summary>
    ///     The line break and indentation in front of the element at <paramref name="start" />, to
    ///     put in front of a sibling added after it. Empty when the element shares its line with
    ///     something else: the sibling then follows it on that line.
    /// </summary>
    private static string IndentBefore(string text, int start, string lineEnding)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var indent = text.AsSpan(lineStart, start - lineStart);
        return indent.IsWhiteSpace() ? lineEnding + indent.ToString() : string.Empty;
    }

    /// <summary>
    ///     The span widened to its whole lines when nothing else shares them, so a removed row leaves
    ///     no empty line behind; the span itself otherwise.
    /// </summary>
    private static (int Start, int End) WholeLines(string text, int start, int end)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var newline = text.IndexOf('\n', end);
        var lineEnd = newline < 0 ? text.Length : newline;
        var alone = text.AsSpan(lineStart, start - lineStart).IsWhiteSpace()
                    && text.AsSpan(end, lineEnd - end).IsWhiteSpace();
        return alone ? (lineStart, newline < 0 ? text.Length : newline + 1) : (start, end);
    }

    /// <summary>The text with every replacement made; they never overlap, so the last one in the text goes first.</summary>
    private static string Apply(string text, List<Edit> edits)
    {
        var builder = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            builder.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        return builder.ToString();
    }

    private static string EscapeText(string value)
    {
        return new XText(value).ToString();
    }

    /// <summary>Escaped for either kind of quote, since the attribute keeps the quotes it was written with.</summary>
    private static string EscapeAttribute(string value)
    {
        var written = new XAttribute("a", value).ToString();
        return written[3..^1].Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private readonly record struct Edit(int Start, int Length, string Replacement);

    private sealed class Row(XmlElementSpan span)
    {
        public XmlElementSpan Span { get; } = span;
        public bool Deleted { get; set; }
        public string? NewKey { get; set; }

        /// <summary>The values set, by language, in the order the languages were first written to.</summary>
        public List<(string Language, string Value)> Values { get; } = [];

        public void Set(string language, string value)
        {
            var at = Values.FindIndex(v => string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) Values[at] = (Values[at].Language, value);
            else Values.Add((language, value));
        }
    }
}