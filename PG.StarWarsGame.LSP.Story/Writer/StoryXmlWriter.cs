// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Writer;

/// <summary>A single replacement over the current document text (0-based, end-exclusive).</summary>
public sealed record StoryTextEdit(StorySourceRange Range, string NewText);

/// <summary>
///     Produces minimal text edits for story-event mutations. Everything outside the touched
///     lines stays byte-identical — comments, blank lines, and the file's original indentation
///     style are preserved. Tags the writer inserts land at their canonical position
///     (<see cref="StoryEventTagOrder" />); existing tags are never reordered (that would fight
///     minimality — the tag-order diagnostic already reports legacy violations).
///     The writer is deliberately dumb: existence checks, duplicate-name rules and layer guards
///     are the command handler's business. All operations assume the ranges on the given
///     <see cref="StoryEvent" />/<see cref="StoryThread" /> were parsed from exactly the given
///     text.
/// </summary>
public static class StoryXmlWriter
{
    // ── Event blocks ─────────────────────────────────────────────────────────

    /// <summary>Appends a new event block after the last event (or before the root close tag).</summary>
    public static IReadOnlyList<StoryTextEdit> CreateEvent(
        string text, StoryThread thread, string name, string? eventType, string? rewardType,
        IReadOnlyList<(string Tag, string Value)>? extraTags = null)
    {
        var eol = DetectEol(text);
        var lastEvent = thread.Events.LastOrDefault();
        var indent = lastEvent is not null
            ? IndentAt(text, lastEvent.Range.StartLine, lastEvent.Range.StartColumn)
            : "\t";
        var childIndent = lastEvent is not null && lastEvent.Tags.Count > 0
            ? IndentAt(text, lastEvent.Tags[0].ValueRange.StartLine,
                LineIndentLength(text, lastEvent.Tags[0].ValueRange.StartLine))
            : indent + "\t";

        var block = new StringBuilder();
        block.Append(eol);
        block.Append(indent).Append("<Event Name=\"").Append(Escape(name)).Append("\">").Append(eol);
        if (eventType is not null)
            block.Append(childIndent).Append("<Event_Type>").Append(Escape(eventType)).Append("</Event_Type>")
                .Append(eol);
        if (rewardType is not null)
            block.Append(childIndent).Append("<Reward_Type>").Append(Escape(rewardType)).Append("</Reward_Type>")
                .Append(eol);
        foreach (var (tag, value) in extraTags ?? [])
            block.Append(childIndent).Append('<').Append(tag).Append('>').Append(Escape(value))
                .Append("</").Append(tag).Append('>').Append(eol);
        block.Append(indent).Append("</Event>").Append(eol);

        var insertLine = lastEvent is not null
            ? lastEvent.Range.EndLine + 1
            : RootCloseLine(text);
        return [Insert(insertLine, 0, block.ToString())];
    }

    /// <summary>Removes the event block's full lines.</summary>
    public static IReadOnlyList<StoryTextEdit> DeleteEvent(string text, StoryEvent storyEvent)
    {
        return
        [
            new StoryTextEdit(
                new StorySourceRange(storyEvent.Range.StartLine, 0, storyEvent.Range.EndLine + 1, 0), "")
        ];
    }

    // ── Single-value tags ────────────────────────────────────────────────────

    /// <summary>
    ///     Sets, inserts (at the canonical position) or — with a <see langword="null" /> value —
    ///     removes a single-value child tag.
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> SetTagValue(
        string text, StoryEvent storyEvent, string tagName, string? value)
    {
        var existing = storyEvent.Tags.FirstOrDefault(t =>
            string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (value is not null)
                return [new StoryTextEdit(existing.ValueRange, Escape(value))];
            return [DeleteLines(existing.ValueRange.StartLine, existing.ValueRange.EndLine)];
        }

        if (value is null) return [];
        return [InsertTagLine(text, storyEvent, tagName, value)];
    }

