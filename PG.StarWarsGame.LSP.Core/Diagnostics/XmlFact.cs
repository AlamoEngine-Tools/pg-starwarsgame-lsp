// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Base for all XML document observations. Carries position info; makes no judgement about severity.
/// </summary>
public abstract record XmlFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length);