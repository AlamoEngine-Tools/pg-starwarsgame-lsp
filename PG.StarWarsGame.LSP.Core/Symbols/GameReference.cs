// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

public sealed record GameReference(
    string TargetId,
    GameSymbolKind? ExpectedKind,
    string? ExpectedTypeName,
    string DocumentUri,
    int Line,
    int Column,
    int Length
)
{
    /// <summary>
    ///     The type whose id space the target is looked up in, when that is not
    ///     <see cref="ExpectedTypeName" />: a tag that names an object KIND expects the kind, which is
    ///     judged by behaviour, and looks the id up among game objects.
    /// </summary>
    public string? LookupType { get; init; }

    /// <summary>Where the target is looked up: <see cref="LookupType" />, else the expected type.</summary>
    public string? IdSpace => LookupType ?? ExpectedTypeName;
}