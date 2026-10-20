// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: the document has an XML well-formedness violation (mismatched tag, unclosed tag, malformed attribute,
///     etc.).
/// </summary>
public sealed record XmlStructureFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    CatalogueText Reason,
    XmlStrictnessCategory Category = XmlStrictnessCategory.StrictOnly) : XmlFact(DocumentUri, Line, Column, Length);