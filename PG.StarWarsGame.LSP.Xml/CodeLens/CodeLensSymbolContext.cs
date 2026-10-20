// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

public sealed class CodeLensSymbolContext
{
    public CodeLensSymbolContext(GameSymbol symbol, FileOrigin origin, GameIndex index)
    {
        Symbol = symbol;
        Origin = origin;
        Index = index;
    }

    /// <summary>The server's texts in the configured locale; English when nothing set it.</summary>
    public IMessageCatalog Messages { get; init; } = ResxMessageCatalog.Server("en");

    public GameSymbol Symbol { get; }
    public FileOrigin Origin { get; }
    public GameIndex Index { get; }
}