    /// <summary>
    ///     Applies a batch of param-slot changes (<paramref name="prefix" /> is
    ///     <c>Event_Param</c> or <c>Reward_Param</c>; positions are 0-based schema slots; a null
    ///     value removes the slot). Inserts landing on the same anchor are merged so the edits
    ///     never overlap.
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> SetParams(
        string text, StoryEvent storyEvent, string prefix,
        IReadOnlyList<(int Position, string? Value)> values)
    {
        var edits = new List<StoryTextEdit>();
        foreach (var (position, value) in values.OrderBy(v => v.Position))
            edits.AddRange(SetTagValue(text, storyEvent, $"{prefix}{position + 1}", value));
        return MergeSamePointInserts(edits);
    }

    /// <summary>
    ///     Removes the trigger or reward block wholesale: the <c>{prefix}_Type</c> line, every
    ///     <c>{prefix}_ParamN</c> line, and — for the trigger — the <c>Event_Filter</c> line, as
    ///     one atomic edit set. The graph editor's immutable-type rule: changing a type means
    ///     clearing it (params included, so nothing stale survives) and attaching a new one.
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> ClearTypeBlock(
        string text, StoryEvent storyEvent, string prefix)
    {
        var isEvent = string.Equals(prefix, "Event", StringComparison.OrdinalIgnoreCase);
        var typeTag = prefix + "_Type";
        var paramPrefix = prefix + "_Param";

        var edits = new List<StoryTextEdit>();
        foreach (var tag in storyEvent.Tags)
        {
            var remove = string.Equals(tag.Name, typeTag, StringComparison.OrdinalIgnoreCase)
                         || IsParamTag(tag.Name, paramPrefix)
                         || (isEvent && string.Equals(tag.Name, "Event_Filter", StringComparison.OrdinalIgnoreCase));
            if (remove)
                edits.Add(DeleteLines(tag.ValueRange.StartLine, tag.ValueRange.EndLine));
        }

        return edits;

        static bool IsParamTag(string name, string paramPrefix)
        {
            return name.Length > paramPrefix.Length
                   && name.StartsWith(paramPrefix, StringComparison.OrdinalIgnoreCase)
                   && name[paramPrefix.Length..].All(char.IsDigit);
        }
    }

    // ── Prereqs ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Adds a prereq token: into the AND-line at <paramref name="groupIndex" />, or as a new
    ///     OR-line when the index is null or out of range.
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> AddPrereq(
        string text, StoryEvent storyEvent, int? groupIndex, string token)
    {
        if (groupIndex is { } index && index >= 0 && index < storyEvent.PrereqGroups.Count)
        {
            var group = storyEvent.PrereqGroups[index];
            return [Insert(group.Range.EndLine, group.Range.EndColumn, " " + Escape(token))];
        }

        return InsertNewPrereqLine(text, storyEvent, token);
    }

    /// <summary>
    ///     Adds a new AND-line with every token joined in one atomic edit — the graph editor's
    ///     "wire an AND-junction's accumulated sources to a target" gesture, which knows every
    ///     token up front and would otherwise need one <see cref="AddPrereq" /> call per token
    ///     while guessing at the new group's index for every call after the first.
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> AddPrereqGroup(
        string text, StoryEvent storyEvent, IReadOnlyList<string> tokens)
    {
        return InsertNewPrereqLine(text, storyEvent, string.Join(" ", tokens));
    }

    /// <summary>
    ///     Adds one new OR-line per token in one atomic edit set — the graph editor's "wire an
    ///     OR-junction's accumulated sources to a target" gesture. The per-token counterpart of
    ///     <see cref="AddPrereqGroup" />: N separate <see cref="AddPrereq" /> commands would each
    ///     be computed against the same original text and race the detached applyEdits.
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> AddPrereqAlternatives(
        string text, StoryEvent storyEvent, IReadOnlyList<string> tokens)
    {
        var edits = new List<StoryTextEdit>();
        foreach (var token in tokens)
            edits.AddRange(InsertNewPrereqLine(text, storyEvent, token));
        // Every line anchors at the same insertion point (after the last existing prereq, or the
        // canonical position) — merging turns them into a single non-overlapping insert.
        return MergeSamePointInserts(edits);
    }

