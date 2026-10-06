// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>A structural finding: 0-based position, the span it covers, what it is, and its repair if one exists.</summary>
public sealed record XmlStructureError(
    int Line,
    int Column,
    string Reason,
    XmlStrictnessCategory Category = XmlStrictnessCategory.StrictOnly,
    int Length = 1,
    XmlRepair? Repair = null);

/// <summary>One text change, by character offset into the text the finding came from.</summary>
public sealed record XmlTextEdit(int Start, int Length, string NewText);

/// <summary>
///     The fix for one finding: a title for the lightbulb and the edits, computed against the exact
///     text the finding was read from.
/// </summary>
public sealed record XmlRepair(string Title, IReadOnlyList<XmlTextEdit> Edits);