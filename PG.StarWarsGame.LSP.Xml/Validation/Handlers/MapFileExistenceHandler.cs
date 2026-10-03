// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A named map that is not there is an Error, not a Warning (issue #132).
/// </summary>
/// <remarks>The measured branch behind the severity is recorded with the rule, in <see cref="AssetKindRules" />.</remarks>
public sealed class MapFileExistenceHandler : AssetFileExistenceHandlerBase
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.MapFileExistence;

    protected override ReferenceKind TargetKind => ReferenceKind.MapFile;
}