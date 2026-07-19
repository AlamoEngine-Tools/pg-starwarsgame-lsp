// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A variant sets a tag whose <c>variantMode</c> is <c>merge</c> and whose base also sets it. This
///     is legal and often deliberate, but it does not replace the base's value the way the line reads -
///     the engine keeps both. Reported so the accumulation is visible at the point it happens.
/// </summary>
/// <param name="TagName">The additive tag.</param>
/// <param name="BaseValue">What the base contributes.</param>
/// <param name="MergedValue">The union the engine will actually use.</param>
public sealed record VariantAdditiveMergeFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string TagName,
    string BaseValue,
    string MergedValue
) : XmlFact(DocumentUri, Line, Column, Length);
