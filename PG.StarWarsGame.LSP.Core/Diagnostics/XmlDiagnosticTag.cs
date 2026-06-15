// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Editor-facing metadata tag for a diagnostic, mirroring the LSP <c>DiagnosticTag</c> values.
///     <see cref="Unnecessary" /> tells the client to fade/grey out the flagged text;
///     <see cref="Deprecated" /> renders it with a strike-through.
/// </summary>
public enum XmlDiagnosticTag
{
    Unnecessary,
    Deprecated
}
