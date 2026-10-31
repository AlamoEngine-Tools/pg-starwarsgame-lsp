// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests.Protocol;

/// <summary>
///     One document a protocol test opens and edits, the way the editor would: opened with a text, changed by
///     whole-text replacement (the server syncs in full), and read back as the diagnostics it settles on.
///     Nothing is written to disk unless a test saves on purpose.
/// </summary>
public sealed class ProtocolDocument
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly LspServerFixture _fixture;
    private int _version;

    private ProtocolDocument(LspServerFixture fixture, string path, string text)
    {
        _fixture = fixture;
        Path = path;
        Uri = DocumentUri.FromFileSystemPath(path);
        Text = text;
    }

    public string Path { get; }
    public DocumentUri Uri { get; }
    public string Text { get; private set; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];

    /// <summary>The text's lines, without line endings.</summary>
    public string[] Lines => Text.Replace("\r\n", "\n").Split('\n');

    /// <summary>Opens <paramref name="path" /> with <paramref name="text" />, or its content on disk.</summary>
    public static async Task<ProtocolDocument> OpenAsync(LspServerFixture fixture, string path, string? text = null,
        string languageId = "xml")
    {
        await WaitForScanAsync(fixture);
        var document = new ProtocolDocument(fixture, path, text ?? await File.ReadAllTextAsync(path));
        var settled = fixture.WaitForSettledDiagnosticsAsync(document.Uri, Quiet, Timeout);
        fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = document.Uri, LanguageId = languageId, Version = ++document._version, Text = document.Text
            }
        });
        document.Diagnostics = Published(await settled, document.Path);
        return document;
    }

    // Every open document is published, an empty list included: no publish at all would let every
    // "no such diagnostic" assertion pass for nothing.
    private static List<Diagnostic> Published(PublishDiagnosticsParams? published, string path)
    {
        if (published is null)
            throw new InvalidOperationException($"No diagnostics were published for {path} within {Timeout}.");
        return [.. published.Diagnostics];
    }

    public static async Task WaitForScanAsync(LspServerFixture fixture)
    {
        if (await Task.WhenAny(fixture.ScanCompleted, Task.Delay(TimeSpan.FromMinutes(4))) != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$The workspace scan did not complete within 4 min.");
    }

    /// <summary>Replaces the whole text, as an unsaved edit, and waits for the diagnostics to settle.</summary>
    public async Task ReplaceAsync(string text)
    {
        Text = text;
        var settled = _fixture.WaitForSettledDiagnosticsAsync(Uri, Quiet, Timeout);
        _fixture.Client.DidChangeTextDocument(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = Uri, Version = ++_version },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent
            {
                Text = text
            })
        });
        Diagnostics = Published(await settled, Path);
    }

    /// <summary>Replaces one line (1-based), keeping the others.</summary>
    public Task ReplaceLineAsync(int line, string text)
    {
        var lines = Lines;
        lines[line - 1] = text;
        return ReplaceAsync(string.Join("\n", lines));
    }

    public void Close()
    {
        // Waits for the server to clear the closed document, as the editor's close would leave it: a
        // re-open in the same moment is otherwise folded into one publish pass and published not at all.
        var cleared = _fixture.WaitForDiagnosticsAsync(Uri, p => !p.Diagnostics.Any(), TimeSpan.FromSeconds(10));
        _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier(Uri)
        });
        cleared.GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Runs <paramref name="change" /> - another document's edit - and returns whether this document was
    ///     published again meanwhile; its diagnostics are then those it settled on. Listens from before the
    ///     change, since both are published in one pass.
    /// </summary>
    public async Task<bool> RepublishedAfterAsync(Func<Task> change, TimeSpan within)
    {
        var listening = _fixture.WaitForSettledDiagnosticsAsync(Uri, Quiet, within);
        await change();
        var settled = await listening;
        if (settled is null) return false;
        Diagnostics = [.. settled.Diagnostics];
        return true;
    }

    /// <summary>The 0-based position of the <paramref name="occurrence" />-th <paramref name="needle" /> on a 1-based line.</summary>
    public Position At(int line, string needle, int occurrence = 1, int offset = 0)
    {
        var text = Lines[line - 1];
        var index = -1;
        for (var i = 0; i < occurrence; i++)
            index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException($"'{needle}' not on line {line}: {text}");
        return new Position(line - 1, index + offset);
    }

    /// <summary>The diagnostics whose code starts with <paramref name="prefix" />.</summary>
    public List<Diagnostic> WithCode(string prefix)
    {
        return [.. Diagnostics.Where(d => d.Code?.String?.StartsWith(prefix, StringComparison.Ordinal) == true)];
    }

    /// <summary>The diagnostics within 20 lines of <paramref name="line" />, for a failure message.</summary>
    public static string Near(ProtocolDocument document, int line)
    {
        var near = document.Diagnostics.Where(d => Math.Abs(d.Range.Start.Line - line) <= 20)
            .Select(d => $"{d.Range.Start.Line}: {d.Code?.String} {d.Message}");
        return $"Line {line}: {document.Lines[line].Trim()}; {document.Diagnostics.Count} in all; near: " +
               string.Join(" | ", near);
    }

    /// <summary>
    ///     Waits until the document's diagnostics satisfy <paramref name="wanted" /> - for findings that
    ///     arrive late, as the Lua analyzer's do - and returns whether they did within the time.
    /// </summary>
    public async Task<bool> UntilAsync(Func<IReadOnlyList<Diagnostic>, bool> wanted, TimeSpan within)
    {
        if (wanted(Diagnostics)) return true;
        var published = await _fixture.WaitForDiagnosticsAsync(Uri, p => wanted([.. p.Diagnostics]), within);
        if (published is null) return false;
        Diagnostics = [.. published.Diagnostics];
        return true;
    }

    /// <summary>The hover text at a position as a reader sees it - Markdown emphasis and code marks removed - or empty.</summary>
    public async Task<string> HoverAsync(Position position)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var hover = await _fixture.Client.RequestHover(new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(Uri), Position = position
        }, cts.Token);
        if (hover?.Contents is not { } contents) return string.Empty;
        var text = contents.HasMarkupContent
            ? contents.MarkupContent!.Value
            : string.Join("\n", contents.MarkedStrings?.Select(m => m.Value) ?? []);
        return text.Replace("**", "").Replace("`", "");
    }

    /// <summary>The completion labels and details at a position.</summary>
    public async Task<List<CompletionItem>> CompleteAsync(Position position)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var items = await _fixture.Client.RequestCompletion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(Uri), Position = position
        }, cts.Token);
        return [.. items ?? []];
    }

    /// <summary>Saves the current text to disk and tells the server, as the editor's save does.</summary>
    public async Task SaveAsync()
    {
        await File.WriteAllTextAsync(Path, Text);
        _fixture.Client.DidSaveTextDocument(new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier(Uri), Text = Text
        });
    }

    /// <summary>A file of the workspace by its path relative to the root, whatever the case on disk.</summary>
    public static string Find(string root, string relative)
    {
        var path = root;
        foreach (var part in relative.Split('\\', '/'))
        {
            var next = Directory.Exists(path)
                ? Directory.EnumerateFileSystemEntries(path)
                    .FirstOrDefault(e => string.Equals(System.IO.Path.GetFileName(e), part,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            path = next ?? System.IO.Path.Combine(path, part);
        }

        return path;
    }

    /// <summary>The code actions offered for <paramref name="diagnostic" />, as the lightbulb lists them.</summary>
    public async Task<List<CodeAction>> CodeActionsAsync(Diagnostic diagnostic)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var actions = await _fixture.Client.RequestCodeAction(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Range = diagnostic.Range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(diagnostic) }
        }, cts.Token);
        return [.. (actions ?? []).Where(a => a.IsCodeAction).Select(a => a.CodeAction!)];
    }

    /// <summary>Applies a code action's edit to this document and sends the result, as accepting it would.</summary>
    public async Task ApplyAsync(CodeAction action)
    {
        var edits = new List<TextEdit>();
        if (action.Edit?.Changes is { } changes)
            foreach (var (uri, list) in changes)
                if (DocumentUri.From(uri.ToString()) == Uri || SameDocument(uri))
                    edits.AddRange(list);
        if (action.Edit?.DocumentChanges is { } documentChanges)
            foreach (var change in documentChanges)
                if (change.IsTextDocumentEdit && SameDocument(change.TextDocumentEdit!.TextDocument.Uri))
                    edits.AddRange(change.TextDocumentEdit.Edits.Select(e => (TextEdit)e));
        if (edits.Count == 0)
            throw new InvalidOperationException($"'{action.Title}' carries no edit to this document.");
        await ReplaceAsync(Apply(Text, edits));
    }

    private bool SameDocument(DocumentUri uri)
    {
        return string.Equals(uri.ToString(), Uri.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Applies edits to a text, last first so earlier offsets stay valid.</summary>
    public static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts.Add(i + 1);
        int Offset(Position p) => Math.Min(text.Length, starts[Math.Min(p.Line, starts.Count - 1)] + p.Character);
        foreach (var edit in edits.OrderByDescending(e => Offset(e.Range.Start)))
        {
            var start = Offset(edit.Range.Start);
            var end = Offset(edit.Range.End);
            text = text[..start] + edit.NewText + text[end..];
        }

        return text;
    }
}
