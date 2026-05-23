// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>Observation: a tag with a non-empty value was found at the given position.</summary>
public sealed record XmlTagValueFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    XmlTagDefinition Tag,
    string RawValue) : XmlFact(DocumentUri, Line, Column, Length);