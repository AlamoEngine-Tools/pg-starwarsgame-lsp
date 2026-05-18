// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

[Union(0, typeof(FileOrigin))]
[Union(1, typeof(MegArchiveOrigin))]
[Union(2, typeof(UnknownOrigin))]
[MessagePackObject]
public abstract record SymbolOrigin;
