// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>Shared context passed to every <see cref="IXmlDiagnosticsHandler" /> invocation.</summary>
public record DiagnosticsContext(
    ISchemaProvider Schema,
    GameIndex Index,
    string DocumentUri,
    string Locale);