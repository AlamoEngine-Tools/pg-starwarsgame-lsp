// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MessagePack;

namespace PG.StarWarsGame.LSP.Core.Symbols;

[MessagePackObject]
public sealed record FileOrigin(
    [property: Key(0)] string Uri,
    [property: Key(1)] int Line,
    [property: Key(2)] int? Column
) : SymbolOrigin
{
    /// <summary>
    ///     True when <see cref="Uri" /> is a real editor-openable <c>file://</c> URI. Baseline symbols
    ///     projected from shipped game files carry a game-relative path (e.g. <c>DATA\XML\Foo.xml</c>)
    ///     that the editor cannot open, so navigation handlers must not emit a location for them.
    /// </summary>
    [IgnoreMember]
    public bool IsNavigable => Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
}