    /// <summary>
    ///     Inserts a new <c>&lt;Prereq&gt;</c> line after the last existing one (or at the
    ///     canonical position if there isn't one). <paramref name="rawValue" /> is escaped exactly
    ///     once here — callers must not pre-escape, or a token containing e.g. <c>&amp;</c> would
    ///     be double-escaped.
    /// </summary>
    private static IReadOnlyList<StoryTextEdit> InsertNewPrereqLine(
        string text, StoryEvent storyEvent, string rawValue)
    {
        var lastGroup = storyEvent.PrereqGroups.LastOrDefault();
        if (lastGroup is not null)
        {
            var eol = DetectEol(text);
            var indent = IndentAt(text, lastGroup.Range.StartLine, LineIndentLength(text, lastGroup.Range.StartLine));
            return
            [
                Insert(lastGroup.Range.EndLine + 1, 0,
                    indent + "<Prereq>" + Escape(rawValue) + "</Prereq>" + eol)
            ];
        }

        return [InsertTagLine(text, storyEvent, "Prereq", rawValue)];
    }

    /// <summary>
    ///     Removes a prereq token: from the given AND-line, or — with a null
    ///     <paramref name="groupIndex" /> — from every line containing it (the edge-removal
    ///     gesture doesn't know line indices). Removing the last token removes the whole line.
    ///     Unknown tokens produce no edits (the handler validates first).
    /// </summary>
    public static IReadOnlyList<StoryTextEdit> RemovePrereq(
        string text, StoryEvent storyEvent, int? groupIndex, string token)
    {
        if (groupIndex is null)
        {
            var edits = new List<StoryTextEdit>();
            for (var i = 0; i < storyEvent.PrereqGroups.Count; i++)
                edits.AddRange(RemovePrereq(text, storyEvent, i, token));
            return edits;
        }

        return RemovePrereqFromGroup(storyEvent, groupIndex.Value, token);
    }

    private static IReadOnlyList<StoryTextEdit> RemovePrereqFromGroup(
        StoryEvent storyEvent, int groupIndex, string token)
    {
        if (groupIndex < 0 || groupIndex >= storyEvent.PrereqGroups.Count) return [];
        var group = storyEvent.PrereqGroups[groupIndex];
        var position = -1;
        for (var i = 0; i < group.Tokens.Count; i++)
            if (string.Equals(group.Tokens[i].Text, token, StringComparison.OrdinalIgnoreCase))
            {
                position = i;
                break;
            }

        if (position < 0) return [];

        if (group.Tokens.Count == 1)
            return [DeleteLines(group.Range.StartLine, group.Range.EndLine)];

        // Swallow the separator towards the neighbouring token so no double space remains.
        var target = group.Tokens[position].Range;
        var range = position == 0
            ? new StorySourceRange(target.StartLine, target.StartColumn,
                group.Tokens[1].Range.StartLine, group.Tokens[1].Range.StartColumn)
            : new StorySourceRange(group.Tokens[position - 1].Range.EndLine,
                group.Tokens[position - 1].Range.EndColumn, target.EndLine, target.EndColumn);
        return [new StoryTextEdit(range, "")];
    }

    // ── Mechanics ────────────────────────────────────────────────────────────

    private static StoryTextEdit InsertTagLine(string text, StoryEvent storyEvent, string tagName, string value)
    {
        var eol = DetectEol(text);
        var (anchorLine, indent) = InsertionPoint(text, storyEvent, tagName);
        return Insert(anchorLine + 1, 0, indent + "<" + tagName + ">" + Escape(value) + "</" + tagName + ">" + eol);
    }

