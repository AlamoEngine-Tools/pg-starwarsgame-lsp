// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>A variant redefines a tag with the same value it would inherit from its base.</summary>
public sealed record VariantRedundantOverrideFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TagName
) : XmlFact(DocumentUri, Line, Column, Length);
