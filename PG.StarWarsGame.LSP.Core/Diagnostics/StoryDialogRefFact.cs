// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a story event carries a <c>Story_Dialog</c> tag (dialog script name without
///     extension) and possibly a <c>Story_Chapter</c> tag. <see cref="Chapter" /> is null when
///     the event has no parseable <c>Story_Chapter</c>; <see cref="ChapterLine" /> is -1 then.
/// </summary>
public sealed record StoryDialogRefFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string DialogName,
    int? Chapter,
    int ChapterLine) : XmlFact(DocumentUri, Line, Column, Length);