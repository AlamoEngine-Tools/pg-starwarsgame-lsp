// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>Observation: a tag that carries Notes was found at the given position.</summary>
public sealed record XmlNotesFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    XmlTagDefinition Tag) : XmlFact(DocumentUri, Line, Column, Length);