    /// <summary>
    ///     Where a new tag goes: after the last existing tag whose canonical rank precedes (or,
    ///     for same-rank param slots, whose position precedes) the new tag's — so emitted tags
    ///     respect the documented order without reordering anything that already exists.
    /// </summary>
    private static (int AnchorLine, string Indent) InsertionPoint(string text, StoryEvent storyEvent, string tagName)
    {
        var targetRank = StoryEventTagOrder.RankOf(tagName);
        var targetKey = ParamPosition(tagName);

        StoryEventTag? anchor = null;
        foreach (var tag in storyEvent.Tags)
        {
            if (targetRank is not null)
            {
                var rank = StoryEventTagOrder.RankOf(tag.Name);
                if (rank is null) continue;
                if (rank > targetRank) continue;
                if (rank == targetRank && ParamPosition(tag.Name) > targetKey) continue;
            }

            anchor = tag;
        }

        if (anchor is not null)
            return (anchor.ValueRange.EndLine,
                IndentAt(text, anchor.ValueRange.StartLine, LineIndentLength(text, anchor.ValueRange.StartLine)));

        // Empty block (or everything ranks after the new tag): insert right below the open tag.
        var indent = IndentAt(text, storyEvent.Range.StartLine, storyEvent.Range.StartColumn) + "\t";
        if (storyEvent.Tags.Count > 0)
            indent = IndentAt(text, storyEvent.Tags[0].ValueRange.StartLine,
                LineIndentLength(text, storyEvent.Tags[0].ValueRange.StartLine));
        return (storyEvent.Range.StartLine, indent);
    }

    private static int ParamPosition(string tagName)
    {
        var digits = tagName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray();
        return digits.Length > 0 && int.TryParse(digits, out var n) ? n : 0;
    }

    private static IReadOnlyList<StoryTextEdit> MergeSamePointInserts(List<StoryTextEdit> edits)
    {
        var result = new List<StoryTextEdit>();
        foreach (var edit in edits)
        {
            var previous = result.LastOrDefault();
            if (previous is not null
                && IsInsert(previous.Range) && IsInsert(edit.Range)
                && previous.Range.StartLine == edit.Range.StartLine
                && previous.Range.StartColumn == edit.Range.StartColumn)
                result[^1] = previous with { NewText = previous.NewText + edit.NewText };
            else
                result.Add(edit);
        }

        return result;

        static bool IsInsert(StorySourceRange r)
        {
            return r.StartLine == r.EndLine && r.StartColumn == r.EndColumn;
        }
    }

    private static StoryTextEdit Insert(int line, int column, string newText)
    {
        return new StoryTextEdit(new StorySourceRange(line, column, line, column), newText);
    }

    private static StoryTextEdit DeleteLines(int startLine, int endLine)
    {
        return new StoryTextEdit(new StorySourceRange(startLine, 0, endLine + 1, 0), "");
    }

    private static string DetectEol(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    /// <summary>The raw leading text of the line up to the given column (preserves tabs/spaces).</summary>
    private static string IndentAt(string text, int line, int column)
    {
        var start = LineStartOffset(text, line);
        var end = Math.Min(start + column, text.Length);
        return text[start..end];
    }

    private static int LineIndentLength(string text, int line)
    {
        var offset = LineStartOffset(text, line);
        var length = 0;
        while (offset + length < text.Length && (text[offset + length] == ' ' || text[offset + length] == '\t'))
            length++;
        return length;
    }

    private static int LineStartOffset(string text, int line)
    {
        var offset = 0;
        for (var i = 0; i < line; i++)
        {
            var next = text.IndexOf('\n', offset);
            if (next < 0) return text.Length;
            offset = next + 1;
        }

        return offset;
    }

    /// <summary>The line holding the root element's close tag (insertion point for a first event).</summary>
    private static int RootCloseLine(string text)
    {
        var index = text.LastIndexOf("</", StringComparison.Ordinal);
        if (index < 0) return CountLines(text);
        var line = 0;
        for (var i = 0; i < index; i++)
            if (text[i] == '\n')
                line++;
        return line;
    }

    private static int CountLines(string text)
    {
        return text.Count(c => c == '\n');
    }

    private static string Escape(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}