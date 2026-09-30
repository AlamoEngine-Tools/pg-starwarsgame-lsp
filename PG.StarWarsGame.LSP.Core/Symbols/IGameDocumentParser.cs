// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

public interface IGameDocumentParser
{
    bool CanParse(string fileExtension);

    ValueTask<DocumentIndex> ParseAsync(
        string documentUri,
        string text,
        int version,
        CancellationToken ct);

    /// <summary>
    ///     Anything <see cref="ParseAsync" /> published OUTSIDE its returned
    ///     <see cref="DocumentIndex" />, in a form that can be persisted beside the document's
    ///     cached index and handed back to <see cref="RestoreParserState" /> later. Null when the
    ///     parser has no such state, which is the normal case.
    /// </summary>
    /// <remarks>
    ///     A document served from a persisted index snapshot is never parsed, so a parser whose
    ///     work includes a WRITE somewhere else silently loses that write for the whole session.
    ///     That is not hypothetical: the Lua parser fills the annotation repository that
    ///     completion, hover and inlay hints read workspace-wide, and on a layered mod most
    ///     indexed files are Lua - so a warm start dropped nearly every annotation in the
    ///     workspace, visible only as functions quietly losing their documented parameters.
    ///     <para>
    ///         The bytes are opaque to the caller. Their layout is the parser's own business, but
    ///         it is CACHED, so a change to it must invalidate:
    ///         <see cref="Caching.ProjectIndexSnapshot.CurrentSchemaVersion" /> has to be bumped
    ///         alongside, exactly as for a change to what the parser emits.
    ///     </para>
    /// </remarks>
    byte[]? CaptureParserState(string documentUri)
    {
        return null;
    }

    /// <summary>
    ///     Replays the side effects of a parse that did not happen, from bytes previously returned
    ///     by <see cref="CaptureParserState" />. Must not throw: unreadable state means a degraded
    ///     session, while an exception here would fail the whole workspace scan.
    /// </summary>
    void RestoreParserState(string documentUri, byte[] state)
    {
    }
}