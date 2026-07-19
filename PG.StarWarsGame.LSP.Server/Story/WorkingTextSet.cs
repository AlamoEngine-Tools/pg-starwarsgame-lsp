// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Story.Writer;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     An in-memory, edit-at-a-time view over document text, seeded lazily from the open-buffer-first
///     <see cref="IDocumentTextSource" />. A batch of story commands composes over one set: each
///     command reads the current text (results of the commands before it), and its edits are
///     <see cref="Apply">applied</see> back into the set before the next command runs. Nothing is
///     written to the workspace - <see cref="Changed" /> reports what a single applyEdit (or an
///     in-memory validation run) would need to touch.
/// </summary>
internal sealed class WorkingTextSet(IDocumentTextSource textSource)
{
    private readonly Dictionary<string, string> _current = new(StringComparer.Ordinal);

    // Buffer text the set was seeded from, keyed by canonical URI. A null value records "the file
    // did not exist when first touched" (a created file diffs against nothing).
    private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

    /// <summary>
    ///     The document's current text - the buffer text on first touch, or the composed result of
    ///     edits applied since. Null when the file is neither readable nor created.
    /// </summary>
    public string? GetText(string uri)
    {
        if (_current.TryGetValue(uri, out var current)) return current;
        var buffer = textSource.GetText(uri)?.Text;
        _original[uri] = buffer; // records the seed (or null: absent) so Changed() can diff against it
        if (buffer is not null) _current[uri] = buffer;
        return buffer;
    }

    /// <summary>Seeds a not-yet-existing file with its skeleton so later commands can read/parse it.</summary>
    public void CreateFile(string uri, string skeleton)
    {
        if (!_original.ContainsKey(uri)) _original[uri] = null;
        _current[uri] = skeleton;
    }

    /// <summary>Applies one file's edits to its current text (no-op when the file isn't present).</summary>
    public void Apply(string uri, IReadOnlyList<StoryTextEdit> edits)
    {
        if (edits.Count == 0) return;
        if (GetText(uri) is not { } text) return;
        _current[uri] = ApplyToString(text, edits);
    }

    /// <summary>
    ///     Every file whose current text differs from its seed, with the seed (null = created).
    ///     This is exactly the set a composed applyEdit must write, or a validation run must re-check.
    /// </summary>
    public IEnumerable<(string Uri, string? Original, string Current)> Changed()
    {
        foreach (var (uri, current) in _current)
        {
            var original = _original.GetValueOrDefault(uri);
            if (!string.Equals(original, current, StringComparison.Ordinal))
                yield return (uri, original, current);
        }
    }

    /// <summary>
    ///     Applies (0-based, end-exclusive) edits to text, last-edit-first so earlier offsets stay
    ///     valid. Edits must be non-overlapping - compose them through
    ///     <see cref="StoryCommandExecutor.SortedNonOverlapping" /> first.
    /// </summary>
    private static string ApplyToString(string text, IReadOnlyList<StoryTextEdit> edits)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                lineStarts.Add(i + 1);

        int Offset(int line, int col)
        {
            var start = line >= 0 && line < lineStarts.Count ? lineStarts[line] : text.Length;
            return Math.Clamp(start + col, 0, text.Length);
        }

        var result = text;
        foreach (var edit in edits
                     .OrderByDescending(e => e.Range.StartLine)
                     .ThenByDescending(e => e.Range.StartColumn))
        {
            var start = Offset(edit.Range.StartLine, edit.Range.StartColumn);
            var end = Offset(edit.Range.EndLine, edit.Range.EndColumn);
            if (start > end) continue;
            result = result[..start] + edit.NewText + result[end..];
        }

        return result;
    }
}