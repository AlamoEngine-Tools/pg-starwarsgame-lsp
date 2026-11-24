// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Xml;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Editing;

/// <summary>One file a batch changes: the text it was composed from and the text it comes to.</summary>
/// <param name="Uri">The file's canonical URI.</param>
/// <param name="Original">The text the batch was composed from; null for a file the batch creates.</param>
/// <param name="Current">The composed text.</param>
/// <param name="Version">
///     The version of the open document <paramref name="Original" /> was read from; null when it
///     was read from the saved file.
/// </param>
public sealed record EditBatchFileChange(string Uri, string? Original, string Current, int? Version);

/// <summary>Turns the files a batch changes into the one edit that writes them.</summary>
public static class EditBatchEdits
{
    /// <summary>
    ///     One workspace edit for the whole batch: a create for each new file, then a
    ///     whole-document replacement per changed file (the composed final text). Composing many
    ///     commands' minimal edits would need offset rebasing after every step; replacing each
    ///     touched document with its final text is simpler and, for a deliberate Save, just as good.
    ///     <para>
    ///         Each replacement says what it was composed from, the way the protocol provides for:
    ///         the version of the open document, or none when the saved file was read. The client
    ///         holds the edit to that - it does not write a text composed from the saved file over
    ///         a document that has unsaved changes, nor one composed from a version the document
    ///         has since left.
    ///     </para>
    /// </summary>
    public static WorkspaceEdit Build(IReadOnlyList<EditBatchFileChange> changed)
    {
        var changes = new List<WorkspaceEditDocumentChange>();

        // Creations first, so a freshly created file exists before its content lands.
        foreach (var change in changed)
            if (change.Original is null)
                changes.Add(new WorkspaceEditDocumentChange(new CreateFile
                {
                    Uri = DocumentUri.From(change.Uri),
                    Options = new CreateFileOptions { IgnoreIfExists = true }
                }));

        foreach (var change in changed)
            changes.Add(new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier
                {
                    Uri = DocumentUri.From(change.Uri),
                    Version = change.Version
                },
                Edits = new TextEditContainer(new TextEdit
                {
                    Range = change.Original is null ? new Range(0, 0, 0, 0) : WholeDocumentRange(change.Original),
                    NewText = change.Current
                })
            }));

        return new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) };
    }

    private static Range WholeDocumentRange(string text)
    {
        var (line, col) = new LineOffsetIndex(text).GetPosition(text.Length);
        return new Range(0, 0, line, col);
    }
}