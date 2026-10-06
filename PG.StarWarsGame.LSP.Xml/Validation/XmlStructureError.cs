// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>A structural finding: 0-based position, the span it covers, and what it is.</summary>
public sealed record XmlStructureError(
    int Line,
    int Column,
    string Reason,
    XmlStrictnessCategory Category = XmlStrictnessCategory.StrictOnly,
    int Length = 1);