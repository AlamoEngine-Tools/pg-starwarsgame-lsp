// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>A variant sets a tag whose <c>variantMode</c> is <c>ignored</c>; the engine drops it.</summary>
public sealed record VariantIgnoredOverrideFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TagName
) : XmlFact(DocumentUri, Line, Column, Length);