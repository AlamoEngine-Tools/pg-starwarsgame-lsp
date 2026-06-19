// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     A single child tag of a shipped (baseline) GameObject, captured so the effective-object
///     merge engine can resolve variants whose base lives in the shipped game data.
/// </summary>
/// <param name="TagName">The tag's element name (e.g. <c>Max_Health</c>).</param>
/// <param name="Value">
///     The tag's trimmed inner-text value (e.g. <c>"100"</c>). Pre-extracted at projection time so the
///     Core merge engine can read scalar and list values without an XML-parser dependency.
/// </param>
/// <param name="Fragment">
///     The tag's verbatim outer XML (e.g. <c>&lt;Max_Health&gt;100&lt;/Max_Health&gt;</c>). Preserves
///     multi-line values and nested structure for faithful reproduction in the effective-object view.
/// </param>
/// <param name="StartLine">0-based line of the tag's opening element in its source file.</param>
[MessagePackObject]
public sealed record BaselineTag(
    [property: Key(0)] string TagName,
    [property: Key(1)] string Value,
    [property: Key(2)] string Fragment,
    [property: Key(3)] int StartLine
);