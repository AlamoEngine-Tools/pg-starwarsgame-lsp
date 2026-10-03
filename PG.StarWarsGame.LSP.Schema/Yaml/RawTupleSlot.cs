// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>A parsed, not yet resolved tuple slot: names only, no type or enum lookups.</summary>
internal sealed record RawTupleSlot(string Label, ReferenceKind ReferenceKind, string? ReferenceType, string? EnumName);
